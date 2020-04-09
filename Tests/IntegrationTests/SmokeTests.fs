module Tests.IntegrationTests.SmokeTests

open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Logging
open FSharpx
open FSharpx.Collections
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open SonicChannel
open SonicChannel.Configuration
open FSharp.Control.Tasks.V2.ContextInsensitive
open Tests.IntegrationTests

let testLogger = Log.create "MyTests"
let worker, io = ThreadPool.GetMaxThreads()
testLogger.info (Message.eventX "Threads: {worker} {io}" >> Message.setField "worker" worker >> Message.setField "io" io)

type FooOptionReader() =
    interface IOptionReader with
        member _.ConnectionOption() =
            { Host = "localhost"; Port = 1491; AuthPassword = Some "p@55w0rd" }
        member _.TransceiverOption () = TransceiverOption.Default

let integrationTest (factory: _ -> 'T when 'T :> SonicChannel) (test: 'T -> unit Task) =
    task {
        use loggerFactory =
            new NullLoggerFactory()
//            LoggerFactory.Create
//                (fun builder ->
//                        builder.AddConsole(fun cfg -> cfg.TimestampFormat <- "hh:mm:ss.fff ").SetMinimumLevel(LogLevel.Debug) |> ignore
//                )
        use channel = factory (FooOptionReader(), loggerFactory)
        do! channel.StartAsync()
        do! test channel
        do! channel.QuitAsync()
    } |> Async.AwaitTask
let ingestTest =
    integrationTest (fun x -> new IngestChannel(x))

let ingestTests =
    [
        testAsync "start ingest" {
            do! ingestTest <| ( fun channel ->
                Expect.isSome channel.Config "channel retrieved from server"
                Task.FromResult()
            )
        }
        testAsync "push" {
            do! ingestTest <| ( fun channel ->
                let push (key, item) =
                    channel.PushAsync("thousand_character", "test", key, item, Some "cmn")
                Documents.samples
                |> List.map push
                |> Task.WhenAllUnits
            )
        }
    ]
    |> testList "ingest"
    |> testSequenced

let searchTest =
    integrationTest (fun x -> new SearchChannel(x))
let searchTests =
    [
        testAsync "start search" {
            do! searchTest <| ( fun channel ->
                Expect.isSome channel.Config "channel retrieved from server"
                Task.FromResult()
            )
        }
        let searchWith (terms, expected) =
            testAsync (sprintf "query %s" terms) {
                do! searchTest <| ( fun channel ->
                    task {
                        let! result = channel.QueryAsync("thousand_character", "test", terms, Some 10, Some 0, Some "cmn")
                        let result = Set.ofArray result
                        let expected = Set.ofList expected
                        Expect.equal result expected "search result set"
                    }
                )
            }
        let searchCases =
            [ "丽水", ["section:1"]
              "玄黄", ["section:1"]
              "连枝", ["section:2"]
            ]
        yield! searchCases |> List.map (searchWith)
    ]
    |> testList "search"
    |> testSequenced

[<Tests>]
let tests =
    [
        ingestTests
        searchTests
    ]
    |> testList "SmokeTests"
    |> testSequenced

