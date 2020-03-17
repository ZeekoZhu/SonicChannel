// Learn more about F# at http://fsharp.org

open SonicChannel
open SonicChannel.Configuration
open System.Net.Sockets

type FooOptionReader() =
    interface IOptionReader with
        member _.ConnectionOption() =
            { Host = "localhost"; Port = 7788; AuthPassword = Some "p@55w0rd" }
        member _.TransceiverOption () = TransceiverOption.Default
[<EntryPoint>]
let main argv =
    use client = new TcpClient()
    use channel = new SearchChannel(client, FooOptionReader())
    channel.StartAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously
    0 // return an integer exit code
