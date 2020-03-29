open Expecto

[<EntryPoint>]
let main argv =
    Impl.testFromThisAssembly()
    |> Option.defaultValue (TestList ([], Normal))
    |> runTestsWithCLIArgs [] argv
