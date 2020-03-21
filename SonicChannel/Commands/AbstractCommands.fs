module SonicChannel.Commands.AbstractCommands
open System
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

[<AbstractClass>]
type ConstantResultCommand() =
    abstract ToCommandString : unit -> string
    abstract Response : string
    interface ISonicCommand with
        member this.ToCommandString () = this.ToCommandString()
        member _.HandleWaitingMsg msg =
            if String.Equals(msg, StringComparison.OrdinalIgnoreCase) then
                SonicCommandState.Finished |> Handled
            else Bypass
        member _.HandlePendingMsg _ = failwith "Invalid state pending"

[<AbstractClass>]
type IntResultCommand() =
    inherit SonicCommand<int>() with
        override _.MatchResult (msg) =
            let resultRegex = Regex ("RESULT (?<result>\d+)", regexOpt)
            tryMatch resultRegex msg
            |> Option.bind (tryGetGroup "result")
            |> Option.bind (Option.tryParse<int>)
        override _.HandlePendingMsg _ = failwith "Invalid state pending"
