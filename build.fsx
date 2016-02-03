// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#I "packages/FAKE/tools"
#r "NuGet.Core.dll"
#r "FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.Testing

// --------------------------------------------------------------------------------------
// Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package 
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project 
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Gluon"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Type-safe remoting connector between F# and TypeScript."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = """
    Gluon provides a type-safe remoting connector between an F# backend
    and a TypeScript client."""
// List of author names (for NuGet package)
let authors = [ "Tachyus Corp" ]
// Tags for your project (for NuGet package)
let tags = "F# fsharp web typescript webapi"

// File system information 
// (<solutionFile>.sln is built during the building process)
let solutionFile = "Gluon.sln"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted 
let gitHome = "https://github.com/Tachyus"
// The name of the project on GitHub
let gitName = "gluon"
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/Tachyus"

// --------------------------------------------------------------------------------------
// The rest of the file includes standard build steps 
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let (!!) includes = (!! includes).SetBaseDirectory __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")
let isAppVeyorBuild = environVar "APPVEYOR" <> null
let nugetVersion = 
    if isAppVeyorBuild then
        let isTagged = Boolean.Parse(environVar "APPVEYOR_REPO_TAG")
        if isTagged then
            environVar "APPVEYOR_REPO_TAG_NAME"
        else
            sprintf "%s-b%03i" release.NugetVersion (int buildVersion)
    else release.NugetVersion

let fsharpAssemblyInfo proj =
    CreateFSharpAssemblyInfo (sprintf "src/%s/AssemblyInfo.fs" proj)
        [ Attribute.Title proj
          Attribute.Product proj
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion ]

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" <| fun _ ->
    fsharpAssemblyInfo "Gluon"
    fsharpAssemblyInfo "Gluon.CLI"

Target "BuildVersion" <| fun _ ->
    Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" nugetVersion) |> ignore

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "Clean" <| fun _ ->
    CleanDirs ["bin"; "temp"]

Target "CleanDocs" <| fun _ ->
    CleanDirs ["docs/output"]

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" <| fun _ ->
    let projects =
        [
            "src/Gluon/Gluon.fsproj"
            "src/Gluon.CLI/Gluon.CLI.fsproj"
            "tests/Gluon.Tests/Gluon.Tests.fsproj"
        ]
    for projFile in projects do
        build (fun x ->
            { x with
                Properties =
                    [ "Optimize",      environVarOrDefault "Build.Optimize"      "True"
                      "DebugSymbols",  environVarOrDefault "Build.DebugSymbols"  "True"
                      "Configuration", environVarOrDefault "Build.Configuration" "Release" ]
                Targets =
                    [ "Rebuild" ]
                Verbosity = Some Quiet }) projFile

let tryFindApp (app: string) (dirs: string seq) =
    dirs |> Seq.map (fun dir -> dir </> app) |> Seq.tryFind File.Exists

let findApp app dirs =
    match tryFindApp app dirs with
    | Some path -> path
    | None -> failwithf "unable to find app `%s` in %A" app dirs

let runApp configProcessStartInfo timeout =
    ExecProcess (fun p ->
        configProcessStartInfo p
        logfn "%s> \"%s\" %s" p.WorkingDirectory p.FileName p.Arguments
    ) timeout

let getProgramFiles() =
    seq {
        match Environment.Is64BitOperatingSystem, Environment.Is64BitProcess with
        | true, true ->
            yield Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles // C:\Program Files
            yield Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86 // C:\Program Files (x86)
        | true, false ->
            yield Environment.GetEnvironmentVariable "ProgramW6432" // C:\Program Files
            yield Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles // C:\Program Files (x86)
        | false, _ ->
            yield Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles
    }

let findNode() =
    let programFiles = getProgramFiles() |> List.ofSeq
    let subdirs name = programFiles |> Seq.map (fun dir -> Path.Combine(dir, name))
    Seq.append
        (subdirs "iojs")
        (subdirs "nodejs")
    |> (findApp "node.exe")

