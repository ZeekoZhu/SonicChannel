module Tests.CommandQueueTests.CtorTests
open System.Threading.Tasks
open Expecto
open SonicChannel
open Tests

[<Tests>]
let tests =
    testList "ctor" [
        test "can construct" {
            let sendMsgSpy msg = Task.FromResult ()
            Expect.throws
                (fun () -> new CommandQueue(sendMsgSpy, null) |> ignore)
                "it should check null logger"
        }
    ]
    |> labelCmdQueue
