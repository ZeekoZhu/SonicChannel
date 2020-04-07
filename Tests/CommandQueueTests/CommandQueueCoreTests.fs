module Tests.CommandQueueTests.CommandQueueCoreTests

open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open SonicChannel
open Tests
open Tests.CommandQueueTests.SonicCommandMocks

let logger = new NullLoggerFactory()

let expectCancelled (t: Task) =
    Expect.equal t.Status TaskStatus.Canceled "Task cancelled"

[<Tests>]
let tests =
    [
        test "set waiting" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb
            Expect.isSome coreQueue.Waiting "set waiting"
        }
        test "set waiting FIFO" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb1) = createCallback ()
            let (_, cb2) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb1
            coreQueue.Dispatch <| SetWaiting cb2
            Expect.isSome coreQueue.Waiting "set waiting"
            Expect.equal coreQueue.Waiting.Value cb1
                "fifo queue"
        }
        test "waiting handled" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb
            coreQueue.Dispatch <| WaitingHandled
            Expect.isNone coreQueue.Waiting "waiting queue should be empty"
        }
        test "waiting handled FIFO" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb1) = createCallback ()
            let (_, cb2) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb1
            coreQueue.Dispatch <| SetWaiting cb2

            coreQueue.Dispatch <| WaitingHandled
            Expect.isSome coreQueue.Waiting "set waiting"
            Expect.equal coreQueue.Waiting.Value cb2
                "pop cb1"
            coreQueue.Dispatch <| WaitingHandled
            Expect.isNone coreQueue.Waiting
                "pop cb2"
        }
        ftest "timeout" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            let (_, cb2) = createCallback ()
            let (_, cb3) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb
            coreQueue.Dispatch <| SetWaiting cb2
            coreQueue.Dispatch <| AddPending cb3

            coreQueue.Dispatch <| TimeoutAndMark
            Expect.isTrue cb.Marked "current waiting is marked"
            Expect.isTrue cb3.Marked "current pending items are marked"
            coreQueue.Dispatch <| TimeoutAndMark
            expectCancelled cb.Callback.Task
            expectCancelled cb3.Callback.Task
            Expect.isEmpty coreQueue.PendingList "pending list should be empty"
            Expect.equal coreQueue.Waiting.Value cb2 "pop cb due to timeout"
            Expect.isTrue cb2.Marked "waiting(cb2) is marked"
            coreQueue.Dispatch <| TimeoutAndMark
            Expect.isNone coreQueue.Waiting "pop cb2 due to timeout"
            expectCancelled cb2.Callback.Task
        }
        test "add pending" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            coreQueue.Dispatch <| AddPending cb
            Expect.sequenceEqual
                coreQueue.PendingList
                [ cb ]
                "add pending"
        }
        test "remove pending" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            coreQueue.Dispatch <| AddPending cb
            coreQueue.Dispatch <| RemovePending cb
            Expect.sequenceEqual
                coreQueue.PendingList
                []
                "remove pending"
        }
        test "cancel all" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            let (_, cb2) = createCallback ()
            let (_, cb3) = createCallback ()
            coreQueue.Dispatch <| SetWaiting cb
            coreQueue.Dispatch <| SetWaiting cb2
            coreQueue.Dispatch <| AddPending cb3

            coreQueue.Dispatch CancelAll

            Expect.isNone coreQueue.Waiting "waiting should be empty"
            Expect.isEmpty coreQueue.PendingList "pending list should be empty"
            expectCancelled cb.Callback.Task
            expectCancelled cb2.Callback.Task
            expectCancelled cb3.Callback.Task
        }
    ]
    |> testList "CommandQueueCore"
    |> labelCmdQueue
