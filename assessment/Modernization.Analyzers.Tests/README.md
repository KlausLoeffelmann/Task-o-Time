# Independent compiler-assisted modernization assessment

This **xUnit** project is independent of the legacy MSTest
`ArchitectureFitness.Tests` project. Static diagnostics use Roslyn
`DiagnosticAnalyzer`; stage policy and independent CLI replay run outside its
callbacks. The evaluator does not invoke the application or query/mutate a
database. Replay executes synthetic emitted-code behavior in a child process.
Compilation and input-extraction failures fail closed.

## Portable bundle and execution

Copy exactly the relative files in `..\Bundle.files` into a repository-root
`assessment\` directory. Do not copy `ArchitectureFitness.Tests`, `bin`, `obj`,
reports/TRX, compiler intermediates, or archived scenario documents. The bundle
contains both external MSBuild configuration files, the scope manifest, the shared
configuration source, compiler-input target, all analyzer/test sources, and this
README. `Candidate-Prompt.md` and `Assessor-Guide.md` are versioned top-level
assessment documents; keep their stage contracts aligned with this bundle.
The assessment is not an application project reference.

Use Windows and the pinned .NET 10 SDK/runtime; enter the assessment directory
before invoking dotnet so its private `global.json` is honored. Roslyn 5.0 matches
the .NET 10 compiler generation; a .NET 11 SDK is deliberately not accepted.
The application uses .NET Framework 4.6.1 reference assemblies restored from its
existing package dependencies. From a fresh golden-branch checkout:

```powershell
$source = Join-Path $PWD 'src\TaskOTime'
Set-Location .\assessment
# Compile/restore the solution, but do not run the application or database tests.
dotnet build ..\src\TaskOTime\TaskOTime.slnx --verbosity quiet
$env:ASSESSMENT_STAGE = 'S0'
$env:ASSESSMENT_MODE = 'starting-point-integrity'
dotnet test .\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj `
  "-p:TaskOTimeSourceRoot=$source" --filter 'Category=AnalyzerUnit|Category=ReplayUnit|Category=RepositoryScan|Category=StagePreservation'
# Candidate acceptance: expected to FAIL on the deliberately bad starting app.
$env:ASSESSMENT_MODE = 'final-delivery'
dotnet test .\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj `
  "-p:TaskOTimeSourceRoot=$source" --filter 'Category=Modernization'
