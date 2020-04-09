namespace SonicChannel

open System
open System.IO.Pipelines
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Configuration
open System.Buffers
open System.IO
open System.Threading
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open Microsoft.Extensions.Logging

module Util =
    type Deps =
        { Encoding: Encoding
          BufferSize: int
          CancellationToken: CancellationToken
          ProcessMessage: string -> unit
          Logger: ILogger
        }
    let readLine (bytes: byte array) (deps: Deps) =
        let str = deps.Encoding.GetString bytes
        str.TrimEnd ('\r')
    let readPipe (reader: PipeReader) (deps: Deps) =
        let ct = deps.CancellationToken
        let readLine p = readLine p deps
        let onLineReceived = deps.ProcessMessage
        task {
            let mutable completed = false
            try
                while not ct.IsCancellationRequested && not completed do
                    let readTask = reader.ReadAsync(ct)
                    let! readResult = readTask
                    completed <- readResult.IsCompleted
                    let buffer = readResult.Buffer
                    if not completed && buffer.Length > 0L then
                        let position = buffer.PositionOf(byte '\n')
                        if position.HasValue then
                            let bufferRead = buffer.Slice(0, position.Value).ToArray()
                            bufferRead |> readLine |> onLineReceived
                            reader.AdvanceTo (buffer.GetPosition(1L, position.Value))
                        else
                            reader.AdvanceTo (buffer.Start, buffer.End)
                    else
                        do! Task.Yield()
                reader.Complete()
            with _ as e ->
                reader.Complete(e)
                deps.Logger.LogInformation (e, "Unable to read data from socket")
        }
    let readLinesAsync (stream: NetworkStream) (deps: Deps) =
        let reader = PipeReader.Create(stream, StreamPipeReaderOptions(bufferSize = deps.BufferSize, leaveOpen = true))
        readPipe reader deps
        |> Async.AwaitTask

    let writeLineAsync (writer: StreamWriter) (text: string) cancellationToken =
        let buffer = text.AsMemory()
        writer.WriteLineAsync (buffer, cancellationToken)


type TransceiverContext =
    { Stream: NetworkStream
      Writer: StreamWriter
      CommandQueue: CommandQueue
      MessageSender: MailboxProcessor<string>
    }
    with member this.Dispose() =
            this.Writer.Dispose()
            (this.CommandQueue :> IDisposable).Dispose()
type MessageTransceiver
    (
        optionReader: IOptionReader,
        loggerFactory: ILoggerFactory
    ) =
    let socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
    let opt = optionReader.TransceiverOption()
    let logger = loggerFactory.CreateLogger<MessageTransceiver>()
    let mutable disposed = false
    let mutable state : TransceiverContext option = None
    let ensureInitialized () =
        Option.getOrRaise (InvalidOperationException("Transceiver not initialized")) state
    let bufferSize = opt.BufferSize
    let encoding = opt.Encoding
    let cts = new CancellationTokenSource()

    let sendMsg (writer: StreamWriter) (inbox: MailboxProcessor<string>) =
        let rec sendLoop () =
            async {
                let! msg = inbox.Receive()
                do! writer.WriteLineAsync msg |> Async.AwaitTask
                do! writer.FlushAsync() |> Async.AwaitTask
                return! sendLoop()
            }
        sendLoop()
    let disconnect () =
        if not socket.Connected then
            cts.Cancel()
            state
            |> Option.map (fun x -> x.CommandQueue)
            |> Option.iter
                (fun x -> x.CancelAllTask())
            socket.Shutdown(SocketShutdown.Both)
            socket.Close()
    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                match state with
                | Some x -> x.Dispose()
                | _ -> ()
                cts.Cancel()
                disconnect ()
                cts.Dispose()
                socket.Dispose()
    let onMsgReceived (msg: string) =
        let state = ensureInitialized ()
        state.CommandQueue.OnResponseArrived msg
    member _.InitializeAsync () =
        let connOpt = optionReader.ConnectionOption()
        let transOpt = optionReader.TransceiverOption()
        task {
            if not socket.Connected then
                do! socket.ConnectAsync(connOpt.Host, connOpt.Port)
            let stream = new NetworkStream(socket, false)
            let writer = new StreamWriter(stream, encoding, bufferSize, true)
            let msgSender = MailboxProcessor.Start (sendMsg writer, cts.Token)
            msgSender.Error.Add (fun err -> logger.LogError (err, "Transceiver Error"))
            let commandQueue = new CommandQueue(msgSender, loggerFactory, optionReader)
            commandQueue.OnQuit.Add disconnect
            state <- Some {
                Stream = stream
                Writer = writer
                CommandQueue = commandQueue
                MessageSender = msgSender
            }
            Util.readLinesAsync
                stream
                { Encoding = transOpt.Encoding
                  BufferSize = transOpt.BufferSize
                  Logger = loggerFactory.CreateLogger(typeof<MessageTransceiver>)
                  CancellationToken = cts.Token
                  ProcessMessage = onMsgReceived
                }
            |> Async.Start
        }
    member _.SendAsync(cmd) =
        let state = ensureInitialized ()
        state.CommandQueue.ExecuteCommandAsync cmd
    interface IDisposable with
        member this.Dispose() =
            cleanup true
            GC.SuppressFinalize this

