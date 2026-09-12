# Local compiler-aware VB → C# migration

Independent .NET 10 CLI; no dependency on assessment or application projects.
Run commands **from this directory**, which pins SDK 10.0.401. Windows and the
.NET Framework 4.7.2 runtime are needed for the executable WPF canaries.

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --project Tests\TaskOTime.Migration.Tests.csproj

dotnet bin\Debug\net10.0\TaskOTime.Migration.dll convert-language `
  --input ..\..\src\TaskOTime --output artifacts\converted `
  --project TaskOTime.TimeTrackingServices\TaskOTime.TimeTrackingServices.vbproj `
  --project TaskOTime.ViewModel\TaskOTime.ViewModel.vbproj --dry-run

# Repeat without --dry-run, with --restore, to execute in a fresh output directory.
```

`--input` is the directory containing the **whole dependency graph**, not just
the selected VB project. Repeat `--project` to select production projects while
retaining independent VB tests. Omit it to select every VB project.
`--from vb --to cs` are accepted explicitly. `--configuration Debug|Release`
selects one evaluated configuration. Build both configurations separately when
accepting an application with conditional compilation.

Only **trusted MSBuild inputs** should be processed: builds execute project
targets, as ordinary `dotnet build` does. This is not an MSBuild sandbox.
The wrapper never writes to the input directory. It copies inputs (excluding
Git, IDE state, artifacts, bin and obj), builds the copied graph, captures actual
compiler arguments, converts, and rebuilds the C# projects. Input/output trees
must be disjoint; existing outputs, linked files and paths outside the copied
graph are rejected. `--restore` permits dependency restore during input builds.
The output rebuild restores its freshly regenerated intermediate assets.

## Engine and project context

The engine is **ICSharpCode.CodeConverter 10.0.1.923**, pinned with a NuGet lock
file. This is a local library, not the hosted service or an installed global
converter. See `THIRD-PARTY-NOTICES.txt` for the MIT notice.

Official APIs were inspected before integration:

