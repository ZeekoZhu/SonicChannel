module Tests.CommandQueueTests.CommandQueueCoreTests

open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open SonicChannel
open Tests
open Tests.CommandQueueTests.SonicCommandMocks

let logger = new NullLoggerFactory()

let expectFaulted (t: Task) =
    Expect.equal t.Status TaskStatus.Faulted "Task timeout"

[<Tests>]
let tests =
    [
        testAsync "set waiting" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb
            Expect.isSome coreQueue.Waiting "set waiting"
        }
        testAsync "set waiting FIFO" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb1) = createCallback ()
            let (_, cb2) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb1
            do! coreQueue.DispatchAsync <| SetWaiting cb2
            Expect.isSome coreQueue.Waiting "set waiting"
            Expect.equal coreQueue.Waiting.Value cb1
                "fifo queue"
        }
        testAsync "waiting handled" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb
            do! coreQueue.DispatchAsync <| WaitingHandled
            Expect.isNone coreQueue.Waiting "waiting queue should be empty"
        }
        testAsync "waiting handled FIFO" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb1) = createCallback ()
            let (_, cb2) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb1
            do! coreQueue.DispatchAsync <| SetWaiting cb2

            do! coreQueue.DispatchAsync <| WaitingHandled
            Expect.isSome coreQueue.Waiting "set waiting"
            Expect.equal coreQueue.Waiting.Value cb2
                "pop cb1"
            do! coreQueue.DispatchAsync <| WaitingHandled
            Expect.isNone coreQueue.Waiting
                "pop cb2"
        }
        testAsync "timeout" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            let (_, cb2) = createCallback ()
            let (_, cb3) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb
            do! coreQueue.DispatchAsync <| SetWaiting cb2
            do! coreQueue.DispatchAsync <| AddPending cb3

            do! coreQueue.DispatchAsync <| TimeoutAndMark
            Expect.isTrue cb.Marked "current waiting is marked"
            Expect.isTrue cb3.Marked "current pending items are marked"
            do! coreQueue.DispatchAsync <| TimeoutAndMark
            expectFaulted cb.Callback.Task
            expectFaulted cb3.Callback.Task
            Expect.isEmpty coreQueue.PendingList "pending list should be empty"
            Expect.equal coreQueue.Waiting.Value cb2 "pop cb due to timeout"
            Expect.isTrue cb2.Marked "waiting(cb2) is marked"
            do! coreQueue.DispatchAsync <| TimeoutAndMark
            Expect.isNone coreQueue.Waiting "pop cb2 due to timeout"
            expectFaulted cb2.Callback.Task
        }
        testAsync "add pending" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            do! coreQueue.DispatchAsync <| AddPending cb
            Expect.sequenceEqual
                coreQueue.PendingList
                [ cb ]
                "add pending"
        }
        testAsync "remove pending" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            do! coreQueue.DispatchAsync <| AddPending cb
            do! coreQueue.DispatchAsync <| RemovePending cb
            Expect.sequenceEqual
                coreQueue.PendingList
                []
                "remove pending"
        }
        testAsync "cancel all" {
            use ct = new CancellationTokenSource()
            let coreQueue = CommandQueueCore(ct.Token, logger)
            let (_, cb) = createCallback ()
            let (_, cb2) = createCallback ()
            let (_, cb3) = createCallback ()
            do! coreQueue.DispatchAsync <| SetWaiting cb
            do! coreQueue.DispatchAsync <| SetWaiting cb2
            do! coreQueue.DispatchAsync <| AddPending cb3

            do! coreQueue.DispatchAsync CancelAll

            Expect.isNone coreQueue.Waiting "waiting should be empty"
            Expect.isEmpty coreQueue.PendingList "pending list should be empty"
            expectFaulted cb.Callback.Task
            expectFaulted cb2.Callback.Task
            expectFaulted cb3.Callback.Task
        }
    ]
    |> testList "CommandQueueCore"
    |> labelCmdQueue
