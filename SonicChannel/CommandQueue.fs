namespace SonicChannel
open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Timers
open Configuration
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open FSharpx.Collections
open Microsoft.Extensions.Logging
open SonicChannel.SonicCommand
open SonicChannel.Commands
type CSList<'a> = System.Collections.Generic.List<'a>

type SonicCommandCallback =
    {
        Command: ISonicCommand
        Callback: TaskCompletionSource<ISonicCommand>
        mutable Marked: bool
    }

type CommandQueueAction =
    | SetWaiting of SonicCommandCallback
    | WaitingHandled
    | TimeoutAndMark
    | CancelAll
    | RemovePending of SonicCommandCallback
    | AddPending of SonicCommandCallback
    | Complete of SonicCommandCallback
    | Message of string
    | Quit

type CommandQueueCore
    (
        ct: CancellationToken,
        loggerFactory: ILoggerFactory
    ) =
    let logger = loggerFactory.CreateLogger<CommandQueueCore>()
    let msgProcessor =
       SessionMessageProcessor
           (loggerFactory.CreateLogger<SessionMessageProcessor>())
    let waitingQueue = Queue<SonicCommandCallback>()
    let waiting =
        Option.fromTryPattern (waitingQueue.TryPeek)
    let mutable pendingList: SonicCommandCallback list = []
    let onQuit = Event<unit>()
    let runningCommands () =
        seq {
            let any, waiting = waitingQueue.TryPeek()
            if any then yield waiting
            yield! pendingList
        }
    let setWaiting (cb: SonicCommandCallback) =
        waitingQueue.Enqueue cb
        logger.LogDebug <| sprintf "set waiting %s" (cb.Command.ToCommandString())
        Seq.empty

    let removeMarked () =
        match waiting() with
        | Some x when x.Marked ->
            waitingQueue.Dequeue () |> ignore
        | _ -> ()
        let marked =
            pendingList
            |> List.filter (fun x -> not x.Marked)
        pendingList <- marked
    let timeoutAndMark () =
        let cancelMarked (cb) =
            if cb.Marked then
                cb.Callback.TrySetCanceled()
                |> ignore
        let mark cb =
            cb.Marked <- true
        runningCommands ()
        |> Seq.iter cancelMarked
        removeMarked ()
        runningCommands ()
        |> Seq.iter mark
        Seq.empty
    let cancelAll () =
        let cancelCb cb =
            cb.Callback.TrySetCanceled() |> ignore
        waitingQueue
        |> Seq.iter (cancelCb)
        pendingList
        |> Seq.iter (cancelCb)
        waitingQueue.Clear()
        pendingList <- []
        Seq.empty
    let removePending cb =
        pendingList <- pendingList |> List.filter (fun x -> cb <> x)
        Seq.empty
    let addPending cb =
        pendingList <- cb :: pendingList
        Seq.empty
    let waitingHandled () =
        waitingQueue.Dequeue() |> ignore
        Seq.empty
    let complete cb =
        match cb.Command with
        | :? QuitCommand -> onQuit.Trigger()
        | _ -> ()
        cb.Callback.SetResult cb.Command
        Seq.empty
    let quit () =
        onQuit.Trigger ()
        Seq.empty
    let message str =
        msgProcessor.ProcessMessage (waiting(), pendingList |> Seq.ofList) str
        |> Option.getOrElse Seq.empty
    let rec handleAction (act: CommandQueueAction) =
        logger.LogDebug <| sprintf "Handle action %A" act
        match act with
        | SetWaiting value -> setWaiting value
        | WaitingHandled -> waitingHandled ()
        | TimeoutAndMark -> timeoutAndMark ()
        | CancelAll -> cancelAll ()
        | RemovePending cb -> removePending cb
        | AddPending cb -> addPending cb
        | Complete cb -> complete cb
        | Quit -> quit()
        | Message str -> message str
        |> Seq.iter handleAction
    let mailbox =
        MailboxProcessor.Start
            ( fun (inbox: MailboxProcessor<CommandQueueAction * AsyncReplyChannel<unit>>) ->
                let rec msgLoop () =
                    async {
                        let! (action, reply) = inbox.Receive()
                        handleAction action
                        reply.Reply()
                        return! msgLoop()
                    }
                msgLoop()
            , ct)
    member _.Dispatch (act: CommandQueueAction) =
        mailbox.PostAndReply(fun rc -> act, rc)
    member _.Waiting
        with get () : SonicCommandCallback option = waiting ()
    member _.PendingList
        with get () : SonicCommandCallback seq = pendingList |> Seq.ofList
    [<CLIEvent>]
    member _.OnQuit = onQuit.Publish

