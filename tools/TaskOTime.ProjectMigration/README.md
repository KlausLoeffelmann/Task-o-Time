# Local project migration CLI

Independent .NET 10 tooling for staged **project** migration, not language conversion.
No application type names, hosted services, external converter, NuGet tool packages,
or source-text replacement rules are involved. It uses `System.Xml.Linq` for edits
and the pinned local SDK's `dotnet msbuild -getProperty/-getItem` JSON API for
evaluation. Run only on trusted projects: MSBuild evaluation can execute property
functions and SDK resolvers. Evaluation does not run build targets or restore.

## Immediate use

From this directory (`tools\TaskOTime.ProjectMigration`), with SDK **10.0.401** installed:

```powershell
dotnet build --nologo --verbosity quiet
$cli = '.\bin\Debug\net10.0\TaskOTime.ProjectMigration.dll'

dotnet $cli inspect --source ..\..\src\TaskOTime

# All 11 production/test projects, including the remaining VB tests.
# Use NEW output paths: the CLI never overwrites an existing workspace.
dotnet $cli normalize-framework --target net472 `
  --source ..\..\src\TaskOTime --output .\artifacts\s1 --dry-run
dotnet $cli normalize-framework --target net472 `
  --source ..\..\src\TaskOTime --output .\artifacts\s1

# On the language-converted net472 tree, substitute that workspace for s1.
# This command itself preserves language and the current framework.
dotnet $cli convert-projects --sdk-style `
  --source .\artifacts\s1 --output .\artifacts\s2a --dry-run
dotnet $cli convert-projects --sdk-style `
  --source .\artifacts\s1 --output .\artifacts\s2a

dotnet build .\artifacts\s2a\TaskOTime.slnx --nologo --verbosity quiet

# After reviewed serializer/API changes, prepare the known compatibility seams.
dotnet $cli prepare-net10 --source .\artifacts\s2a --output .\artifacts\prepared --dry-run
dotnet $cli prepare-net10 --source .\artifacts\s2a --output .\artifacts\prepared

# Unknown/unresolved compatibility seams still return 2.
dotnet $cli retarget --framework net10.0 --wpf-framework net10.0-windows `
  --source .\artifacts\prepared --output .\artifacts\s3 --dry-run
