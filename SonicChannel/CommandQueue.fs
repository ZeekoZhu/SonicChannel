namespace SonicChannel
open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open Microsoft.Extensions.Logging
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
    let onResponseArrived (line: string) =
        let handleMessage (cb: SonicCommandCallback) (handleFn: string -> MessageHandleResult) =
            let handleResult = handleFn line
            match handleResult with
            | Bypass -> None
            | Handled state ->
                match state with
                | Pending ->
                    pendingQueue.Add cb
                    waiting <- None
                | Finished ->
                    waiting <- None
                Some cb

        let checkWaiting () =
            waiting
            |> Option.bind (fun waiting -> handleMessage waiting waiting.Command.HandleWaitingMsg)

        let checkPendingQueue () =
            let cb =
                pendingQueue
                |> Seq.map (fun cb -> handleMessage cb cb.Command.HandlePendingMsg)
                |> Seq.tryFind (Option.isSome)
                |> Option.bind id
            cb |> Option.iter (fun x -> pendingQueue.Remove x |> ignore)
            cb

        checkWaiting ()
        |> Option.orElseWith checkPendingQueue
        |> function
        | None -> logger.LogWarning ("Not handled: {Message}", line)
        | Some cb -> cb.Callback.SetResult cb.Command

    let acquireWaitingLock fn p =
        task {
            do! waitingLock.WaitAsync()
            let result = fn p
            waitingLock.Release() |> ignore
            return result
        }
    let setWaiting cb =
        acquireWaitingLock
            ( fun () ->
                match waiting with
                | None -> waiting <- Some cb
                | Some _ -> failwith "Other command is waiting"
            ) ()
    let receive (inbox: MailboxProcessor<ISonicCommand * AsyncReplyChannel<TaskCompletionSource<_>>>) =
        let receiveCmdTask =
            task {
                while not disposed do
                    let! (cmd, ch) = inbox.Receive()
                    let cb = {
                        Command = cmd
                        Callback = TaskCompletionSource()
                    }
                    do! setWaiting cb
                    let msg = cmd.ToCommandString()
                    logger.LogDebug("Execute: {Message}", msg)
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
                waitingLock.Dispose()

    member _.Initialize() =
        mailbox <- MailboxProcessor.Start receive |> Some

    member _.OnResponseArrived msg =
        acquireWaitingLock onResponseArrived msg

    member _.ExecuteCommandAsync cmd =
        let mailbox = ensureInitialized ()
        task {
            let! task = mailbox.PostAndAsyncReply(fun ch -> cmd, ch)
            return! task.Task
        }
    interface IDisposable with
        override this.Dispose() =
            cleanup true
            GC.SuppressFinalize this
