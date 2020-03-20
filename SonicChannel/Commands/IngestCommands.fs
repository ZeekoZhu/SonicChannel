module SonicChannel.Commands.IngestCommands
open FsToolkit.ErrorHandling
open SonicChannel.Commands.AbstractCommands
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder


type PushCommand(collection: string, bucket: string, object: string, text: string, lang: string option) =
    inherit OkResultCommand() with
        override _.ToCommandString() =
            sprintf """PUSH %s %s %s "%s" %s"""
                (escapeCmdText collection)
                (escapeCmdText bucket)
                (escapeCmdText object)
                (escapeCmdText text)
                (lang |> Option.map escapeCmdText |> CommandTextBuilder.lang)

type PopCommand(collection: string, bucket: string, object: string, text: string) =
    inherit IntResultCommand() with
        override _.ToCommandString () =
            sprintf "POP %s %s %s \"%s\""
                (escapeCmdText collection)
                (escapeCmdText bucket)
                (escapeCmdText object)
                (escapeCmdText text)

type CountCommand(collection: string, bucket: string option, object: string option) =
    inherit IntResultCommand() with
        override _.ToCommandString () =
            sprintf "COUNT %s %s %s"
                (escapeCmdText collection)
                (defaultEmpty bucket)
                (defaultEmpty object)

type FlashCommand(collection: string, bucketAndObject: (string * (string option)) option) =
    inherit IntResultCommand() with
        override _.ToCommandString () =
            let bucket = bucketAndObject |> Option.map fst
            let object = bucketAndObject |> Option.bind snd
            sprintf "FLASHB %s %s %s"
                (escapeCmdText collection)
                (defaultEmpty bucket)
                (defaultEmpty object)
