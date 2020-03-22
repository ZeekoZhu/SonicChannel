module SonicChannel.SonicCommand

open System
open System.Text
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

    // todo: simplify
    let queryCmd collection bucket terms limitOpt offsetOpt langOpt =
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
    let splitTextChunks (bufferSize: int) (encoding: Encoding) (text: string) =
        // reserve 50% space for other parts of command
        let chunkSize =
            Math.Floor((float bufferSize) * 0.5) / (float (encoding.GetMaxByteCount(1)))
            |> int
        let rec split (remain: ReadOnlyMemory<char>) =
            seq {
                if remain.Length > chunkSize
                then
                    yield remain.Slice(0, chunkSize).ToString()
                    yield! split (remain.Slice(chunkSize))
                else
                    yield remain.ToString()
            }
        split (text.AsMemory())


    let defaultEmpty = Option.map escapeCmdText >> Option.defaultValue ""
    let regexOpt = RegexOptions.IgnoreCase ||| RegexOptions.Compiled


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
    | Pending
    | Finished
type MessageHandleResult =
    | Handled of SonicCommandState
    | Bypass
type ISonicCommand =
    abstract ToCommandString: unit -> string
    abstract HandleWaitingMsg: string -> MessageHandleResult
    abstract HandlePendingMsg: string -> MessageHandleResult

[<AbstractClass>]
type SonicCommand<'r>() =
    abstract ToCommandString: unit -> string
    abstract HandlePendingMsg: string -> MessageHandleResult
    abstract MatchResult : string -> 'r option
    member val Result: 'r option = None with get, set
    override this.ToString () =
        this.ToCommandString()
    interface ISonicCommand with
        member this.HandleWaitingMsg str =
            match this.MatchResult str with
            | Some result ->
                this.Result <- Some result
                Finished |> Handled
            | None -> Bypass

        member this.HandlePendingMsg str = this.HandlePendingMsg str
        member this.ToCommandString () = this.ToCommandString ()

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


