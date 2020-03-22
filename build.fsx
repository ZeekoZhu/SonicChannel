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
        
Target.create "build" (ignore >> Project.build)
Target.create "pack" (ignore >> Project.pack)
"build" ==> "pack"
Target.useTriggerCI ()

Target.create "empty" ignore


// start build
Target.runOrDefaultWithArguments "empty"
