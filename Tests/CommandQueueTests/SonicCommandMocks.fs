module Tests.CommandQueueTests.SonicCommandMocks
open System
open System.Threading.Tasks
open Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter
open Moq
open SonicChannel
open SonicChannel.SonicCommand

let toCmdString =
    <@ Func<_, _>(fun (x: ISonicCommand) -> x.ToCommandString ()) @>
    |> QuotationToLambdaExpression

let handlePending =
    <@ Func<_, _>(fun (x: ISonicCommand) -> x.HandlePendingMsg (It.IsAny<string>())) @>
    |> QuotationToLambdaExpression

let handleWaiting =
    <@ Func<_, _>(fun (x: ISonicCommand) -> x.HandleWaitingMsg (It.IsAny<string>())) @>
    |> QuotationToLambdaExpression

let createCmdMock () =
    Mock<ISonicCommand>()

let createCallback () =
    let cmdMock = createCmdMock ()
    cmdMock, { Command = cmdMock.Object; Callback = TaskCompletionSource(); Marked = false }
