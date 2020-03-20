namespace SonicChannel

open Configuration
open System
open System.Buffers
open System.IO
open System.IO.Pipelines
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open SonicChannel.SonicCommand

type CSList<'a> = System.Collections.Generic.List<'a>

module Util =
    let readLinesAsync (stream: Stream) (transceiverOption: TransceiverOption) (onLineReceived: string -> unit Task) (ct: CancellationToken) =
        let bufferSize = transceiverOption.BufferSize
        let encoding = transceiverOption.Encoding
        let pipe = Pipe()
        let writer = pipe.Writer
        let reader = pipe.Reader
        let fillPipe () =
            task {
                let mutable flag = true
                while flag && not ct.IsCancellationRequested do
                    let memory = writer.GetMemory(bufferSize)
                    let! bytesRead = stream.ReadAsync (memory, ct)
                    if bytesRead = 0
                    then flag <- false
                    else
                        writer.Advance bytesRead
                        let! flushResult = writer.FlushAsync(ct)
                        if flushResult.IsCompleted
                        then flag <- false
            }
        let readPipe () =
            let readLine (bytes: byte array) =
                encoding.GetString bytes
            task {
                let mutable completed = false
                while not completed do
                    let! readResult = reader.ReadAsync(ct)
                    let buffer = readResult.Buffer
                    let position = buffer.PositionOf(byte '\n')
                    if position.HasValue then
                        do! buffer.Slice(0, position.Value).ToArray() |> readLine |> onLineReceived
                        reader.AdvanceTo (buffer.GetPosition(1L, position.Value), buffer.End)
                    completed <- readResult.IsCompleted
            }
        let fillTask = fillPipe ()
        let readTask = readPipe ()
        Task.WhenAll (fillTask, readTask)
        |> Task.map ignore

    let writeLineAsync (writer: StreamWriter) (text: string) cancellationToken =
        let buffer = text.AsMemory()
        writer.WriteLineAsync (buffer, cancellationToken)


type SonicCommandCallback =
    { Command: ISonicCommand; Callback: TaskCompletionSource<ISonicCommand>; }
type TransceiverContext =
    { Stream: NetworkStream
      Writer: StreamWriter
      MsgSender: MailboxProcessor<ISonicCommand * AsyncReplyChannel<TaskCompletionSource<ISonicCommand>>> }
    with member this.Dispose() =
            this.Writer.Dispose()
type MessageTransceiver(client: TcpClient, optionReader: IOptionReader) =
    let opt = optionReader.TransceiverOption()
    let mutable disposed = false
    let mutable waiting: SonicCommandCallback option = None
    let mutable state : TransceiverContext option = None
    let ensureInitialized () =
        Option.getOrRaise (InvalidOperationException("Transceiver not initialized")) state
    let waitingLock = new SemaphoreSlim(1, 1)
    let bufferSize = opt.BufferSize
    let encoding = opt.Encoding
    let pendingQueue = CSList<SonicCommandCallback>()
    let cts = new CancellationTokenSource()
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
        | None -> printfn "Warning: message not handled"
        | Some cb -> cb.Callback.SetResult cb.Command

    let aquireWaitingLock fn p =
        task {
            do! waitingLock.WaitAsync()
            let result = fn p
            waitingLock.Release() |> ignore
            return result
        }
    let setWaiting cb =
        aquireWaitingLock
            ( fun () ->
                match waiting with
                | None -> waiting <- Some cb
                | Some _ -> failwith "Other command is waiting"
            ) ()
    let receive (inbox: MailboxProcessor<ISonicCommand * AsyncReplyChannel<TaskCompletionSource<_>>>) =
        let state = ensureInitialized()
        let stream = state.Stream
        let writer = state.Writer
        let receiveMsgTask =
            Util.readLinesAsync
                stream
                (optionReader.TransceiverOption())
                (aquireWaitingLock onResponseArrived)
                cts.Token
        let receiveCmdTask =
            task {
                while not disposed do
                    let! (cmd, ch) = inbox.Receive()
                    let cb = {
                        Command = cmd
                        Callback = TaskCompletionSource()
                    }
                    do! setWaiting cb
                    do! writer.WriteLineAsync (cmd.ToString())
                    ch.Reply cb.Callback
                return ()
            }
        Task.WhenAll (receiveCmdTask, receiveMsgTask)
        |> Task.Ignore
        |> Async.AwaitTask
    let sendAsync (cmd: ISonicCommand) =
        let state = ensureInitialized ()
        let msgSender = state.MsgSender
        task {
            let! task = msgSender.PostAndAsyncReply(fun ch -> cmd, ch)
            return! task.Task
        }
    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                cts.Cancel()
                cts.Dispose()
                waitingLock.Dispose()
                match state with
                | Some x -> x.Dispose()
                | _ -> ()
    member _.InitializeAsync () =
        let connOpt = optionReader.ConnectionOption()
        task {
            if not client.Connected then
                do! client.ConnectAsync(connOpt.Host, connOpt.Port)
            let stream = client.GetStream()
            let writer = new StreamWriter(stream, encoding, bufferSize, true)
            writer.AutoFlush <- true
            let msgSender = MailboxProcessor.Start receive
            state <- Some { Stream = stream; Writer = writer; MsgSender = msgSender }
        }
    member _.SendAsync(cmd) =
        sendAsync cmd
    interface IDisposable with
        member this.Dispose() =
            cleanup true
            GC.SuppressFinalize this

