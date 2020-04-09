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


    module Tests =
        let private testArgs proj = 
            [ 
                "--project"; proj
                "--summary"
                "--no-spinner"
                "--fail-on-focused-tests"
            ]
        let private filterTests filter proj =
            testArgs proj @ [
                "--filter"; filter
            ]
        let private unitTestArgs =
            filterTests "Unit"
        let private smokeTestArgs =
            filterTests "Smoke"
        let private toCmd fn p =
            fn p
            |> String.concat " "
        let private runTestProjects runCmd =
            let projects =
                [ "Tests" ] |> Seq.map resolveProjectFile
            let run proj =
                let result = DotNet.exec id "run" (runCmd proj)
                if result.ExitCode <> 0 then
                    failwith "test failed"
            projects
            |> Seq.iter run

        let unitTest () =
            runTestProjects (toCmd unitTestArgs)
        let smokeTest () =
            runTestProjects (toCmd smokeTestArgs)


Target.create "build" (ignore >> Project.build)
Target.create "test:unit" (ignore >> Project.Tests.unitTest)
Target.create "test:smoke" (ignore >> Project.Tests.smokeTest)
Target.create "test" ignore
Target.create "pack" (ignore >> Project.pack)

"build" ==> "pack"
"build" ==> "test:unit" ==> "test"
"build" ==> "test:smoke" ==> "test"

Target.useTriggerCI ()

Target.create "empty" ignore


// start build
Target.runOrDefaultWithArguments "empty"
