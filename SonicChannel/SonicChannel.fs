namespace SonicChannel

open System
open System.Threading.Tasks
open Commands.ControlCommand
open Commands.IngestCommands
open Commands.QueryCommands
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharpx
open Microsoft.Extensions.Logging
open SonicChannel.Commands
open SonicChannel.Configuration
open SonicChannel.SonicCommand

[<AbstractClass>]
type SonicChannel
    (
        optionReader: IOptionReader,
        loggerFactory: ILoggerFactory
    ) as this =
    let connOpt = optionReader.ConnectionOption ()
    let mutable disposed = false
    let mutable started = false;
    let transceiver = new MessageTransceiver(optionReader, loggerFactory)
    let cleanup disposing =
        if not disposed then
            disposed <- true
            if disposing then
                (transceiver :> IDisposable).Dispose()
    member _.Transceiver
        with get (): MessageTransceiver = transceiver
    member val Config: ChannelConfig option = None with get, set
    member internal _.EnsureStarted () =
        this.Config |> Option.getOrRaise (InvalidOperationException("This channel is not started"))
    member internal this.StartAsync (mode: ChannelMode) =
        if not started then
            started <- true
            let cmd = StartCommand(mode, connOpt.AuthPassword)
            task {
                do! transceiver.InitializeAsync()
                let! _ = transceiver.SendAsync(cmd)
                this.Config <- cmd.Result
            }
        else Task.FromResult ()
    abstract member StartAsync: unit -> Task<unit>
    member _.QuitAsync() =
        let cmd = QuitCommand()
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return ()
        }
    interface IDisposable with
        member this.Dispose () =
            cleanup true
            GC.SuppressFinalize this

type IngestChannel(optionReader, loggerFactory) as this =
    inherit SonicChannel(optionReader, loggerFactory)
    let transOpt = optionReader.TransceiverOption()
    let splitText text =
        let config = this.EnsureStarted()
        CommandTextBuilder.splitTextChunks (config.BufferSize) (transOpt.Encoding) text
    override _.StartAsync() =
        base.StartAsync(ChannelMode.Ingest)
    member this.PushAsync
        (
            collection: string,
            bucket: string,
            object: string,
            text: string,
            lang: string option
        )=
        task {
            for chunk in splitText (text.Trim()) do
                let cmd = PushCommand(collection, bucket, object, chunk, lang)
                let! _ = this.Transceiver.SendAsync(cmd)
                ()
        }
    member this.PopAsync
        (
            collection: string,
            bucket: string,
            object: string,
            text: string
        )=
        task {
            let mutable cnt = 0
            for chunk in splitText (text.Trim()) do
                let cmd = PopCommand(collection, bucket, object, chunk)
                let! _ = this.Transceiver.SendAsync(cmd)
                cnt <- cnt + cmd.Result.Value
            return cnt
        }

    member _.CountAsync
        (collection: string, bucket: string option, object: string option) =
        let cmd = CountCommand(collection, bucket, object)
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return cmd.Result.Value
        }
    member _.FlushAsync
        (collection: string, bucketAndObject: (string * (string option)) option) =
        let cmd = FlushCommand(collection, bucketAndObject)
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return cmd.Result.Value
        }

type SearchChannel(optionReader, loggerFactory) as this =
    inherit SonicChannel(optionReader, loggerFactory)
    override _.StartAsync() =
        base.StartAsync(ChannelMode.Search)
    member _.QueryAsync
        (
             collection: string,
             bucket: string,
             terms: string,
             limit: int option,
             offset: int option,
             lang: string option
        ) =
        let cmd = QueryCommand(collection, bucket, terms, limit, offset, lang)
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return cmd.Result.Value
        }
    member _.SuggestAsync
        (
             collection: string,
             bucket: string,
             word: string,
             limit: int option
        ) =
        let cmd = SuggestCommand(collection, bucket, word, limit)
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return cmd.Result.Value
        }

type ControlChannel(optionReader, loggerFactory) as this =
    inherit SonicChannel(optionReader, loggerFactory)
    override _.StartAsync() =
        base.StartAsync(ChannelMode.Control)
    member _.TriggerAsync
        (
            action: string,
            data: string option
        ) =
        let cmd = TriggerCommand(action, data)
        task {
            let! _ = this.Transceiver.SendAsync(cmd)
            return ()
        }
    member _.TriggerConsolidateAsync() =
        this.TriggerAsync("consolidate", None);