```

The tool's own `global.json` is copied next to its binary, so the subprocess also
selects SDK 10.0.401 regardless of the caller's working directory. The SDK must
already be installed. The source application's `global.json`, if any, is copied
unchanged; pinning how a later application build selects its SDK is a separate
workspace concern. There are no third-party package dependencies for this CLI.

## Contracts

- `inspect` emits evaluated Debug/Release project/item inventories and seam
  diagnostics. `--configuration Debug,Release` and `--platform AnyCPU` select the
  evaluation matrix. Supply custom configurations/platforms explicitly; this is
  not a proof of arbitrary unevaluated condition branches.
- All mutation commands require distinct, non-overlapping `--source` and
  non-existing `--output` directories. `--dry-run` (`--dryrun`) writes nothing;
  its `ChangedFiles[].ProposedXml` is the complete proposed project XML.
- The source scope must contain all linked source, content, resources, and
  project references. External/absolute source items, junctions and symlinks are
  rejected rather than silently copied from elsewhere.
  Evaluated assets whose defining import **and** asset/assembly-hint location
  are in the NuGet cache are package-provided, not application source links.
  They remain in the inventory, but are supplied by normal PackageReference
  restore in the output, never copied from the cache. Explicitly declaring the
  same external file in an application project remains an error.
- C# and VB application/test projects are discovered recursively. Projects
  beneath `tools` or `assessment` are not migrated; their files are copied
  unchanged when the source is a whole repository. This is **not a candidate
  export command**: supply an application-only workspace for clean stage trees.
- `.git` (including worktree pointer files), `.vs`, `bin`, `obj`, `artifacts`,
  `packages`, `node_modules`, `TestResults`, and prior migration manifests are
  excluded from copying. Restore generated dependencies separately before
  building. A workspace needing a checked-in file in those directories is
  outside this tool's supported layout.
- Normalization edits literal target declarations and matching
  `Microsoft.NETFramework.ReferenceAssemblies.net4*` package IDs together,
  retaining package versions, conditions and metadata. It does not downgrade
  modern projects or rewrite inherited/computed/multi-target frameworks.
- Classic conversion uses **explicit SDK props/targets imports** rather than a
  root `Sdk` attribute, preserving where the language-target import occurred
  relative to custom targets. This is valid SDK-style MSBuild.
- Explicit compile/link/resource/configuration items remain explicit. Default
  item globs and generated assembly information are disabled for converted
  classic projects. Assembly/root-namespace values, resource naming convention,
  conditional settings and legacy output paths are retained. Framework
  normalization changes only the SDK's existing appended target-folder suffix.
- Unknown classic imports fail closed. Inline custom targets are retained and
  reported for review; inspect the dry-run XML and execute the relevant targets
  in subsequent builds. There is no pretend generic rewrite for arbitrary
  build logic.
- `prepare-net10` is an explicit compatibility operation, not another accepted
  Framework checkpoint. It keeps TFMs unchanged, upgrades the reviewed EF6
  6.5.1 -> 6.5.2 and MSTest 2.2.10 -> 3.6.4 / test-SDK 17.2.0 -> 17.14.1
  package versions, and replaces simple assembly references with appropriate
  packages. It refuses unknown versions/custom reference semantics.
  Reference removal is checked against **evaluated metadata in every selected
  configuration**, not just the declaring XML. Aliases, overridden copy-local
  settings, custom metadata and noncanonical assembly paths are rejected,
  including values introduced by `Reference Update` or imported item defaults.
  The serializer source/API replacement must be reviewed first; this command
  does not rewrite C#/VB source.
- Preparation maps literal `EntityDeploy` items to `EntityModel`, retains their
  original metadata, and adds a shared `build\EntityFramework.Metadata.targets`
  template. Built-in local MSBuild XSLT extracts the original runtime
  CSDL/SSDL/MSL elements without changing provider, schema or mappings.
  Incremental generation uses evaluated intermediate paths; standard
  `Content`/`TargetPath` project-reference propagation deploys and publishes
  metadata. Only simple old metadata-copy targets for known EDMX model names
  are removed. Unknown extra tasks/semantics fail closed.
  The actual EDMX and evaluated item settings are inspected before conversion:
  `EmbedInOutputAssembly` is explicitly unsupported because loose files cannot
  preserve embedded resource names or `res://` connections. Explicit
  `CopyToOutputDirectory` and the supported default filesystem layout are
  accepted. Every removed copy source must resolve to an evaluated producer
  metadata output, and its destination must equal the consumer's propagated
  metadata layout in every selected configuration. Custom folders, unrelated
  producers, unresolved paths and unmatched directory-creation effects are
  rejected rather than silently redirected.
- Modern retargeting requires an SDK checkpoint, removes obsolete reference-
  assembly packages and simple implicit Framework references, and propagates
  `net10.0-windows` from WPF/WinForms projects through project references.
  Simple explicit desktop assembly references establish `UseWPF` or
  `UseWindowsForms` before their removal, ensuring the WindowsDesktop reference
  pack is actually selected. Conflicting/conditional flags or imported desktop
  reference shapes require explicit review instead of a silent flag override.
  Known incompatible serialization, EntityDeploy, hardcoded metadata copy,
  Framework assembly hint paths, and missing ConfigurationManager packages are
  **errors**, not silently waived warnings. Correct these seams with behavior
  tests before retrying. The CLI is not a complete .NET API compatibility analyzer.
