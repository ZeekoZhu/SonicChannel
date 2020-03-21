module SonicChannel.Commands.ControlCommand
open SonicChannel.SonicCommand.CommandTextBuilder
open SonicChannel.Commands.AbstractCommands

type TriggerCommand(action: string, data: string option) =
    inherit ConstantResultCommand() with
        override _.Response = "OK"
        override _.ToCommandString () =
            sprintf "TRIGGER %s %s"
                (escapeCmdText action)
                (defaultEmpty data)