- [Official README/library integration](https://github.com/icsharpcode/CodeConverter/tree/v10.0.1)
- [ProjectConversion.ConvertDocumentsAsync](https://github.com/icsharpcode/CodeConverter/blob/v10.0.1/CodeConverter/Common/ProjectConversion.cs)
- [VBToCSConversion](https://github.com/icsharpcode/CodeConverter/blob/v10.0.1/CodeConverter/CSharp/VBToCSConversion.cs)

The upstream standalone CLI dropped Framework support; the library did not.
An MSBuildWorkspace spike failed in its separate Framework build host
(`Microsoft.Build.Shared.XMakeElements` initialization). The adapter instead
uses `dotnet msbuild -t:Rebuild -p:ProvideCommandLineArgs=true
-p:BuildProjectReferences=false -getItem:VbcCommandLineArgs` and Roslyn's
`VisualBasicCommandLineParser`. Thus root namespaces, global imports, references,
VB options, conditional symbols, explicit/linked compile items and generated WPF
members come from the **real compiler invocation**, not source-text heuristics.
The dependency graph is built first; referenced assemblies retain full symbols.
No bin/obj source is emitted: WPF and assembly-info sources are regenerated.

Three narrow adapters address reproduced upstream gaps:

1. Resolve explicit interface `get_Item`/`set_Item` methods against Roslyn
   interface indexer symbols; reassemble the original bodies into an indexer.
2. Preserve the VB compiler's implicit `InitializeComponent()` for
   `DesignerGenerated` types with no source constructor.
3. Map `Handles NamedControl.Event` on generated, non-reassigned WPF controls to
   XAML event wiring. `Handles Me.Event` and ordinary WithEvents remain engine
   responsibilities. XAML `x:Class` is root-namespace-qualified.

Project/solution-reference and linked-source edits use `XDocument`, not regex.
Namespaces, field modifiers, comments, business behavior and MVVM defects are
not "cleaned up." No product-specific type names occur in adapter logic.

## Results, repeatability and failures

Exit 0 means either a **dry-run inventory plan**, or successful conversion with
both input and emitted project builds passing. Exit 1 always means failure.
A dry run prints JSON and creates nothing; it does not claim compiler validation.
Mutation runs build under `<output>.incomplete`. After validation, source bytes
are hash-checked into `<output>.publishing`, then atomically renamed to
`<output>`. The published tree contains no bin/obj outputs. This avoids Windows
build-host directory locks without weakening publication checks. The private
build workspace is retained with status `validated-build-workspace`, not
`succeeded`; it can be deleted after the run. Failed build trees retain a
`failed` manifest and actionable diagnostic, never a success-shaped output.
Use a new output path to retry.

`migration-manifest.json` records tool/engine version and binary SHA-256,
evaluated pinned SDK, configuration, compiler arguments and reference hashes, source/output SHA-256,
renames and all changed files, repair rules, generated-document counts,
input/output build evidence and diagnostics. Logs contain elapsed times;
**compare `OutputFiles` hashes**, not log bytes, for determinism. Generated
bin/obj files are deliberately excluded. An already-converted input reports
`NO_VB`, not a fabricated successful conversion.

The independent executable test project copies its fixtures under ignored
`artifacts`, builds and executes the **original VB** and emitted **C#** on STA,
compares behavior, examines actual emitted code and compares repeat output
hashes. Canaries cover custom collection identity/sorting/notifications, typed
and untyped overloaded indexers, Implements, ByRef, optional parameters,
banker's rounding, integer division, root namespaces, generated named controls,
implicit XAML initialization, `Handles Me.Loaded`, named-button events and
idempotent initialization. Negative tests cover collisions, unsupported imports,
dry-run side effects, already-converted input and rejected generated-control
reassignment (including failed-manifest state).

## Application execution evidence

The complete current application graph was copied and processed by this CLI:
**11 TimeTrackingServices sources and 29 ViewModel sources**, including all
five WPF code-behind files, with 10 generated context documents. Both emitted
projects built successfully; a fresh build of the published `TaskOTime.App`
consumer also passed with zero warnings/errors. No original `src` files were
modified. Full repeated application conversion produced **177 identical
source/output file hashes**. Private manifests live under
`artifacts\bulk-verified` and `artifacts\bulk-repeat`.

The original tests pass against the converted graph: **33/33
TimeTrackingServices.Tests and 30/30 AppServer.Tests**. At deeply nested paths,
the Framework MSTest runner reported that
`MSTestAdapter.PlatformServices.Interface, Version=14.0.0.0` could not be loaded
even though the assembly was present. Copying the identical built output to a
shorter owned artifact directory resolved the path-length limitation without
source, dependency or test changes:

```powershell
dotnet build artifacts\bulk-verified\TaskOTime.TimeTrackingServices.Tests\TaskOTime.TimeTrackingServices.Tests.vbproj
Copy-Item artifacts\bulk-verified\TaskOTime.TimeTrackingServices.Tests\bin\Debug\net472 artifacts\t -Recurse
dotnet vstest artifacts\t\TaskOTime.TimeTrackingServices.Tests.dll --TestAdapterPath:artifacts\t

dotnet build artifacts\bulk-verified\TaskOTime.AppServer.Tests\TaskOTime.AppServer.Tests.csproj
Copy-Item artifacts\bulk-verified\TaskOTime.AppServer.Tests\bin\Debug\net461 artifacts\s -Recurse
dotnet vstest artifacts\s\TaskOTime.AppServer.Tests.dll --TestAdapterPath:artifacts\s
```

Use fresh `t`/`s` destinations. On normalized inputs, AppServer's output target
is `net472` instead. Database integration and full application visual acceptance
remain separate parent-stage gates, not claimed by this CLI. Never execute the
old fixed-name SQL reset helper copied from the original graph.

## Explicit limitations / reviewed exceptions

- Only one Debug/Release configuration per run; multitargeting, custom targets,
  unknown imports and valued conditional constants are rejected.
- `.resx` designer/resource identity transformations and legacy `.sln` reference
  rewriting are rejected rather than guessed; use `.slnx`.
- Reassigned generated WPF WithEvents controls and multiple handlers combined
  with an existing XAML event require a reviewed lifetime adapter and fail
  explicitly. Normal source-defined WithEvents is handled by the engine.
- Compiling output is not proof of all runtime semantics. Run the application's
  independent tests and desktop smoke before promoting a candidate.
- Keep tooling, canaries and evidence out of candidate exports.
