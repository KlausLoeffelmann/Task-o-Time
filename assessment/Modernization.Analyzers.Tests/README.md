# Independent compiler-assisted modernization assessment

This **xUnit** project is independent of the legacy MSTest
`ArchitectureFitness.Tests` project. Candidate verdicts are actual Roslyn
`DiagnosticAnalyzer` diagnostics. The evaluator does not invoke the application,
inspect its objects with reflection, query/mutate a database, use git history, or
ask an AI to grade it. Compilation and input-extraction failures fail closed.

## Portable bundle and execution

Copy exactly the relative files in `..\Bundle.files` into a repository-root
`assessment\` directory. Do not copy `ArchitectureFitness.Tests`, `bin`, `obj`,
reports/TRX, compiler intermediates, or archived scenario documents. The bundle
contains both external MSBuild configuration files, the scope manifest, the shared
configuration source, compiler-input target, all analyzer/test sources, and this
README. `Candidate-Prompt.md` and `Assessor-Guide.md` are included as top-level
assessment documents, copied unchanged from the parent's external originals.
Refresh those copies from the originals if the parent revises them before
packaging. The assessment is not an application project reference.

Use Windows and the .NET 10 SDK/runtime (a compatible newer SDK can build it).
The application uses .NET Framework 4.6.1 reference assemblies restored from its
existing package dependencies. From a fresh golden-branch checkout:

```powershell
$source = Join-Path $PWD 'src\TaskOTime'
# Compile/restore the solution, but do not run the application or database tests.
dotnet build .\src\TaskOTime\TaskOTime.slnx --verbosity quiet
dotnet test .\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj `
  "-p:TaskOTimeSourceRoot=$source" --filter 'Category=AnalyzerUnit|Category=RepositoryScan'
# Candidate acceptance: expected to FAIL on the deliberately bad starting app.
dotnet test .\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj `
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
Test projects, hidden/build/package folders, and the assessment itself are
excluded. Evaluated test-framework references and `IsTestProject` also exclude
tests from production verdicts. Renamed projects are discovered by project
extension, not a list of original VB project filenames.

| xUnit trait | Meaning |
| --- | --- |
| `Category=AnalyzerUnit` | Bilingual positive/negative, alias, renamed, generated-source, XML, empty-input and compiler-failure fixtures; should pass |
| `Category=RepositoryScan` | Complete source compilation plus report generation; passes on a compilable bad application |
| `Category=Modernization` | Requires valid inputs **and zero analyzer diagnostics**; intentionally fails on the bad baseline |

An unfiltered test run intentionally fails on that baseline. No acceptance rule is
implemented as a runtime UI test or an assertion about baseline diagnostic counts.

## Input boundary and reproducibility

MSBuild supplies evaluated compiler arguments, language options, references,
project edges, resource manifest names and items. Source project dependencies are
recompiled and emitted to **in-memory** metadata; existing frontend DLL contents
do not determine semantic verdicts. Restored assets/imports and reference outputs
are prerequisite build inputs, not execution evidence. Extraction disables compiler
execution and project-reference builds. WPF/design-time intermediates are written
under `Artifacts\compiler-inputs`, outside the application; normal application
`obj` assets/imports are read, not replaced. The only intentional parse-option
adjustment is `DocumentationMode.Parse`: XML documentation must remain Roslyn
syntax trivia even when the candidate does not request a `/doc` output.

SDK implementation and reference-assembly paths are both mapped to source-project
identity before replacement. The mapping uses evaluated `TargetRefPath`,
`ReferenceAssembly`, `ReferencePathWithRefAssemblies` and source-project metadata,
not DLL basenames. Missing or ambiguous project identities fail closed. Metadata
regressions compile an equivalent .NET 10 executable/library pair using the actual
source-image replacement, without reading a stale reference DLL.

The report is `Artifacts\Reports\roslyn-diagnostics.json` (or the configured
artifact root), written before test assertions. It contains compilation validity,
sorted project/source/AdditionalFile paths, selected architecture types, all
diagnostics and per-criterion integer metrics. There are no timestamps or elapsed
times. Diagnostics are sorted by ID, path, line, column and message.
`OutcomeAnalyzer` runs a compilation action over the supplied compilation corpus;
cross-project locations are Roslyn external-file diagnostic locations, retaining
their source spans. XML is supplied through Roslyn `AdditionalText` inputs.
Infrastructure failures use `LOAD001`; compiler diagnostics and analyzer input
gate `ASM001` prevent success. Analyzer exceptions also prevent success.

`Examined` / `Satisfied` are bounded static evidence units, **not percentages of
correctness**: comment trivia without lexicon evidence; documented declarations;
authored types by language; resource/accessor evidence; resolved theme pairs;
required source roles; tool pipelines/fixtures; EF context contracts. `Unverified`
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

* **ENG001** inspects Roslyn C#/VB comment and XML-documentation **syntax trivia**,
  not regex matches over string literals or entire source files. A deterministic
  Dutch/German lexicon requires two distinct distinctive words, or a known
  source-language n-gram. Sentence fragments, mixed English/source-language prose,
  umlauts and common source-language phrasing have bilingual fixtures.
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

### Classic localization in a WPF application

* **LOC001** requires a classic `.resx` root/header/data schema, nonempty textual
  entries, a neutral resource and the language cultures required by the external
  `ScenarioScope.xml` localization policy, all with exact key parity. The supplied
  scenario requires **neutral English plus both German (`de`) and Dutch (`nl`)**.
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
* Actual `ResourceManager` construction, same-assembly input, symbol-resolved
  `GetString`, constant key/base-name alignment with evaluated manifest metadata,
  and a strongly typed static string accessor are examined, **including generated
  designer source**. Generated-source metadata/conventions or the standard
  `GeneratedCodeAttribute` identify the generated accessor role; this does not
  prove who generated it. Resource/toolkit/class/file names are not prescribed.
* The accessor must flow into a known WPF text property, `MessageBox` string
  argument, observable UI output, or structurally resolved XAML `x:Static`
  property use. The validated lookup must also contribute to the getter's
  **returned value**; discarded calls and unrelated local initializers do not
  qualify. Returned local aliases and resource-valued branches are supported.
  Unused resource construction/imports/accessors do not suffice.
* **LOC002** diagnoses literal display text at those semantic sinks and WPF XAML
  display attributes/property elements. Observable outputs are recognized through
  bound property names or standard display-output contracts. Producer tracing
  follows source returns, locals, fields and setter-helper arguments, but excludes
  conditional predicates and external lookup/provider metadata. Logs, identifiers,
  format-only strings, `nameof`, environment keys, resource keys and culture names
  are not independently treated as displayed prose. There is no class-wide
  generated-accessor exemption: a UI-consumed hardcoded getter return or literal
  fallback is diagnosed, including consumption through XAML `x:Static`.

This is **WinForms/designer-style resource localization, not a requirement to use
WinForms UI in WPF**. Merely moving strings into a XAML dictionary is insufficient.
The checker establishes static UI consumption, not that a particular window is
ever opened, a culture is selected at runtime, every possible dynamic string is
translated, or a translation is good. Reflection, arbitrary custom markup
extensions, source generators and unmodeled external string producers are not
proved. Source-generator-dependent inputs that cannot compile fail closed.

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
  existing store compatibility contracts. Generated EF models remain evidence,
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

**TOOL001** requires an independently discovered, compiling tooling project with
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
