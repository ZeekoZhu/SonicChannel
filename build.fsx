open Fake.MyFakeTools

#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.MyFakeTools
open Fake.Core.TargetOperators

Target.useTriggerCI ()

Target.create "empty" ignore

// start build
Target.runOrDefaultWithArguments "empty"
