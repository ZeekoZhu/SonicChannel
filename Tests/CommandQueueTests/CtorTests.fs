module Tests.CommandQueueTests.CtorTests
open System.Threading.Tasks
open Expecto
open Moq.AutoMock
open SonicChannel
open SonicChannel.SonicCommand
open Tests

let nullSendMsg msg = Task.FromResult ()
[<Tests>]
let ctorTests =
    testList "ctor" [
        test "can construct" {
            Expect.throws
                (fun () -> new CommandQueue(nullSendMsg, null) |> ignore)
                "it should check null logger"
        }
    ]
    |> labelCmdQueue

let mocker = AutoMocker()

let createMatchCmd () =
    let cmdMock = mocker.GetMock<ISonicCommand>()
    cmdMock.Setup(SonicCommandMocks.toCmdString).Returns("foo")
    |> ignore
    cmdMock.Setup(SonicCommandMocks.handlePending).Returns(Handled Finished)

