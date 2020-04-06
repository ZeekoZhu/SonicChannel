open Fake.MyFakeTools

#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.MyFakeTools
open Fake.Core.TargetOperators
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators

module Project =
    let internal resolveProjectFile name =
        name </> (sprintf "%s.fsproj" name)
    let SonicChannel = "SonicChannel"
    let projects =
        [
            SonicChannel
            "SonicConsole"
        ]
        |> Seq.map resolveProjectFile
    
    let packages =
        [
            SonicChannel
        ]

    let build () =
        let buildProject project =
            DotNet.build id project
        projects
        |> Seq.iter buildProject

    let pack () =
        let setOpt (x: Paket.PaketPackParams) =
            { x with
                OutputPath = "./output"
                LockDependencies = true
            }
        Paket.pack setOpt

    let internal testArgs = sprintf "--project %s --summary"
    
    let test () =
        let projects =
            [ "Tests" ] |> Seq.map resolveProjectFile
        let run proj =
            let result = DotNet.exec id "run" (testArgs proj)
            if result.ExitCode <> 0 then
                failwith "test failed"
        projects
        |> Seq.iter run

Target.create "build" (ignore >> Project.build)
Target.create "test" (ignore >> Project.test)
Target.create "pack" (ignore >> Project.pack)

"build" ==> "pack"
"build" ==> "test"

Target.useTriggerCI ()

Target.create "empty" ignore


// start build
Target.runOrDefaultWithArguments "empty"