and SessionMessageProcessor
    (
        logger: ILogger<SessionMessageProcessor>
    ) =
    let filterMsg (line: string) =
        line.StartsWith("CONNECTED", StringComparison.OrdinalIgnoreCase) = false
    let processLine (waiting, pendingList) (line: string) =
        let result = CSList<CommandQueueAction>()
        let dispatch a = result.Add a
        let handleMessage (cb: SonicCommandCallback) (handleFn: string -> MessageHandleResult) =
            let handleResult = handleFn line
            match handleResult with
            | Ignore -> None
            | Handled state ->
                Some (cb, state)

        let handleWaiting (cb, state) =
            dispatch WaitingHandled
            logger.LogDebug ("Waiting handled {msg}", line)
            match state with
            | Finished ->
                dispatch (Complete cb)
            | Pending ->
                dispatch (AddPending cb)
        let checkWaiting () =
            let handled =
                waiting
                |> Option.bind (fun waiting -> handleMessage waiting waiting.Command.HandleWaitingMsg)
            match handled with
            | Some x -> handleWaiting x
            | None -> ()
            handled

        let handlePending (cb, state) =
            match state with
            | Finished ->
                dispatch (Complete cb)
                dispatch (RemovePending cb)
            | _ -> raise (InvalidOperationException("Pending command should finished"))
        let checkPendingQueue () =
            let handled =
                pendingList
                |> Seq.map (fun cb -> handleMessage cb cb.Command.HandlePendingMsg)
                |> Seq.tryFind (Option.isSome)
                |> Option.bind id
            match handled with
            | Some x -> handlePending x
            | None -> ()
            handled

        let checkQuit () =
            if line.StartsWith ("ENDED", StringComparison.OrdinalIgnoreCase) then
                dispatch Quit
            else
                logger.LogWarning ("Not handled: {Message}", line)

        checkWaiting ()
        |> Option.orElseWith ( fun () -> checkPendingQueue ())
        |> function
            | Some _ -> ()
            | None -> checkQuit ()
        result :> _ seq

    member _.ProcessMessage context (msg: string) =
        Some msg
        |> Option.filter filterMsg
        |> Option.map (processLine context)

type CommandQueue
    (
        sendMsgFn: string -> Task<unit>,
        loggerFactory: ILoggerFactory,
        optReader: IOptionReader
    ) =
    let opt = optReader.TransceiverOption()
    let logger = loggerFactory.CreateLogger<CommandQueue>()
    let mutable disposed = false
    let cts = new CancellationTokenSource()
    let coreQueue = CommandQueueCore(cts.Token, loggerFactory)
    let cancelAllTasks () =
        coreQueue.Dispatch CancelAll
    let onQuitEvent = Event<unit>()
    do coreQueue.OnQuit.Add <| fun () ->
        cancelAllTasks()
        onQuitEvent.Trigger()
    let timeout () =
        coreQueue.Dispatch TimeoutAndMark
    let setupTimeout () =
        let timer = new Timer(float opt.Timeout.Milliseconds)
        timer.Elapsed.Add (fun _ -> timeout())
        timer.AutoReset <- true
        timer.Start()
        timer
    let timer = setupTimeout()
    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                logger.LogDebug("Command Queue disposed")
                timer.Stop()
                timer.Dispose()
                cts.Dispose()

    member _.OnResponseArrived msg =
        coreQueue.Dispatch <| Message msg

    member _.ExecuteCommandAsync cmd =
        task {
            let cb = {
                Command = cmd
                Callback = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                Marked = false
            }
            coreQueue.Dispatch (SetWaiting cb)
            let msg = cmd.ToCommandString()
            do! sendMsgFn msg
            return! cb.Callback.Task
        }
    [<CLIEvent>]
    member _.OnQuit = onQuitEvent.Publish
    member _.CancelAllTask () =
        cancelAllTasks()
    interface IDisposable with
        override this.Dispose() =
            cleanup true
            GC.SuppressFinalize this
