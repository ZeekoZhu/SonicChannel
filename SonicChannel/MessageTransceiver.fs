namespace SonicChannel

open Configuration
open System
open System.Buffers
open System.IO
open System.IO.Pipelines
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx

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


type TransceiverContext =
    { Stream: NetworkStream
      Writer: StreamWriter
      CommandQueue: CommandQueue
    }
    with member this.Dispose() =
            this.Writer.Dispose()
            (this.CommandQueue :> IDisposable).Dispose()
type MessageTransceiver(client: TcpClient, optionReader: IOptionReader) =
    let opt = optionReader.TransceiverOption()
    let mutable disposed = false
    let mutable state : TransceiverContext option = None
    let ensureInitialized () =
        Option.getOrRaise (InvalidOperationException("Transceiver not initialized")) state
    let bufferSize = opt.BufferSize
    let encoding = opt.Encoding
    let cts = new CancellationTokenSource()

    let cleanup (disposing: bool) =
        if not disposed then
            disposed <- true
            if disposing then
                cts.Cancel()
                cts.Dispose()
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
            let sendMsgFn (msg: string) =
                writer.WriteLineAsync msg
                |> Task.ToTaskUnit
            let commandQueue = new CommandQueue(sendMsgFn)
            commandQueue.Initialize()
            let receiveMsgTask () =
                Util.readLinesAsync
                    stream
                    (optionReader.TransceiverOption())
                    commandQueue.OnResponseArrived
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

