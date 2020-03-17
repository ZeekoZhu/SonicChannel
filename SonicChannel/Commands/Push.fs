namespace SonicChannel.Commands
open SonicChannel.SonicCommand
open SonicChannel.SonicCommand.CommandTextBuilder

type PushCommand(collection: string, bucket: string, object: string, text: string, lang: string option) =
    let push =
        sprintf """PUSH %s %s %s "%s" %s"""
            (escapeCmdText collection)
            (escapeCmdText bucket)
            (escapeCmdText object)
            (escapeCmdText text)
            (lang |> Option.map escapeCmdText |> CommandTextBuilder.lang)


    interface ISonicCommand with
        member __.ToString() = push
        member _.HandleWaitingMsg msg =
            match msg with
            | "OK" ->
                SonicCommandState.Finished |> Handled
            | _ -> Bypass
        member _.HandlePendingMsg _ = failwith "Invalid state pending"