```

For the existing external installation, substitute its `Evaluators\` path for
`assessment\`. `TaskOTimeSourceRoot` or `TASKOTIME_SOURCE_ROOT` selects the source
directory containing the application solution. The environment variable also
overrides the runtime source path. A repository-root `assessment\` automatically
finds `..\src\TaskOTime`; the external workstation fallback is retained for the
original scenario. `AssessmentRoot` and `EvaluatorArtifactRoot` are overridable
MSBuild properties. Supply absolute paths when overriding them.

`ScenarioScope.xml` discovers production projects below the source root and
repository `tools\` / `tooling\` roots. Add a relative `<Discovery><Root>` or set
`MIGRATION_TOOL_ROOT` for another tooling location. **Build/restore independently
supplied tooling projects first**; they need not be referenced by the application.
Trusted replay `Projects` paths are explicitly unioned into the load set even
outside all discovery roots. Missing project files or source-compilation errors
invalidate the evaluation; a declared CLI binary cannot substitute for its source.
Hidden/build/package folders and the assessment itself are excluded. Test projects
are loaded for evaluated framework validity, but excluded from production
quality diagnostics using test-framework references and `IsTestProject`.
Renamed projects are discovered by project
extension, not a list of original VB project filenames.

| xUnit trait | Meaning |
| --- | --- |
| `Category=AnalyzerUnit` | Bilingual positive/negative, alias, renamed, generated-source, XML, empty-input and compiler-failure fixtures; should pass |
| `Category=RepositoryScan` | Complete source compilation plus report generation; passes on a compilable bad application |
| `Category=ReplayUnit` | Actual child-process replay, converter-shaped empty-class rejection, emitted compilation/behavior and unsupported-input harness regressions |
| `Category=StagePreservation` | Explicit starting-point-integrity only; requires stage language/style/targets and original nonmigration defects, including exactly one BUS001/BUS002 |
| `Category=ReplayRequest` | Explicit metadata-only external request export; no submission project evaluation or acceptance verdict |
| `Category=ReferenceReviewRequest` | Explicit metadata-only owned-source review request export; not review approval or acceptance |
| `Category=Modernization` | Final-delivery only; requires valid inputs **and zero mandatory acceptance diagnostics**; intentionally fails on the bad baseline |

Use explicit category filters. Never include StagePreservation in ideal/final
acceptance. Generic scanning has no assertion requiring business defects.
No static rule is implemented as a runtime UI test.

## Input boundary and reproducibility

MSBuild supplies evaluated compiler arguments, language options, references,
project edges, resource manifest names and items. Source project dependencies are
recompiled and emitted to **in-memory** metadata; existing frontend DLL contents
do not determine semantic verdicts. Restored assets/imports and reference outputs
are prerequisite build inputs, not execution evidence. Extraction disables compiler
execution and project-reference builds. WPF/design-time intermediates are written
under `Artifacts\compiler-inputs`, keyed by full project/configuration/profile hash,
outside the application; normal application
`obj` assets/imports are read, not replaced. The only intentional parse-option
adjustment is `DocumentationMode.Parse`: XML documentation must remain Roslyn
syntax trivia even when the candidate does not request a `/doc` output.

SDK implementation and reference-assembly paths are both mapped to source-project
identity before replacement. The mapping uses evaluated `TargetRefPath`,
`ReferenceAssembly`, `ReferencePathWithRefAssemblies` and source-project metadata,
not DLL basenames. Missing or ambiguous project identities fail closed. Metadata
regressions compile an equivalent .NET 10 executable/library pair using the actual
source-image replacement, without reading a stale reference DLL.
Evaluated `ReferenceOutputAssembly=false` edges remain in the loaded build graph
and their sources are checked, but do not require a compiler assembly reference.
Build-only test edges do not establish a helper role: integration tests also
launch standalone products. Executable helpers need an affirmative trusted
`Discovery/TestSupport` declaration (`Owner` and `Project`) backed by the
evaluated build-only test edge. Independent executable roots remain production
by default; declared producers and production-reference reachability override
helper roles. A helper-like filename alone grants no role.
Project-shaped conversion inputs can have an explicit trusted
`Discovery/FixtureData` role in `ScenarioScope.xml`, with `Owner` and `Root`
paths relative to the candidate source root. The owner must compile as a test/tool,
the root must be its proper subtree, and evaluated `None`/`Content` source data
must be present without overlapping the owner's compiled inputs. Only standalone
discovery is suppressed: declared producer projects and every project-reference
dependency are still loaded and checked, even inside a fixture-data root.
Neither a directory named `Fixtures` nor an unevaluated exclusion grants this
role. Existing generated-artifact discovery exclusions are case-insensitive;
explicit producer roots and reference edges still take precedence.

The reports are `Artifacts\Reports\<stage>-<mode>-<configuration>\roslyn-diagnostics.json` and
`repository-assessment.csv` in the same directory (or the configured artifact root),
written before test assertions, including when the modernization gate fails.
JSON contains compilation validity,
sorted project/source/AdditionalFile paths, selected architecture types, all
diagnostics, per-criterion integer metrics, evaluation-valid/hard-gate/unverified
status, evaluated project framework identifier/version/platform, SDK style and
configuration, trusted stage/mode, stage-integrity failures, actual replay
evidence, criterion applicability, applicable denominator and remaining-work
score. CSV rubric `2026-09-stage-v4`
uses bounded criterion scores rather than dividing by diagnostic counts:
business 28, MVVM 18, localization 14, VB-to-C# 9, theme 9, comments 5,
Main Data naming 5, migration tool 5, SDK-style 3.5, and .NET 10 target 3.5.
It contains criterion evidence and an `OVERALL` row. The universal full-outcome
score remains separate from remaining-work credit. Deferred and pre-satisfied
criteria are not earned passes; S3 migration-tool work is not applicable.
Invalid evaluations score zero, with INVALID rows and null remaining score.
There are no timestamps or inferred usage/cost estimates. Static diagnostics
are sorted by ID, path, line, column and message.
`OutcomeAnalyzer` runs a compilation action over the supplied compilation corpus;
cross-project locations are Roslyn external-file diagnostic locations, retaining
their source spans. XML is supplied through Roslyn `AdditionalText` inputs.
Infrastructure failures use `LOAD001`; compiler diagnostics and analyzer input
gate `ASM001` prevent success. Analyzer exceptions also prevent success.

`Examined` / `Satisfied` are bounded static evidence units, **not percentages of
correctness**: comment trivia without lexicon evidence; documented declarations;
authored types by language; resource/accessor evidence; resolved theme pairs;
required source roles; static tool-shape/companion-fixture evidence (not conversion
success); EF context contracts. `Unverified`
records unsupported theme coverage. Legacy MOD/BUS metrics count compiled source
trees examined and diagnostics (their `Satisfied` field is not inferred).
Diagnostic counts can exceed evidence counts when a unit has several findings.

## Rules and deliberate limits

### Existing architecture and business rules (preserved)

| ID | Semantic evidence |
| --- | --- |
| MOD001 | Concrete WPF/view signatures, construction, member accesses and event references |
| MOD002 | Concrete-view-coupled presentation lacking inherited/direct `INotifyPropertyChanged` |
| MOD003 | Frontend internals exposed to non-test friends |
| MOD004 | Source-symbol presentation-to-concrete-view dependency edges |
| MOD005 | Fixed WPF color/font symbols and constant code construction in selected presentation |
| MOD006 | VB in selected main/master-data presentation (retained alongside broader LNG001) |
| BUS001 | Positional/default project origin flowing into production booking project identity |
| BUS002 | `TimeSpan.Minutes` flowing into elapsed date arithmetic without hours composition |

`ScenarioScope.xml` retains the original master-data selection and protected core.
The loader's selection expands `MasterData`→`MainData`, `MasterTask`→`MainTask`, and
protected VB→C# source aliases, so the expected type/file renames cannot evade the
selection. Empty selected presentation is a source-scope diagnostic.
Names select inputs; coupling, notification, styling and language diagnoses still
require semantic evidence. BUS001/BUS002 run on every production source project.

Business provenance follows bounded locals, tuples/deconstruction, conversions,
nonvirtual source helpers and agreeing branch origins. Predicated project selection,
`TotalMinutes`, lookalikes, and clock composition have negative fixtures. Compound,
increment and unsupported tuple mutations invalidate previous origins. Recursion,
virtual dispatch, heap mutation and arbitrary query wrappers are not fully modeled;
ambiguous flow is not called a proven business defect. The domain booking DTO
identities are deliberate compatibility contracts, not frontend method-name tests.

### English comments and retained documentation

* **ENG001** is emitted by a dedicated diagnostic analyzer over Roslyn C#/VB
  comment and XML-documentation **syntax trivia**, not string literals or entire
  source files. It estimates English, Dutch, and German from sets of common
  article/function/content words and requires minimum vote count, token coverage,
  and a confidence margin over English. Sentence fragments, mixed prose, and
  representative English/Dutch/German comments have calibrated fixtures.
* XML markup is removed **structurally**. Element names, attributes/`cref`,
  `<c>`, `<code>` and `<see>` content do not vote as prose. CamelCase,
  underscore/dotted identifiers, inline backtick code, and `TODO` alone do not
  establish a language finding. There are no expected comment hashes, paths,
  exact translations or required English sentences.
* **ENG002** measures meaningful XML prose (at least four words after structural
  removal) on required time-item interfaces, typed time collections and observable
  authentication/login coordinators. It requires type prose and documentation on
  at least half of the public declared ordinary methods/properties. Time roles
  use nullable temporal and recursive-link property types, collection constraints
  and interface implementation; login uses a credential-taking authentication
  interface and observable coordinator dependency, not class names.

These rules prevent deleting all essential docs, but cannot prove translation
quality, factual equivalence, good grammar, or that all prose is English. English
fixtures with imperfect grammar are not rejected just for stylistic quality.
Unrecognized languages/phrasing and very short prose can evade the lexicon.
`inheritdoc`/`include` that do not provide inspectable local prose do not satisfy
the documentation threshold; documentation outside compiler source trivia (for
example historical Markdown) is not a language verdict input.

### Microsoft extensions localization in a WPF application

* **LOC001** requires a classic `.resx` root/header/data schema, nonempty textual
  entries, a neutral resource and the language cultures required by the external
  `ScenarioScope.xml` localization policy, all with exact key parity. The supplied
  scenario requires **neutral English plus German (`de`), Dutch (`nl`), and
  Spanish (`es`)**. Required culture files must not merely duplicate all neutral
  text.
  Regional variants such as `de-DE` and `nl-BE` satisfy their language families;
  two German variants cannot substitute for Dutch. Resource filenames/classes
  are not prescribed. Duplicate keys/headers, absent required languages and key
  mismatches fail. Per-language `LocalizationCulture.*` metrics record aligned
  resource-group coverage.
* A fixture or another external policy can specify different language families
  or only one. With no explicit language requirements, isolated fixtures retain
  the generic minimum of one recognized culture variant; malformed requirements
  fail closed rather than silently reverting to that minimum.
* The policy records intended neutral English. If an assembly declares
  `NeutralResourcesLanguageAttribute`, it must agree with that policy (regional
  English is accepted). No new attribute is mandatory. Schema/culture coverage
  and a declared language are **not proof that neutral prose is English or that
  any translation is correct**; the translation-quality limitations below remain.
* The repository policy additionally requires an evaluated
  `Microsoft.Extensions.Localization` dependency and a symbol-resolved
  `IStringLocalizer` key lookup that reaches presentation output. A real
  `ResourceManagerStringLocalizerFactory` (including an interface-typed factory),
  evaluated manifest base name, assembly and resource path are resolved through
  source fields/properties. Forwarding methods, indexers, returned lookup delegates,
  formatting facades and key-preserving message objects are traced with call-site
  parameter substitution. A generated strongly typed accessor is **not mandatory**
  for this provider path; namespace-only localizer stubs do not qualify. When
  reaching definitions are not proven, all factory/provider alternatives must
  resolve and agree. An earlier factory assignment cannot authenticate a later
  key-only localizer or another unknown replacement, even when that replacement
  implements the genuine library interface. Actual
  `ResourceManager` construction, same-assembly input, symbol-resolved
  `GetString`, constant key/base-name alignment with evaluated manifest metadata,
  and a strongly typed static string accessor are examined, **including generated
  designer source**. Generated-source metadata/conventions or the standard
  `GeneratedCodeAttribute` identify the generated accessor role; this does not
  prove who generated it. Resource/toolkit/class/file names are not prescribed.
* The accessor/localizer must flow into a known WPF text property, `MessageBox` string
  argument, observable UI output, or structurally resolved XAML `x:Static`
  property use. The validated lookup must also contribute to the getter's
  **returned value**; discarded calls and unrelated local initializers do not
  qualify. Returned local aliases and resource-valued branches are supported.
  Unused resource construction/imports/accessors do not suffice. An inspected
  markup extension returning a WPF binding to the verified provider's indexer is
  also supported; a dead binding, wrong path or hardcoded return is not credited.
  Parity applies to consumed resource groups, not unused template resource files.
* **LOC002** diagnoses literal display text at those semantic sinks and WPF XAML
  display attributes/property elements. Observable outputs are recognized through
  bound property names or standard display-output contracts. Under the repository
  policy, literal checks cover the required surfaces and Options, plus their
  nested controls, DataContext types and bound child models. XAML class identity
  and semantic relationships supplement external surface aliases; unrelated
  maintenance screens are not extra localization requirements. An unknown object's
  stored data does not inherit every assignment to that property on unrelated
  instances; known object initializers and source getters remain inspected.
  Producer tracing
  follows source returns, locals, fields and setter-helper arguments, but excludes
  conditional predicates and external lookup/provider metadata. Logs, identifiers,
  format-only strings, `nameof`, environment keys, resource keys and culture names
  are not independently treated as displayed prose. There is no class-wide
  generated-accessor exemption: a UI-consumed hardcoded getter return or literal
  fallback is diagnosed, including consumption through XAML `x:Static`.

The repository policy requires applied keys on Login Experience, Main
time-collection UI, add/edit booking dialog, and Project Main Data dialog. The
Options surface must contain a language-selection control whose state reaches
current/default culture behavior through its bound setter, the bound `ICommand`
`Execute` implementation and its direct callees or invoked callbacks, a resolved
XAML UI event, or the reachable handlers installed
by presentation constructors. Constructor-forwarded command delegates retain
call-site arguments. Roslyn interface-implementation and override resolution
select the actual `ICommand.Execute` dispatch target; private overloads and
overridden base bodies are not independent command roots.
Unbound/inert commands and private dead culture calls do
not establish this connection. Merely
moving strings into a XAML dictionary is insufficient. The checker establishes
static UI consumption, not that a particular window is ever opened or that every
translation is linguistically correct. Reflection, arbitrary custom markup
extensions, source generators and unmodeled external string producers are not
proved. Source-generator-dependent inputs that cannot compile fail closed.

### Project-system and framework migration

**PRJ001/PRJ002** use evaluated MSBuild metadata supplied as AdditionalFiles.
Each production project must be SDK-style and target `net10.0` (including
platform-qualified forms such as `net10.0-windows`). These checks are independent:
an SDK-style project on an older framework, or a legacy project claiming a newer
target, still receives the corresponding diagnostic. The requested migration
order is documented for delivery review; static final-source analysis proves the
resulting states, not historical execution order.

### All production VB and preserved core / EF6

* **LNG001** diagnoses each authored production VB type by compilation language and
  source-symbol location, including framework/time-service classes, not just the
  master-data frontend. Generated and test trees are excluded.
* **SCP001** requires source time-item, collection, authentication, observable
  login and selected presentation roles. Evaluated project edges must keep core
  roles reachable from the WPF executable. Deleting/disconnecting a project or
  emptying the corpus is not migration.
* **COR001** retains observable collection/property interfaces, exposed core
  command contracts, and no new concrete WPF dependencies in protected observable
  logic. It does not prove runtime notification/event ordering.
* **EF001** rejects evaluated `Microsoft.EntityFrameworkCore*` package/reference
  dependencies, loss/replacement of the EF6 source `DbContext`/`DbSet` contract or
  service/data-access dependency, and changes to the configured EF6 context,
  entity-set types or package version. The external manifest declares those
  existing store compatibility contracts. Trusted stage policy retains 6.5.1
  through S2a, and supplies the reviewed 6.5.2 boundary for S3/S4/final delivery.
  Generated EF models remain evidence,
  but are excluded from presentation naming/language/prose modernization.

This protects the EF6 compatibility boundary; it does not claim byte-for-byte
source equality or general behavioral equivalence without executing data access.
No EF Core migration is requested or rewarded.

### Main Data terminology

**NAM001** examines authored production application/presentation declarations and
references, resource keys, displayed `.resx` values and XAML display/class/name
metadata. CamelCase and separated `MasterData`, `master-data`, `master data`
tokens are recognized. `MainData`/`Main Data` and German `Stammdaten` pass.
Unrelated `master`, SQL's master catalog, historical comments/docs and generated
EF DTO/store compatibility symbols are excluded. Compatibility assembly identities
are explicit scenario boundaries; arbitrary file or comment text is not searched
as a symbol verdict.

### Static dark-calendar and main-data coverage

**THM001/THM002** perform **structural XML processing inside the DiagnosticAnalyzer**
over XAML AdditionalFiles, plus symbol-resolved WPF property operations. Roslyn
does not parse XAML or `.resx` as C#/VB; this XML step is explicit, not a claim of
Roslyn-only code semantics.

The resolver follows local resources, application resources, merged dictionaries,
unique component-resource paths, resource-to-color references, implicit/explicit
styles and `BasedOn`. It inspects main-data foreground/background pairs and both
`CalendarDayButton` and `CalendarButton` normal, selected, inactive, mouse-over and
disabled state coverage. Fixed light backgrounds, insufficient resolvable contrast
and absent shared normal resources receive THM001. Dark, contrasting local
highlight styles are permitted; comments containing colors are ignored.

The cycle-checked style chain supplies inherited setters, the effective (last
assigned) template, and inherited style triggers. Replacing a template does not
borrow states from the overridden base template. State coverage therefore works
for a healthy derived style while still rejecting genuinely missing states.
Calendar button-style properties can themselves come from the Calendar's
implicit/explicit style and its base chain, including dictionary resource
references and inline styles. An explicit missing resource does not fall back
to an unrelated implicit button style.
`DynamicResource` lookup uses the effective consumer scope, so a Calendar-local
override can replace an application dictionary's style or brush. `StaticResource`
retains declaration lookup. Shared styles are checked separately for each
consumer rather than reusing a result obtained with another control's resources.

THM002 means **unverified**, not success: missing/empty states, missing templates,
cyclic/unresolved resources, unknown named colors, opaque bindings, animation
targets that cannot be mapped, and opacity/alpha compositing are not silently
accepted. Brush opacity and color alpha survive resource indirection; zero,
fractional or unknown brush opacity is unverified rather than assumed opaque.
Only unit numeric opacity and opaque named/hex colors qualify for deterministic
sRGB luminance and contrast
(4.5:1, or 3:1 for disabled states; a background luminance above 0.45 is a fixed
light surface). These are bounded static checks, **not rendered-pixel, perceptual
accessibility, visual-state precedence or complete WPF layout proofs**. Complex
merged-resource activation or custom theme frameworks can remain unverified.

### Reusable compiler-assisted migration deliverable

**TOOL001** reports bounded static evidence gaps in an independently discovered,
compiling tooling project with
actual Roslyn VB syntax/semantic input handling and C# syntax construction or
rewriting connected through operation provenance to normalized deterministic
source emission. A prior guard in the same emission method must check diagnostics
from both the connected input and generated-output compilation receivers. The
output compilation's dependency on input syntax is not an input-diagnostics check.
Bounded recognized guards include error-severity `Any` predicates, error-filtered
`Where(...).Any()`, and single-definition local flags. Negation is accepted only
when the **error** branch exits. The guard must precede emission in its enclosing
block and unconditionally exit on that branch; conditionally executed guards,
false predicates, warning-only predicates, reversed rejection and unknown
conditions are not proof. Both C# and VB LINQ/reduced immutable-array operators
are covered. Unused imports,
dummy construction, constant unrelated output, absent output checks and obvious
clock/random output dependencies do not satisfy the rule.

Supply representative VB/C# fixture sources as evaluated `None` or `Content`
items in the tooling project (exclude expected-output `.cs` files from its own
`Compile` items). Corresponding input/output basenames pair fixtures; no particular
basename is mandatory. Both fixture sources must compile using supplied tooling
references and share at least three structural construct categories: properties,
events, generics, loops, exception handling or lambdas.

The analyzer does not execute the migration tool. This is evidence that a reusable,
compiler-assisted deliverable and companion fixtures **exist**, not proof the
agent used it, avoided manual translation, converted every construct, or produced
semantically equivalent outputs. Fixtures are not claimed to be generated-output
execution results. More elaborate multi-method pipelines can require equivalent
inspectable evidence; arbitrary control/dataflow is not a general program proof.

### Trusted CLI replay and final acceptance

TOOL001 is always displayed, but **does not award the TOOL score or decide
acceptance**. A converter-shaped empty-class emitter passes that static pattern
even beside rich independent fixtures; `ToolReplayTests` executes that exact
emitter and rejects its actual output. TOOL002 is the mandatory outside-analyzer
replay diagnostic for missing, failed or incomplete applicable replay.
No product symbol or converter vendor is special-cased: a genuine CLI wrapper
around a Roslyn conversion engine is eligible through the same executable
contract as a custom emitter. The assessor separately reviews compiler-aware
engine identity/licensing; successful fixture replay is not a general proof.

**Formal and local execution are different operations.** The default
`ASSESSMENT_REPLAY_EXECUTION=external-receipt` executes no CLI or emitted behavior
on the assessor host. It fails closed until a separately trusted isolated
executor supplies a valid signed receipt. There is no bundled sandbox and no
provisioned external executor in this repository. Missing isolation is a formal
candidate-evaluation blocker, not permission to relabel a local process isolated.

For development of **source-reviewed, assessor-owned reference tooling only**,
explicitly set `ASSESSMENT_REPLAY_EXECUTION=local-reviewed`. Local case checks
can pass and `LocalEvidencePassed` can be true, but `Verified` remains false,
`ExecutionBoundary=local-reviewed-unisolated`, and TOOL002 remains mandatory.
Never use this mode for an untrusted submission. An owned fixture-copying stub
regression demonstrates why: it can reproduce accessible expected files perfectly
without performing conversion, yet cannot earn formal verification.

For independently reviewed **owned reference tools**, the separately labelled
`reference-reviewed` lane can satisfy reference TOOL acceptance after exact-source
approval, a fresh producer build and actual replay. It never sets formal
`Verified=true`. See `..\Reference-Replay.md` for the runnable commands, approval
contract, producer/binary provenance checks and limitations. Merely choosing this
mode or providing a supplied binary does not establish approval or a pass.

Set `ASSESSMENT_REPLAY_PLAN` to an absolute JSON path **inside the private
assessment tree**, not the candidate checkout. Plans, fixture inputs, expected
checkpoint outputs and behavior programs are trusted assessor artifacts; never
distribute them or preparation tooling. `Projects` identifies independently
built CLI/engine projects for source discovery, including wrappers with no direct
VB reference. Do not list application projects as tooling.

The interface is deliberately generic; arguments are passed directly, not through
a shell. Every command must take explicit `{input}` and `{output}` directories:

```json
{
  "Projects": ["C:\\submission\\utility\\Utility.csproj"],
  "SourceRoots": ["C:\\submission\\utility"],
  "ArtifactRoots": ["C:\\submission\\utility\\bin\\Release\\net10.0"],
  "Cases": [{
    "Name": "held-out-language-canary",
    "Kind": "language",
    "Command": {
      "Executable": "dotnet",
      "Arguments": ["C:\\submission\\utility\\Utility.dll", "convert",
                    "--source", "{input}", "--destination", "{output}"]
    },
    "InputDirectory": "C:\\private\\assessment\\fixtures\\canary-input",
    "ExpectedDirectory": "C:\\private\\assessment\\fixtures\\canary-expected",
    "BehaviorFile": "C:\\private\\assessment\\fixtures\\canary-behavior.cs",
    "Unsupported": false,
    "Idempotent": false,
    "Checkpoint": false
  }]
}
```

This example is intentionally **incomplete**, not an acceptance fixture. For
each applicable kind supply an independent positive fixture, an unsupported
fixture, and a `Checkpoint=true` baseline-to-checkpoint reconciliation case.
S0/S1/S4 require `language` and `project`; S2/S2a require only `project`; S3 does
not require migration replay. Language positives require a trusted C# behavior
program with an integer-returning Main; project positives require
`Idempotent=true`. Include behavior fixtures for project compatibility changes.
Construct held-out language cases covering collection identity/order/events,
generic properties, ByRef/default/indexed members, numeric semantics and WPF
event wiring before accepting a real converter. Review full source graph and
clean project builds separately; a small behavioral fixture does not certify WPF
or SQL behavior.

Each case copies fresh private inputs, invokes the actual CLI with a timeout,
rejects source mutation, compares its complete emitted file set/content against
the trusted expected tree (C# whitespace normalized), and compares byte hashes
on a repeated independent invocation. Project idempotence replays emitted output
as input. Unsupported cases require nonzero exit, a diagnostic and no partial
output. Behavior compiles actual emitted C# with the trusted program and runs a
child process with a timeout. The process is **not a security sandbox**: inspect
submissions and run under an isolated low-privilege account without production
credentials/network access.

Those process steps describe explicit reviewed-local execution and the required
external executor algorithm, not the default local verifier. Reviewed-local
execution freezes expected-output bytes/hashes and behavior source **before**
starting the CLI, compares output against the frozen snapshot, and detects
persistent expectation mutation. Child environments use a small runtime allowlist;
assessor paths/configuration and credentials are not inherited. **Neither measure
provides isolation**: same-identity code can still read fixture files, discover
paths without environment hints, modify and restore files, attack its parent,
or tamper with mutable reports. Hashes and environment filtering alone must never
upgrade local evidence into formal acceptance.

### Minimal external executor contract (not a sandbox implementation)

Use an already organization-provisioned disposable Windows VM/runner suitable
for WPF/Framework builds; no Windows users or broad software installation on a
shared developer host is required by this verifier. A separately trusted
supervisor must enforce the following boundary before its key may be pinned:

1. The trusted supervisor holds the plan, expected trees, immutable snapshots,
   comparison logic, receipt-signing key and request challenge. None is readable
   or writable by submission processes. The signing key must never enter the VM
   or candidate job. An administrator of the candidate environment cannot also
   control the supervisor or signing policy.
2. Candidate custom MSBuild targets, tool source builds, CLI commands, emitted
   code and behavior run only in disposable isolated jobs, with no supervisor
   mounts, production credentials, host filesystem access or unrestricted
   network. A candidate job receives only its input/tool artifacts and explicit
   output area, not expected outputs or the private plan. A same-user directory,
   environment flag, container label or candidate claim is not sufficient.
3. Terminate submission processes before exporting outputs. The supervisor
   rejects reparse/path traversal outputs and compares complete immutable output
   snapshots outside the job. Use fresh jobs/inputs for deterministic reruns and
   project idempotence; compile and run behavioral canaries in isolated jobs too.
   The supervisor independently checks source/tool build provenance, input
   immutability, unsupported exits/no partial output, output compilation and every
   required case. It must never sign a candidate-written `Passed` value.
4. The supervisor signs a short-lived receipt only after those checks. The
   assessor pins the executor's RSA public key through trusted out-of-band
   configuration, **never from the submission**. Signature verification establishes
   trust in that executor's attestation, not remote hardware attestation or a
   proof that an arbitrary service actually enforces these controls.

The repository scan itself invokes MSBuild and therefore is **not safe for
unreviewed project files on the assessor host**. Run the full scan/build process
inside the external execution boundary for adversarial submissions. A signed
replay receipt does not retrospectively isolate a local repository scan or certify
runtime/SQL/visual acceptance. Current local baseline/reference scans are
reviewed-source development evidence only.
Merely copying the assessor and fixtures into a VM and running everything as the
same identity is still insufficient. The external integration must keep trusted
evaluation/comparison in a protected supervisor compartment and route candidate
MSBuild/compiler-input extraction and executable work into separate restricted
jobs. `CompilerInputLoader` does not itself implement that process separation.

Configuration and wire format:

```powershell
$env:ASSESSMENT_REPLAY_EXECUTION = 'external-receipt' # default
$env:ASSESSMENT_REPLAY_CHALLENGE = [guid]::NewGuid().ToString('N')
$env:ASSESSMENT_REPLAY_PUBLIC_KEY = 'C:\private\assessment\executor-public.pem'
$env:ASSESSMENT_REPLAY_RECEIPT = 'C:\private\assessment\executor-receipt.json'
$p = '.\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj'
dotnet test $p --filter 'Category=ReplayRequest'
```

Set `ASSESSMENT_REPLAY_PLAN` and the trusted stage/mode/configuration as above.
This explicit selector reads/hashes files but does not evaluate candidate
MSBuild projects, launch their binaries or issue an acceptance verdict.
With a private plan and challenge, it writes
`Artifacts\Reports\<profile>\external-replay-request.json`. Its `PayloadBase64`
decodes to a `ReplayRequest`; `Sha256` is the uppercase SHA256 of those exact
decoded bytes, avoiding cross-platform JSON canonicalization ambiguity.
The request binds policy/profile/configuration, challenge, the complete declared
plan, input/expected-tree/behavior hashes, project-file hashes, complete reviewed
source-root and binary/dependency-root snapshots, and explicit
executable/DLL/script artifact hashes. `SourceRoots` and `ArtifactRoots` are
mandatory for formal receipts. Source snapshots omit `bin`, `obj`, `Artifacts`
and `.git`; binary snapshots include the complete declared artifact trees.
Include every linked/imported source and dependency root in the trusted plan;
the supervisor must reject builds that consume unbound external inputs.
The trusted supervisor must independently
verify transferred artifacts against these bindings and review/build the full
source/dependency graph; hashing a project file alone is not source-to-binary
provenance. Absolute artifact names are logical request identities; only the
trusted supervisor may map them into job-local paths.

The receipt JSON is `SignedReplayReceipt`: `PayloadBase64` plus
`SignatureBase64`. Sign the decoded payload using RSA-PSS/SHA256 with a minimum
3072-bit RSA key. The payload is `ReplayAttestation`:

```text
Policy: "taskotime-isolated-replay-v1"
Challenge: the assessor-issued, fresh GUID-N
RequestHash: request Sha256
Executor: nonempty trusted executor identity
ExpiresAt: UTC timestamp after verification time, no more than one hour ahead
Cases: [{ Evidence: ReplayEvidence, Checks: [ ... ] }, ...]
```

Use the record definitions in `ExternalReplay.cs` and `ToolReplay.cs` as the wire
schema. Case names must match exactly once, all cases must pass, and the signed
checks must include `isolated-process`, `tool-source-build`, `input-immutable`;
positive cases additionally require `frozen-expectation-comparison`,
`deterministic-rerun`, `output-compilation`, plus `behavior` and
`idempotent-rerun` when applicable. Unsupported cases require
`unsupported-diagnostic` and `no-partial-output`, a nonzero exit and diagnostics.
The verifier rechecks current immutable request bindings and expiry. Issue a
fresh challenge per evaluation and retain its signed receipt in private custody.
Unsigned `sandbox=true`/`isolated=true`, wrong keys, stale challenges, mutated
inputs/expectations/tools and incomplete checks all fail closed.

No production signing key, signed acceptance fixture, live runner, account
creation or untrusted submission execution is included. Tests sign contract-only
receipts with ephemeral in-memory test keys and never execute their dummy tool
artifacts. Provisioning and independently validating the executor remains an
explicit integration prerequisite for formal TOOL002 acceptance.

Record private baseline/checkpoint commit identities when preparing reconciliation
trees from reviewed refs. Replay records actual input/output SHA256, command,
hashes of explicitly supplied executable/DLL/script artifacts,
exit code and stdout/stderr; plan ownership and tree provenance need assessor
review. This checks reproducibility against that checkpoint, not historical
execution order or actual past tool usage. Independently build the declared source
projects and record engine/dependency versions before replay; a matching CLI
artifact hash alone is not source-to-binary provenance. Reconcile generated/manual diffs and
ordered commit/input/output identities separately; do not infer counterfactual
token savings from code or logs. `Verified=false`/TOOL002 must remain until real
submission replay plans and held-out fixtures have been run.

JSON `Diagnostics` retains every static outcome and mandatory replay/stage
failure. `AcceptanceDiagnostics` differs only by excluding static TOOL001.
All business, architecture, localization, language, comment, naming, theme,
scope, core and EF6 rules stay mandatory in final mode, including regressions in
pre-satisfied work. The hard gate requires zero mandatory defects and valid
compilations; S4 also requires full weighted score 1.0. Separate application
unit/SQL/STA and explicit visual checks are required before calling Golden ideal.
