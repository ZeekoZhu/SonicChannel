// Learn more about F# at http://fsharp.org

open Microsoft.Extensions.Logging
open SonicChannel
open SonicChannel.Configuration
open System.Net.Sockets

type FooOptionReader() =
    interface IOptionReader with
        member _.ConnectionOption() =
            { Host = "localhost"; Port = 1491; AuthPassword = Some "p@55w0rd" }
        member _.TransceiverOption () = TransceiverOption.Default
[<EntryPoint>]
let main argv =
    use client = new TcpClient()
    let loggerFactory =
        LoggerFactory.Create
            (fun builder ->
                    builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore
            )
    use channel = new SearchChannel(client, FooOptionReader(), loggerFactory)
    channel.StartAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously
    0 // return an integer exit code
