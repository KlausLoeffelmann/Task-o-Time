# Repository execution evidence

Executed locally with .NET SDK 10.0.401 on the `tooling-framework-migration`
workspace. Only the tooling directory was edited. Application stages were
generated under `tools\TaskOTime.ProjectMigration\artifacts`; no application
source files, candidate refs or databases were modified.

## Verified results

| Check | Result |
| --- | --- |
| Independent regression runner | 14 scenarios pass; emitted net472/net10, desktop and restored MSTest/VB consumers build; structured metadata builds/publishes transitively |
| Repository inspection | 11 application/test projects, including all remaining VB tests |
| Framework normalization | 8 project files changed; all 11 evaluate to net472 in Debug and Release |
| SDK conversion | 3 classic projects changed; all 11 retain net472 and evaluate as SDK-style |
| Repository normalization replay | Byte-identical manifests for separate output destinations |
| Repository SDK replay | Byte-identical manifests for separate output destinations |
| Second normalization / SDK invocation | Zero changed project files |
| Complete emitted normalized solution build | Successful, 0 warnings / 0 errors |
| Complete emitted SDK solution build | Successful, 0 warnings / 0 errors |
| DataLayer EDMX metadata | CSDL, SSDL and MSL produced in legacy `bin\Debug\Model` |
| Desktop metadata copy target | All three EDMX files copied to app `bin\Debug\net472\Model` |
| .NET10 dry-run before compatibility fixes | Exit 2 with actionable blockers; no output workspace published |

The normalization stage retains project style and language. The SDK stage here
also retains VB: language conversion is a separate tool/workstream. These runs
prove the project-tool stage boundaries independently; they do not claim a
completed C# candidate or a .NET10 application.

Deterministic replay manifests from the verified repository copies:

```text
verified-net472 and verified-net472-replay:
28a34e84695f8e08d45db9f04ecc1510ae0baf933a6231f38b95273a90647c8c

verified-sdk and verified-sdk-replay:
2efbee00c10a72e9eb5561550e99094a517b0a1cdd86e5cb5b37f1335fdbe681
```

These hashes identify this execution's manifests, not a universal baseline
across future source/import/package-state changes. Generated trees and logs are
ignored evidence, not committed application changes.

## Compatibility seams reported

- DataLayer, DTOs and CLI are the three classic projects.
- DataLayer and linked integration-test inputs use EF6 `EntityDeploy`.
- The desktop `CopyEntityFrameworkMetadata` and CLI metadata-copy target
  hardcode the DataLayer's legacy output path. SDK conversion deliberately
  disables framework-folder appending for converted classic projects, preserving
  those paths and the targets instead of deleting metadata deployment.
- CLI `JavaScriptSerializer`/System.Web serialization is also compiled into the
  source-linked AppServer test project. Retargeting reports both consumers.
- All 11 reference-assembly package references are aligned with normalization.
- Modern retargeting also reports Framework-specific EntityFramework HintPaths
  and missing `System.Configuration.ConfigurationManager` package references.

No EF package upgrades, serializer replacements, EDMX rewriting or business
logic changes are hidden inside project conversion. Real runtime/SQL/WPF
workflow acceptance remains the subsequent compatibility workstream.

## Dependency and desktop-reference hardening

Independent review identified two generic omissions in the initial tool.
The follow-up tests reproduce and cover both:

- Framework-conditioned `Reference`/`PackageReference` items, and conditional
  custom metadata on otherwise retained dependencies, now fail output validation
  instead of disappearing silently. Four negative fixture variants confirm
  exit 2 and no published workspace.
- Explicit PresentationFramework/System.Windows.Forms projects without SDK
  desktop flags now gain the matching `UseWPF`/`UseWindowsForms` setting.
  Both emitted projects compile actual desktop types under net10.0-windows;
  second invocations are no-ops, and conflicting false flags fail closed.

The complete real normalization and SDK stages were repeated with the stricter
dependency checks (`artifacts\dependency-net472` and `artifacts\dependency-sdk`).
Both passed input/output evaluation for all 11 projects, with the same 8/3
changed-project counts. The earlier manifest hashes above remain evidence for
the initial run; the expanded dependency inventories intentionally change
manifest content in this follow-up.

## Already-restored input integration

The restored-state regression uses actual MSTest.TestAdapter 2.2.10,
MSTest.TestFramework 2.2.10 and Microsoft.NET.Test.Sdk 17.2.0 packages, plus a
nested VB project and explicit NuGet-root-based assembly HintPath. It builds the
source first and verifies:

- Imported adapter `None` assets retain NuGet import provenance in the manifest.
- Migration does not change the source's `obj\project.assets.json`.
- Emitted source contains no copied DLLs or package cache tree.
- Normal output restore/build deploys the adapter successfully.
- A second invocation on built output is a no-op.
- Explicit application links to cache files remain rejected.
- Conditioning the actual adapter PackageReference on the old framework still
  produces an output-dependency mismatch, rather than silently dropping it.

A separate complete repository copy was **built/restored before normalization**
at `artifacts\restored-source`, without changing application project/source
files. This exposed and now covers the real graph's additional restored-state
differences: NuGet-root HintPath expansion, imported VB reference-pack assemblies,
and SDK default `None` globs including nested TestDoubles `bin`/`obj` files.

Normalization of that built graph succeeded with 11 projects / 8 changed files;
the emitted `artifacts\restored-net472` solution built successfully. SDK
conversion then consumed that **built/restored normalized graph**, succeeded
with 11 projects / 3 changed files, and `artifacts\restored-sdk` also built with
zero warnings/errors. No source `obj` deletion, adapter removal, package-cache
copying, or database operations were used.
