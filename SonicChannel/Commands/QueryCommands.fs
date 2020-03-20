module SonicChannel.Commands.QueryCommands
open FsToolkit.ErrorHandling
open System
open System.Text.RegularExpressions
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

[<AbstractClass>]
type QueryModeCommand() =
    member val Marker: string option = None with get, set
    member val Result: string array option = None with get, set
    abstract member QueryType : string
    abstract ToCommandString : unit -> string
    interface ISonicCommand with
        member this.ToCommandString () = this.ToCommandString()
        member this.HandleWaitingMsg msg =
            let regex = Regex("^PENDING (?<marker>\w+)$", regexOpt)
            let marker =
                tryMatch regex msg
                |> Option.bind (tryGetGroup "marker")
            match marker with
            | Some marker ->
                this.Marker <- Some marker
                Pending |> Handled
            | None -> Bypass
        member this.HandlePendingMsg msg =
            let marker = this.Marker |> Option.defaultValue ""
            let regex =
                Regex(sprintf "^EVENT %s %s (?<result>.*)$" this.QueryType marker, RegexOptions.IgnoreCase)
            let result =
                tryMatch regex msg
                |> Option.bind (tryGetGroup "result")
            match result with
            | Some r ->
                this.Result <-
                    r.Split(" ", StringSplitOptions.RemoveEmptyEntries)
                    |> Array.filter (String.IsNullOrWhiteSpace >> not)
                    |> Some
                Finished |> Handled
            | None -> Bypass

type QueryCommand
    (
         collection: string,
         bucket: string,
         terms: string,
         limit: int option,
         offset: int option,
         lang: string option
    ) =
    inherit QueryModeCommand() with

        override _.QueryType = "QUERY"
        override _.ToCommandString () =
            CommandTextBuilder.queryCmd
                collection
                bucket
                terms
                limit
                offset
                lang

type SuggestCommand
    (
         collection: string,
         bucket: string,
         word: string,
         limit: int option
    ) =
    inherit QueryModeCommand() with

        override _.QueryType = "SUGGEST"
        override _.ToCommandString () =
            CommandTextBuilder.suggest
                collection
                bucket
                word
                limit
