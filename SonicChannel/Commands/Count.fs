namespace SonicChannel.Commands
open FsToolkit.ErrorHandling
open System.Text.RegularExpressions
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

[<AbstractClass>]
type IntResultCommand() =
    let resultRegex = Regex "RESULT (?<result>\d+)"

    let mutable result = None
    abstract member Result: int option with get, set
    default _.Result
        with get() = result
        and set (value) = result <- value
    inherit SonicCommand<int>() with
        member x.ToString() = x.ToString()
        member x.Result
            with get() = x.Result
            and set(value) = x.Result <- value
        member _.MatchResult (msg) =
            tryMatch resultRegex msg
            |> Option.bind (tryGetGroup "result")
            |> Option.bind (Option.tryParse<int>)
        member x.HandleWaitingMsg msg =
            CommandHelper.handleMsg x msg
        member _.HandlePendingMsg _ = failwith "Invalid state pending"

type CountCommand(collection: string, bucket: string option, object: string option) =
    inherit IntResultCommand() with
        override _.ToString() =
            sprintf "COUNT %s %s %s"
                (escapeCmdText collection)
                (defaultEmpty bucket)
                (defaultEmpty object)

