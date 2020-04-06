module Tests.CommandQueueTests.SessionMessageProcessorTests

open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open SonicChannel
open SonicChannel.SonicCommand
open Tests
open Tests.CommandQueueTests.SonicCommandMocks

let logger = NullLogger<SessionMessageProcessor>()
let createCallback cmd =
    { Command = cmd; Callback = TaskCompletionSource(); Marked = false }
let msgProcessor = SessionMessageProcessor(logger)

[<Tests>]
let tests =
    [
        test "waiting finished" {
            let cmd = createCmdMock()
            cmd.Setup(handleWaiting).Returns(Handled Finished) |> ignore
            let cb = createCallback cmd.Object
            let waiting = Some cb
            let pending = [] |> Seq.ofList
            let result =
                msgProcessor.ProcessMessage (waiting, pending) "msg"
            let expected =
                seq {
                    WaitingHandled
                    Complete cb
                }
            Expect.sequenceEqual result.Value expected "waiting handled"
        }
        test "waiting -> pending" {
            let cmd = createCmdMock()
            cmd.Setup(handleWaiting).Returns(Handled Pending) |> ignore
            let cb = createCallback cmd.Object
            let waiting = Some cb
            let pending = [] |> Seq.ofList
            let result =
                msgProcessor.ProcessMessage (waiting, pending) "msg"
            let expected =
                seq {
                    WaitingHandled
                    AddPending cb
                }
            Expect.sequenceEqual result.Value expected "waiting -> pending"
        }
        test "waiting not handled" {
            let cmd = createCmdMock()
            cmd.Setup(handleWaiting).Returns(Ignore) |> ignore
            let cb = createCallback cmd.Object
            let waiting = Some cb
            let pending = [] |> Seq.ofList
            let result =
                msgProcessor.ProcessMessage (waiting, pending) "msg"
            let expected = Seq.empty
            Expect.sequenceEqual result.Value expected "nothing"
        }
        test "no waiting" {
            let waiting = None
            let pending = [] |> Seq.ofList
            let result =
                msgProcessor.ProcessMessage (waiting, pending) "msg"
            let expected = Seq.empty
            Expect.sequenceEqual result.Value expected "nothing"
        }
        test "pending handled" {
            let waiting = None
            let cmd = createCmdMock()
            cmd.Setup(handlePending).Returns(Handled Finished) |> ignore
            let cb = createCallback cmd.Object
            let pending =
                seq {
                    cb
                    createCallback cmd.Object
                }
            let result =
                msgProcessor.ProcessMessage (waiting, pending) "msg"
            let expected =
                seq {
                    Complete cb
                    RemovePending cb
                }
            Expect.sequenceEqual result.Value expected "remove pending"
        }
    ]
    |> ftestList "MsgProcessor"
    |> labelCmdQueue
