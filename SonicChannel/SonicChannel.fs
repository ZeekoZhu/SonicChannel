namespace SonicChannel

open System
open System.Net.Sockets
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open SonicChannel.Configuration
open SonicChannel.SonicCommand
open SonicChannel.Commands
open FSharp.Control.Tasks.V2.ContextInsensitive

[<AbstractClass>]
type SonicChannel
    (
        client: TcpClient,
        optionReader: IOptionReader,
        loggerFactory: ILoggerFactory
    ) =
    let connOpt = optionReader.ConnectionOption ()
    let mutable disposed = false
    let mutable started = false;
    let transceiver = new MessageTransceiver(client, optionReader, loggerFactory)
    let cleanup disposing =
        if not disposed then
            disposed <- true
            if disposing then
                (transceiver :> IDisposable).Dispose()
    member val Config: ChannelConfig option = None with get, set
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
    interface IDisposable with
        member this.Dispose () =
            cleanup true
            GC.SuppressFinalize this

type IngestChannel(client, optionReader, loggerFactory) =
    inherit SonicChannel(client, optionReader, loggerFactory) with
        override _.StartAsync() =
            base.StartAsync(ChannelMode.Ingest)
type SearchChannel(client, optionReader, loggerFactory) =
    inherit SonicChannel(client, optionReader, loggerFactory) with
        override _.StartAsync() =
            base.StartAsync(ChannelMode.Search)
type ControlChannel(client, optionReader, loggerFactory) =
    inherit SonicChannel(client, optionReader, loggerFactory) with
        override _.StartAsync() =
            base.StartAsync(ChannelMode.Control)
