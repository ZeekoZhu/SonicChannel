module SonicChannel.SonicCommand

open System.Text.RegularExpressions

module CommandTextBuilder =
    let protocolFunc<'a> name (arg: 'a option) =
        match arg with
        | Some x -> sprintf "%s(%O)" name x
        | None -> ""

    let offset = protocolFunc<int> "OFFSET"
    let limit = protocolFunc<int> "LIMIT"
    let lang = protocolFunc<string> "LANG"
    let matched (g: Group) = g.Success
    let tryMatch (regex: Regex) (str: string) =
        regex.Match str
        |> Some
        |> Option.filter matched
    let tryGetGroup (group: string) (m: Match) =
        m.Groups.[group]
        |> Some
        |> Option.filter matched
        |> Option.map (fun x -> x.Value)


    let query collection bucket terms limitOpt offsetOpt langOpt =
        sprintf """QUERY %s %s "%s" %s %s %s""" collection bucket terms
            (limit limitOpt) (offset offsetOpt) (lang langOpt)

    let suggest collection bucket word limitOpt =
        sprintf """SUGGEST %s %s "%s" %s""" collection bucket word
            (limit limitOpt)

    let ping = "PING"
    let quit = "QUIT"
    let internal escapePatterns =
        [ Regex("""\\""", RegexOptions.Multiline), """\\"""
          Regex("\n", RegexOptions.Multiline), """\n"""
          Regex("\"", RegexOptions.Multiline), "\\\""
        ]
    let escapeCmdText (text: string) =
        let escape text (pattern: Regex, replacement: string) =
            pattern.Replace(text, replacement)
        escapePatterns
        |> List.fold escape text
    let defaultEmpty = Option.map escapeCmdText >> Option.defaultValue ""


type ChannelMode =
    | Search
    | Ingest
    | Control
    override x.ToString() =
        match x with
        | Search -> "search"
        | Ingest -> "ingest"
        | Control -> "control"

type SonicCommandState =
    | Waiting
    | Pending
    | Finished
type MessageHandleResult =
    | Handled of SonicCommandState
    | Bypass
type ISonicCommand =
    abstract ToString: unit -> string
    abstract HandleWaitingMsg: string -> MessageHandleResult
    abstract HandlePendingMsg: string -> MessageHandleResult

[<AbstractClass>]
type internal SonicCommand<'r>() =
    interface ISonicCommand with
        abstract Result : 'r option with get, set
        abstract MatchResult : string -> 'r option

type ChannelConfig =
    { Mode: ChannelMode
      BufferSize: int }

module internal CommandHelper =
    let handleMsg (this: SonicCommand<'r>) msg =
        match this.MatchResult msg with
        | Some result ->
            this.Result <- Some result
            Finished |> Handled
        | None -> Bypass