let nodeRun node task workingDir timeout =
    let config (p : ProcessStartInfo) =
        p.FileName <- node
        p.WorkingDirectory <- p.WorkingDirectory </> workingDir
        p.Arguments <- task
    if runApp config timeout <> 0 then
        failwithf "%s failed" task

let findNpm node =
    (Path.GetDirectoryName node) </> @"node_modules/npm/bin/npm-cli.js"

let npm task workingDir timeout =
    let node = findNode()
    let npm = findNpm node
    let npmRun task = sprintf "\"%s\" %s" npm task
    nodeRun node (npmRun task) workingDir (TimeSpan.FromMinutes 5.)

Target "NpmInstall" <| fun _ ->
    npm "install" "src/Gluon.Client/" (TimeSpan.FromMinutes 5.)

Target "BuildClientJS" <| fun _ ->
    npm "run build" "src/Gluon.Client/" (TimeSpan.FromMinutes 5.)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "RunTests" <| fun _ ->
    !! testAssemblies
    |> xUnit2 (fun p ->
        { p with
            TimeOut = TimeSpan.FromMinutes 20.
            XmlOutputPath = Some "bin/TestResults.xml" })

#if MONO
#else
// --------------------------------------------------------------------------------------
// SourceLink allows Source Indexing on the PDB generated by the compiler, this allows
// the ability to step through the source code of external libraries https://github.com/ctaggart/SourceLink
#load "packages/SourceLink.Fake/tools/SourceLink.fsx"
open SourceLink

Target "SourceLink" <| fun _ ->
    let baseUrl = sprintf "%s/%s/{0}/%%var2%%" gitRaw project
    !! "src/**/*.fsproj"
    |> Seq.iter (fun projFile ->
        let proj = VsProj.LoadRelease projFile 
        SourceLink.Index proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ baseUrl)
#endif

// --------------------------------------------------------------------------------------
// Build a NuGet package

let referenceDependencies dependencies =
    let packagesDir = __SOURCE_DIRECTORY__  </> "packages"
    [ for dependency in dependencies -> dependency, GetPackageVersion packagesDir dependency ]

Target "NuGet" <| fun _ ->
    Paket.Pack <| fun x ->
        { x with
            OutputPath = "bin"
            Version = nugetVersion
            ReleaseNotes = String.concat Environment.NewLine release.Notes }

Target "PublishNuGet" <| fun _ ->
    Paket.Push <| fun p ->
        { p with WorkingDir = "bin" }

// --------------------------------------------------------------------------------------
// Generate the documentation

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateReferenceDocs" <| fun _ ->
    if not <| executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE"] [] then
      failwith "generating reference documentation failed"

let generateHelp' fail debug =
    let args =
        if debug then ["--define:HELP"]
        else ["--define:RELEASE"; "--define:HELP"]
    if executeFSIWithArgs "docs/tools" "generate.fsx" args [] then
        traceImportant "Help generated"
    else
        if fail then
            failwith "generating help documentation failed"
        else
            traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target "GenerateHelp" <| fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    generateHelp true

Target "GenerateHelpDebug" <| fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    generateHelp' true true

Target "KeepRunning" <| fun _ ->
    use watcher = !! "docs/content/**/*.*" |> WatchChanges (fun changes ->
         generateHelp false
    )

    traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.Dispose()

Target "GenerateDocs" DoNothing

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" <| fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Commit tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir

Target "Release" <| fun _ ->
    StageAll ""
    Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  =?> ("BuildVersion", isAppVeyorBuild)
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "NpmInstall"
  ==> "BuildClientJS"
  ==> "RunTests"
  ==> "All"
  =?> ("GenerateReferenceDocs",isLocalBuild && not isMono)
  =?> ("GenerateDocs",isLocalBuild && not isMono)
  =?> ("ReleaseDocs",isLocalBuild && not isMono)

"All" 
#if MONO
#else
  =?> ("SourceLink", Pdbstr.tryFind().IsSome )
#endif
  ==> "NuGet"
  ==> "BuildPackage"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"
    
"ReleaseDocs"
  ==> "Release"

"BuildPackage"
  ==> "PublishNuGet"
  ==> "Release"

RunTargetOrDefault "All"

