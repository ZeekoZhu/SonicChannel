module Tests.CommandTextBuilderTests

open System.Text
open Expecto
open FsCheck
open SonicChannel.SonicCommand

[<Tests>]
let escapeCmdTests =
   [
       test "escape \\" {
           Expect.equal
               ("foo \\ bar" |> CommandTextBuilder.escapeCmdText)
               "foo \\\\ bar"
               "\\ should be escaped"
       }
       test "escape '\\n'" {
           Expect.equal
               ("foo \n bar" |> CommandTextBuilder.escapeCmdText)
               "foo \\n bar"
               "'\\n' should be escaped"
       }
       test "escape '\"'" {
           Expect.equal
               ("foo \" bar" |> CommandTextBuilder.escapeCmdText)
               "foo \\\" bar"
               "'\"' should be escaped"
       }
   ]
   |> testList "escapeCmdTest"
   |> unitTest

let bSize = 50
let encoding = Encoding.UTF8
let splitText =
    CommandTextBuilder.splitTextChunks
        (bSize * encoding.GetMaxByteCount(1)) (Encoding.UTF8)
    >> List.ofSeq

let checkChunk (pred) (text: string NonNull) =
    let text = text.Get
    splitText text
    |> Seq.forall (fun chunk -> pred chunk)

[<Tests>]
let splitTextChunksProps =
    [
        testProperty
            "text length small than bSize/2 should not split"
            <| fun (text: string NonNull) ->
                let text = text.Get
                (text.Length < (bSize / 2))
                    ==>
                    lazy ((splitText text).Length = 1)
        testProperty
            "single chunk should be text itself"
            <| fun (text: string NonNull) ->
                let text = text.Get
                let result = splitText text
                (result.Length = 1)
                    ==> (result.[0] = text)
        testProperty
            "every chunk can not be split"
            <| checkChunk ( fun chunk ->
                let result = splitText chunk
                result.Length = 1
            )
        testProperty
            "every chunk's size under limit"
            <| checkChunk ( fun chunk ->
                encoding.GetBytes(chunk).Length <= bSize * (encoding.GetMaxByteCount(1))
            )
        test "split null" {
            let splitNull () =
                splitText null
                |> ignore
            Expect.throws
                splitNull
                "should throw error"
        }
    ]
    |> testList "splitTextChunks"
    |> unitTest
