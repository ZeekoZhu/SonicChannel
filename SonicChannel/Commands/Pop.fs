namespace SonicChannel.Commands
open FsToolkit.ErrorHandling
open System.Text.RegularExpressions
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

type PopCommand(collection: string, bucket: string, object: string, text: string) =
    let pop =
        sprintf "POP %s %s %s \"%s\""
            (escapeCmdText collection)
            (escapeCmdText bucket)
            (escapeCmdText object)
            (escapeCmdText text)

    let resultRegex = Regex "RESULT (?<result>\d+)"

    let mutable result: int option = None
    interface SonicCommand<int> with
        member _.ToString() = pop
        member _.Result
            with get () = result
            and set(value) = result <- value
        member _.MatchResult (msg) =
            tryMatch resultRegex msg
            |> Option.bind (tryGetGroup "result")
            |> Option.bind (Option.tryParse<int>)
        member x.HandleWaitingMsg msg =
            CommandHelper.handleMsg x msg
        member _.HandlePendingMsg _ = failwith "Invalid state pending"

