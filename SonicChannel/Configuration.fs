module SonicChannel.Configuration

open System
open System.Text

type ChannelConnection =
    { Host: string
      Port: int
      AuthPassword: string option }

type TransceiverOption =
    { Encoding: Encoding
      Timeout: TimeSpan
      BufferSize: int }
    with
        static member Default =
            { Encoding = UTF8Encoding(false)
              BufferSize = 1024 * 8
              Timeout = TimeSpan.FromMilliseconds(500.0)
            }

type IOptionReader =
    abstract member ConnectionOption : unit -> ChannelConnection
    abstract member TransceiverOption : unit -> TransceiverOption
