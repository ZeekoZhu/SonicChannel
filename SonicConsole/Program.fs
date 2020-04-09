// Learn more about F# at http://fsharp.org

open Microsoft.Extensions.Logging
open SonicChannel
open SonicChannel.Configuration
open System.Net.Sockets
open FSharp.Control.Tasks.V2.ContextInsensitive

type FooOptionReader() =
    interface IOptionReader with
        member _.ConnectionOption() =
            { Host = "localhost"; Port = 1491; AuthPassword = Some "p@55w0rd" }
        member _.TransceiverOption () = TransceiverOption.Default
[<EntryPoint>]
let main argv =
    let loggerFactory =
        LoggerFactory.Create
            (fun builder ->
                    builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore
            )
    use channel = new IngestChannel(FooOptionReader(), loggerFactory)
    task {
        do! channel.StartAsync()
        do! channel.PushAsync("c1", "b1", "o1", "hello world", None)
        do! channel.PushAsync("c1", "b1", "o2", "who is your daddy", None)
        do! channel.PushAsync("c1", "b1", "o3", "greed is good", None)
        do! channel.PushAsync("c1", "b1", "o4", "there is no spoon", None)
        do! channel.QuitAsync()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
    use client1 = new TcpClient()
    use channel = new SearchChannel(FooOptionReader(), loggerFactory)
    task {
        do! channel.StartAsync()
        let! result = channel.QueryAsync("c1", "b1", "hello world", None, None, None)
        printfn "%A" result
        let! result = channel.QueryAsync("c1", "b1", "good world", None, None, None)
        printfn "%A" result
        let! result = channel.QueryAsync("c1", "b1", "daddy", None, None, None)
        printfn "%A" result
        let! result = channel.QueryAsync("c1", "b1", "spoon", None, None, None)
        printfn "%A" result
        do! channel.QuitAsync()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
    0 // return an integer exit code
