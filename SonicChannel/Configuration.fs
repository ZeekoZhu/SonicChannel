module SonicChannel.Configuration

open System.Text

type ChannelConnection =
    { Host: string
      Port: int
      AuthPassword: string option }

type TransceiverOption =
    { Encoding: Encoding
      BufferSize: int }
    with
        static member Default = { Encoding = Encoding.UTF8; BufferSize = 1024 * 8 }

type IOptionReader =
    abstract member ConnectionOption : unit -> ChannelConnection
    abstract member TransceiverOption : unit -> TransceiverOption
