# .NET 10 compatibility execution

Input: accepted SDK-style net472 application at `307ecf3`. The application
worktree is `files\net10`, on `candidate-csharp-net10`; private tool code and
evidence remain in the separate `files\framework` worktree. No tooling,
assessment, manifests, archives or private STA host enter the candidate.
No S3 tag is created by this workstream.

## Reproduction

From `tools\TaskOTime.ProjectMigration`, build the pinned-SDK tool:

```powershell
dotnet build --nologo --verbosity quiet
$cli = '.\bin\Debug\net10.0\TaskOTime.ProjectMigration.dll'

# $input is an application-only S2a source copy with the reviewed source
# exceptions below applied. Each output path must be new.
dotnet $cli prepare-net10 --source $input --output .\artifacts\prepared --dry-run
dotnet $cli prepare-net10 --source $input --output .\artifacts\prepared
dotnet $cli retarget --framework net10.0 --wpf-framework net10.0-windows `
  --source .\artifacts\prepared --output .\artifacts\modern
```

Apply only the emitted application changes to `src\TaskOTime`. The generated
build template belongs in `src\TaskOTime\build`; the CLI itself does not.
All eleven TFMs are emitted by the CLI, not hand-edited. Preparation deliberately
does not claim its still-net472 output can compile modern source/API changes:
it is an intermediate plan before retargeting, not another candidate checkpoint.

The final replay used a tracked-source archive of `307ecf3` extracted under
`artifacts\net10-replay-s2a`, then overlaid only the three reviewed source/test
files below. `net10-v11-prepared.json` and `net10-v11-final.json` record
the final version 1.1 tool operations; corresponding source-only output
directories are retained. All emitted project/target files are compared
byte-for-byte with the candidate.
Only emitted project files and the metadata target were applied to the candidate.
Earlier `net10-prepared`/`net10-retargeted` manifests are iterative evidence,
not the final replay.

Candidate validation, from the `net10` worktree:

```powershell
dotnet build src\TaskOTime\TaskOTime.slnx --nologo --verbosity quiet
dotnet test src\TaskOTime\TaskOTime.slnx --no-build --no-restore --nologo --verbosity quiet
dotnet publish src\TaskOTime\TaskOTime.App\TaskOTime.App.csproj `
  --configuration Release --no-restore --nologo --verbosity quiet `
  --output $privatePublishOutput
```

## Generated compatibility changes

- All eleven application/test projects, including retained VB tests, target
  net10.0 or net10.0-windows. WPF consumers are propagated through project
  references. The desktop uses Microsoft.NET.Sdk + UseWPF.
- EF6 is 6.5.2 everywhere it was directly referenced. Its explicit .NET 10
  first-class-span compatibility fix is documented in Microsoft's EF6 release
  history: <https://learn.microsoft.com/en-us/ef/ef6/what-is-new/>.
  EntityFramework.SqlServer and System.Data.SqlClient identities are unchanged;
  no EF Core, provider or database-schema conversion occurred.
- The original EDMX file is unchanged. The emitted XSLT template copies its
  runtime conceptual, storage and mapping elements into incremental build
  outputs. Metadata reaches consumers/publish through MSBuild's project-output
  item graph, not hardcoded sibling bin paths. Linked integration-test models
  generate their own equivalent runtime metadata.
- Framework reference-assembly packages and obsolete simple Framework assembly
  references are removed. Microsoft.VisualBasic is supplied by modern .NET.
- ConfigurationManager 10.0.0 is explicit in non-desktop consumers needing it.
  WPF uses its shared-framework copy rather than a redundant pruned package.
  Existing provider/connection configuration and settings identities remain.
- Tests use Microsoft.NET.Test.Sdk 17.14.1 and MSTest 3.6.4 without changing test
  intent. Newtonsoft.Json 13.0.3 supplies the CLI and source-linked test parser.

## Reviewed source exceptions

These are deliberately separate from mechanical project XML transformations:

1. `TaskOTime.Cli\DemoDataConfigLoader.cs`: replace unavailable System.Web
   serialization with Newtonsoft's supported reader. Retain the dictionary/
   array graph expected by existing helpers, ordinal property names, defaults,
   duplicate-key behavior, string/bool conversions, Int32/Int64/Decimal/Double
   numeric ladder, midpoint rounding, exponent semantics, empty-input handling,
   escaped Microsoft-date behavior, and legacy accepted single-quoted/unquoted
   property syntax. Reject comments/trailing commas and retain input/depth
   limits and root-shape errors. Numeric/date behavior was characterized locally
   against the Framework serializer before writing the compatibility canaries.
2. `TaskOTime.App\Properties\AssemblyInfo.cs`: declare its existing Windows-only
   platform contract explicitly. The project intentionally retains handwritten
   assembly information, so the SDK otherwise cannot emit its platform attribute.
3. `TaskOTime.AppServer.Tests\DemoDataJsonCompatibilityTests.cs`: three additional
   canaries cover numeric/type/error/shape/date behavior. Existing tests are not
   weakened or replaced.

No business, MVVM, localization, theme, comment, password-format or intentional
defect cleanup is included. In particular the functioning legacy PBKDF2
constructors are unchanged; the final full rebuild reports five SYSLIB0060
obsolescence warnings from those retained implementations. The platform/SDK/
redundant-package warnings caused by retargeting were corrected rather than
suppressed.

## Evidence and handoff boundary

- Full .NET 10 solution builds successfully.
- All **78 original tests pass**, including **15 authorized GUID-owned SQL
  tests**, plus all **3 new JSON tests**: **81 total**.
- A fresh Release desktop publish succeeds and contains all three `Model`
  metadata files. No application/demo database command was executed.
- The private CLI has **14 passing regression scenarios**, including actual
  restored MSTest/VB graphs, modern test-host idempotence, two independent EDMX
  models, XML semicolon preservation, incremental generation, and transitive
  build/publish metadata copying.
- The subsequent 1.1.1 safety review adds a fifteenth scenario rejecting
  evaluated EF alias overrides, embedded EDMX deployment, and mismatched
  metadata copy sources/destinations. It does not change candidate build files.
- Version 1.1.2 further proves reference-graph delivery rather than workspace
  membership. Sixteen scenarios now cover unreachable/private/reconfigured
  producers, disabled propagation, transitive content-only references, and
  equivalent consumer-local linked metadata. Candidate build files remain
  byte-identical.
- Logs/TRX/published files remain under the private tool's ignored `artifacts`
  directory (`net10-final-rebuild.log`, `net10-final-tests*`, earlier
  `net10-acceptance-*`, `net10-publish.log`, `net10-app-publish`).

The SQL tests use the already accepted GUID-named, ownership-token-guarded
fixture. No fixed-name reset or demo-data writes were run. Real desktop smoke,
the private .NET 10 STA host, formal S3 assessment and S3 tagging remain the
parent's acceptance work.
