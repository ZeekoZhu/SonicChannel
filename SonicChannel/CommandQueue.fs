namespace SonicChannel
open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open Microsoft.Extensions.Logging
open SonicChannel.Commands
open SonicChannel.SonicCommand
type CSList<'a> = System.Collections.Generic.List<'a>

type SonicCommandCallback =
    { Command: ISonicCommand; Callback: TaskCompletionSource<ISonicCommand>; }

type CommandQueue
    (
        sendMsgFn: string -> Task<unit>,
        logger: ILogger<CommandQueue>
    ) =
    let mutable disposed = false
    let mutable waiting: SonicCommandCallback option = None
    let mutable mailbox: MailboxProcessor<_> option = None
    let ensureInitialized () =
        Option.getOrRaise (InvalidOperationException("Transceiver not initialized")) mailbox
    let waitingLock = new SemaphoreSlim(1, 1)
    let pendingQueue = CSList<SonicCommandCallback>()
    let onQuit = Event<unit>()
    let cancelAllTasks () =
        match waiting with
        | Some cb -> cb.Callback.SetCanceled()
        | None -> ()
        pendingQueue
        |> Seq.iter (fun cb -> cb.Callback.SetCanceled())
    do onQuit.Publish.Add cancelAllTasks
    let setWaiting cb =
        match waiting with
        | None -> waiting <- Some cb
        | Some _ ->
            logger.LogError "Other command is waiting"
            failwith "Other command is waiting"
    let filterMsg (line: string) fn =
        if line.StartsWith("CONNECTED", StringComparison.OrdinalIgnoreCase) then
            ()
        fn line
    let onResponseArrived (line: string) =
        let handleMessage (cb: SonicCommandCallback) (handleFn: string -> MessageHandleResult) =
            let handleResult = handleFn line
            match handleResult with
            | Bypass -> None
            | Handled state ->
                Some (cb, state)

        let checkWaiting () =
            waiting
            |> Option.bind (fun waiting -> handleMessage waiting waiting.Command.HandleWaitingMsg)

        let checkPendingQueue () =
            let cb =
                pendingQueue
                |> Seq.map (fun cb -> handleMessage cb cb.Command.HandlePendingMsg)
                |> Seq.tryFind (Option.isSome)
                |> Option.bind id
            cb |> Option.iter (fun (x, _) -> pendingQueue.Remove x |> ignore)
            cb

        logger.LogDebug("CQ checking {msg}", line)
        let waitingHandled = checkWaiting ()
        waitingHandled
        |> Option.iter (fun _ ->
            waiting <- None
            waitingLock.Release() |> ignore
            logger.LogDebug ("Release waiting CNT: {0}", waitingLock.CurrentCount)
        )
        waitingHandled
        |> Option.orElseWith checkPendingQueue
        |> function
        | None ->
            printfn "unhandled %s" line
            if line.StartsWith ("ENDED", StringComparison.OrdinalIgnoreCase) then
                logger.LogWarning ("Connection closed by host: {Message}", line)
                onQuit.Trigger ()
            else
                logger.LogWarning ("Not handled: {Message}", line)
        | Some (cb, state) ->
            match state with
            | Finished ->
                logger.LogDebug ("Command finished: {0}",(cb.Command.ToCommandString()))
                cb.Callback.SetResult cb.Command
                match cb.Command with
                | :? QuitCommand ->
                    onQuit.Trigger()
                | _ -> ()
            | Pending ->
                pendingQueue.Add cb

    let receive (inbox: MailboxProcessor<ISonicCommand * AsyncReplyChannel<TaskCompletionSource<_>>>) =
        let receiveCmdTask =
            task {
                while not disposed do
                    let! (cmd, ch) = inbox.Receive()
                    logger.LogDebug("Received CMD: {Message}", cmd.ToCommandString())
                    let cb = {
                        Command = cmd
                        Callback = TaskCompletionSource()
                    }
                    logger.LogDebug ("try set waiting, CNT {0}", waitingLock.CurrentCount)
                    do! waitingLock.WaitAsync()
                    logger.LogDebug ("set waiting")
                    setWaiting cb
                    let msg = cmd.ToCommandString()
                    do! sendMsgFn msg
                    ch.Reply cb.Callback
                return ()
            }
        receiveCmdTask
        |> Async.AwaitTask

    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                logger.LogDebug("Command Queue disposed")
                if waitingLock.CurrentCount = 0 then
                    waitingLock.Release() |> ignore
                waitingLock.Dispose()

    member _.Initialize() =
        mailbox <- MailboxProcessor.Start receive |> Some

    [<CLIEvent>]
    member _.OnQuit = onQuit.Publish

    member _.OnResponseArrived msg =
        filterMsg msg onResponseArrived

    member _.ExecuteCommandAsync cmd =
        let mailbox = ensureInitialized ()
        task {
            let! task = mailbox.PostAndAsyncReply(fun ch -> cmd, ch)
            return! task.Task
        }
    member _.CancelAllTask () =
        cancelAllTasks()
    interface IDisposable with
        override this.Dispose() =
            cleanup true
            GC.SuppressFinalize this