- Before publishing a mutation, the tool evaluates the emitted project graph
  for every selected configuration and checks frameworks, assembly identity,
  source/resource/link/content/None/reference items and non-retarget output paths.
  Evaluated `Reference` and `PackageReference` dependencies retain **all custom
  metadata**, including dependency conditions' evaluated effects. Only intended
  framework-package renames, recorded explicit removals, and SDK/reference-pack
  generated framework references are allowed to differ. A dependency or metadata
  value disappearing because its old-framework condition became false is an
  error; conditions are not guessed or text-rewritten.
  Comparison excludes package-provided cache assets and uncustomized SDK `None`
  globs of nested `bin`/`obj` output. Thus already-restored/built input can be
  compared with fresh output without deleting source build state or silently
  dropping adapters. The source's evaluated NuGet root is supplied as an
  overridable environment fallback during output evaluation, preserving expanded
  package HintPaths without overriding project/import property declarations.
  It does not automatically build or execute application/tests.
- Output is prepared in an owned sibling staging directory. Normally it is
  renamed into place after validation. Windows long-path rename failures use a
  verified copy with an incomplete marker and write the success manifest last.
  Failed validation publishes nothing; exceptions clean up owned partial
  output. Interrupted copies with `.projectmigration-incomplete` are rejected
  as future inputs. A crash before cleanup may require removing that owned
  incomplete directory before retrying.
- Exit **0** means inspection/planning or emitted-project evaluation succeeded;
  exit **2** means unsupported input, invalid options, evaluation failure or
  filesystem error. It does not certify runtime compatibility or a successful
  application build.

## Reproducibility and verification

Every successful mutation writes `migration-manifest.json`; all commands emit
JSON to stdout. Errors also use stderr for exceptional failures. The manifest
records tool/SDK versions, normalized command/configuration, sorted input/output
SHA-256 hashes, changed project XML, rule IDs, input/output evaluations, and
warnings/errors requiring review. Output file hashes exclude the manifest
itself. Paths are workspace-relative; timestamps, random staging names and
destination paths are absent, making replays comparable across output locations.
`Projects` is the input inventory; `OutputProjects` is the validated emitted
inventory (empty for inspection/dry-run). `Verification` distinguishes a
read-only plan from evaluated output. Identical source, SDK, configuration and
restore/import state produce identical manifests; external imports/package
caches are environmental inputs, not vendored or hashed by this CLI.

Re-running a stage against its own completed output, using a new destination,
must produce `ChangedFiles: []`. Stage-manifest replacement is intentionally not
counted as an application change. To debug exceptional failures locally, set
`PROJECT_MIGRATION_TRACE=1` for a stack trace on stderr.

The regression runner itself has no package dependencies:

```powershell
dotnet run --project .\tests\TaskOTime.ProjectMigration.Tests.csproj --verbosity quiet
```

The runner creates synthetic fixtures under this directory's ignored
`artifacts` folder (never system temporary directories). It verifies actual
builds and assembly/resource behavior for emitted net472/net10 projects, VB
retention, WPF and transitive consumers, deterministic replay, idempotence,
custom-target/condition/link preservation, and fail-closed/rollback behavior.
Additional regressions reject disappearing framework-conditioned assembly and
package dependencies/custom metadata, and compile retargeted WPF and WinForms
projects that originally used explicit desktop assembly references without SDK
desktop flags.
Its restored-state canary builds an actual MSTest 2.2.10 project and nested VB
dependency before migration, so these fixture builds restore their pinned
MSTest/test-SDK/reference-assembly packages through normal NuGet semantics.
It proves all four imported adapter assets are inventoried, no package DLLs are
copied into emitted source, fresh output builds deploy the adapter, and an
old-framework-conditioned adapter PackageReference still fails closed.
Modern test-SDK packages contribute an implicit `Exe` output type after restore.
On an otherwise unchanged modern target, that known package-generated default
may differ from the un-restored SDK `Library` default; explicit OutputType
declarations are never exempted. Restored modern-test idempotence is covered.
It never accesses a database. See [repository execution evidence](docs\Execution-Evidence.md).
For the separate authorized candidate compatibility run, see
[.NET 10 execution and reviewed source exceptions](docs\Net10-Execution.md).
