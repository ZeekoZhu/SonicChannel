namespace SonicChannel

open System
open Configuration
open System.Buffers
open System.IO
open System.IO.Pipelines
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open Microsoft.Extensions.Logging

module Util =
    let readLinesAsync (stream: NetworkStream) (transceiverOption: TransceiverOption) (onLineReceived: string -> unit Task) (ct: CancellationToken) =
        let bufferSize = transceiverOption.BufferSize
        let encoding = transceiverOption.Encoding
        let pipe = Pipe()
        let writer = pipe.Writer
        let reader = pipe.Reader
        let fillPipe () =
            task {
                while not ct.IsCancellationRequested do
                    let memory = writer.GetMemory(bufferSize)
                    if stream.DataAvailable
                    then
                        let! bytesRead = stream.ReadAsync (memory, ct)
                        if bytesRead <> 0 then
                            writer.Advance bytesRead
                            let! _ = writer.FlushAsync(ct)
                            ()
                    else
                        do! Task.Delay(TimeSpan.FromMilliseconds(10.0))
            }
        let readPipe () =
            let readLine (bytes: byte array) =
                let str = encoding.GetString bytes
                str.TrimEnd ('\r')
            task {
                let mutable completed = false
                while not completed do
                    let! readResult = reader.ReadAsync(ct)
                    let buffer = readResult.Buffer
                    let position = buffer.PositionOf(byte '\n')
                    if position.HasValue then
                        do! buffer.Slice(0, position.Value).ToArray() |> readLine |> onLineReceived
                        reader.AdvanceTo (buffer.GetPosition(1L, position.Value))
                    completed <- readResult.IsCompleted
            }
        let fillTask = fillPipe ()
        let readTask = readPipe ()
        Task.WhenAll (fillTask, readTask)
        |> Task.map ignore

    let writeLineAsync (writer: StreamWriter) (text: string) cancellationToken =
        let buffer = text.AsMemory()
        writer.WriteLineAsync (buffer, cancellationToken)


type TransceiverContext =
    { Stream: NetworkStream
      Writer: StreamWriter
      CommandQueue: CommandQueue
    }
    with member this.Dispose() =
            this.Writer.Dispose()
            (this.CommandQueue :> IDisposable).Dispose()
type MessageTransceiver
    (
        client: TcpClient,
        optionReader: IOptionReader,
        loggerFactory: ILoggerFactory
    ) =
    let opt = optionReader.TransceiverOption()
    let logger = loggerFactory.CreateLogger<MessageTransceiver>()
    let mutable disposed = false
    let mutable state : TransceiverContext option = None
    let ensureInitialized () =
        Option.getOrRaise (InvalidOperationException("Transceiver not initialized")) state
    let bufferSize = opt.BufferSize
    let encoding = opt.Encoding
    let cts = new CancellationTokenSource()

    let disconnect () =
        if not disposed && client.Connected then
            cts.Cancel()
            state
            |> Option.map (fun x -> x.CommandQueue)
            |> Option.iter
                (fun x -> x.CancelAllTask())
            client.Close()
    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                cts.Dispose()
                match state with
                | Some x -> x.Dispose()
                | _ -> ()
    let onMsgReceived (msg: string) =
        let state = ensureInitialized ()
        logger.LogDebug("Received: {Message}", msg)
        state.CommandQueue.OnResponseArrived msg
        Task.FromResult ()
    member _.InitializeAsync () =
        let connOpt = optionReader.ConnectionOption()
        task {
            if not client.Connected then
                do! client.ConnectAsync(connOpt.Host, connOpt.Port)
            let stream = client.GetStream()
            let writer = new StreamWriter(stream, encoding, bufferSize, true)
            let sendMsgFn (msg: string) =
                logger.LogDebug("Send: {Message}", msg)
                if msg = "NO EXEC" then Task.FromResult()
                else
                    task {
                        do! writer.WriteLineAsync msg
                        do! writer.FlushAsync()
                    }
            let commandQueue = new CommandQueue(sendMsgFn, loggerFactory, optionReader)
            commandQueue.OnQuit.Add disconnect
            let receiveMsgTask () =
                Util.readLinesAsync
                    stream
                    (optionReader.TransceiverOption())
                    onMsgReceived
                    cts.Token
            Task.Run<unit> (Func<Task<unit>> receiveMsgTask)
            |> ignore
            state <- Some { Stream = stream; Writer = writer; CommandQueue = commandQueue }
        }
    member _.SendAsync(cmd) =
        let state = ensureInitialized ()
        state.CommandQueue.ExecuteCommandAsync cmd
    interface IDisposable with
        member this.Dispose() =
            cleanup true
            GC.SuppressFinalize this

