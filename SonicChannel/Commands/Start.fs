namespace SonicChannel.Commands

open System
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

type StartCommand(mode: ChannelMode, password: string option) =

    let start =
        let password =
            password
            |> Option.map CommandTextBuilder.escapeCmdText
            |> Option.defaultValue String.Empty
        sprintf "START %s %s" (mode.ToString()) password

    let startedRegex =
        Regex ("^STARTED \w+ protocol\((?<protocol>\d+)\) buffer\((?<buffer>\d+)\)$")

    let mutable result: ChannelConfig option = None
    interface SonicCommand<ChannelConfig> with
        member _.Result
            with get () = result
            and set(value) = result <- value
        member _.MatchResult (started: string) =
            let createConfig x =
                { Mode = mode
                  BufferSize = x }
            tryMatch startedRegex started
            |> Option.bind (tryGetGroup "buffer")
            |> Option.bind (Option.tryParse<int>)
            |> Option.map createConfig
        member _.ToString() = start
        member x.HandleWaitingMsg msg =
            CommandHelper.handleMsg x msg
        member _.HandlePendingMsg _ = failwith "Invalid state pending"

