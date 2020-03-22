namespace SonicChannel.Commands

open System
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder
open SonicChannel.Commands.AbstractCommands

module internal Patterns =
    let startedRegex =
        Regex ("^STARTED \w+ protocol\((?<protocol>\d+)\) buffer\((?<buffer>\d+)\)$",
               regexOpt)

type StartCommand(mode: ChannelMode, password: string option) =
    inherit SonicCommand<ChannelConfig>() with
        override _.MatchResult (started: string) =
            let createConfig x =
                { Mode = mode
                  BufferSize = x }
            tryMatch Patterns.startedRegex started
            |> Option.bind (tryGetGroup "buffer")
            |> Option.bind (Option.tryParse<int>)
            |> Option.map createConfig
        override _.ToCommandString () =
            let password =
                password
                |> Option.map CommandTextBuilder.escapeCmdText
                |> Option.defaultValue String.Empty
            sprintf "START %s %s" (mode.ToString()) password
        override _.HandlePendingMsg _ = failwith "Invalid state pending"

type QuitCommand() =
    inherit ConstantResultCommand() with
        override _.Response = "ENDED quit"
        override _.ToCommandString () =
            sprintf "QUIT"
