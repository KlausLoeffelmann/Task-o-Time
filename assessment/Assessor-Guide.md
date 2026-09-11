# Assessor guide: Task-o-Time modernization

This document is assessment material. Distribute only `Candidate-Prompt.md`
with a checkout of the application branch; do not give the candidate this
guide, evaluator sources, scope manifest, or diagnostic reports.

## Branch model

- `junior-dev-mvvm-imp` is the candidate application and its handover history.
  It retains ordinary code and the two character-comment commits, but no
  reachable evaluator introduction/removal commits.
- `TheGoldenBranch` preserves the same unmodernized application plus a
  self-contained `assessment` directory. "Golden" means evaluator custody,
  not a corrected reference application.
- `main` and its history are not rewritten.
- The actual Git author remains the configured contributor. The
  `Klaus obo ...` titles describe fictional documentation voices; no fabricated
  email identities are used.

Keep the complete Git repository and the golden ref private during blind
assessment. A separate branch is not an access-control boundary: an agent that
can fetch `TheGoldenBranch` can inspect its oracle. Use a candidate-only export
or a restricted repository for actual trials. Rebasing removes reachability
from a branch, not objects from other branches, remote caches, or local reflogs.

## Scope and expected changes

| Area | Expected outcome |
| --- | --- |
| Business correctness | Repair project attribution and elapsed-duration truncation without disabling their UI flows. |
| Working core | Preserve login, time collection, normal recording, breaks, downtime, errands, checkout, and task completion. |
| Architecture | Remove concrete-view/control coupling from presentation logic; retain proper observability and commands. |
| Language | Migrate all production VB, including time-tracking libraries, to C#. Test language is not a gate. |
| Comments | English prose and accurate XML documentation, without deleting essential explanations. |
| Localization | Traditional .resx/ResourceManager, strongly typed accessors, actual presentation consumption, and culture resources with matching keys. |
| Theme | Calendar state coverage and Main Data foreground/background coherence through reusable theme-aware resources. |
| Naming | Main Data in application/presentation naming; persisted schema compatibility remains stable. |
| Reuse | A runnable compiler-aware conversion utility with representative conversion fixtures and explicit unsupported-input reporting. |
| Persistence | Keep EF6 and SQL Server. EF Core is prohibited for this exercise. |

The original source boundary protects the recording core from the intentionally
bad master-data architecture checks. That protection does **not** exempt it
from comment translation, production-language migration, or business-correctness
checks. Maintenance views and pseudo-ViewModels are now in the same assembly
as the core; project names alone cannot define assessment scope.

## Evidence and limitations

All new xUnit assessment verdicts are emitted by Roslyn `DiagnosticAnalyzer`
implementations. Source syntax, trivia, symbols, operations, compiler inputs,
and additional-file diagnostics form the evidence. Structural XAML and `.resx`
inspection belongs inside those analyzers as `AdditionalFiles`, not a separate
subjective scoring service. Neither AI nor application execution decides these
verdicts.

Static evidence has deliberate limits:

- Comment language markers can identify remaining Dutch/German prose and
  missing documentation, but cannot prove the semantic quality of an English
  translation. Do not describe lexical evidence as natural-language
  understanding.
- Resource declarations alone do not establish localization; require real
  accessor/ResourceManager consumption. Structural parity cannot certify a
  human-quality translation.
- Theme resource/state and resolvable color checks cannot prove every rendered
  pixel or custom control is accessible. Retain separately reported visual
  verification for production acceptance.
- Compiler APIs in a utility are evidence of a reusable migration asset, not
  proof that an agent used the asset or avoided all manual edits. Do not infer
  token expenditure or historical work strategy from the final source.
- A successful scan requires valid source compilations and a nonempty,
  traceable scope. Renaming or deleting the assessed types must not silently
  produce a perfect score.

The existing MSTest fitness project remains supplementary. It has runtime and
structural checks and is not presented as the Roslyn-only xUnit oracle.
Analyzer self-tests should pass on the baseline; modernization gates should
fail with explicit diagnostics until the candidate delivers the changes.

## Verified baseline and execution

The candidate application tip before adding the oracle is
`137c2c35692f8a8ee87c6dba3770aad47c64e5d8`. The two character-comment commits
are `e670362` (Dutch) and `137c2c3` (junior). Their changes are comment-only.

The portable assessment passed **227 analyzer fixture cases and one complete
repository scan**. Its separate modernization gate failed as expected on valid
compilations, and repeated scans produced identical JSON.

| Diagnostic family | Baseline findings |
| --- | --- |
| Project attribution / elapsed duration (`BUS001`, `BUS002`) | 1 / 1 |
| Comment-language evidence / documentation coverage (`ENG001`, `ENG002`) | 42 / 3 |
| Production VB (`LNG001`) | 42 |
| Resource infrastructure / literal UI strings (`LOC001`, `LOC002`) | 3 / 271 |
| Main Data terminology (`NAM001`) | 49 |
| Theme defects / unverified theme coverage (`THM001`, `THM002`) | 3 / 46 |
| Missing reusable migration pipeline (`TOOL001`) | 1 |

Architecture diagnostics remain in the `MOD` family. Input validity, source
scope, preserved core, and EF6 compatibility checks have no baseline failures.
Counts are diagnostic occurrences, not independent business defects or a
weighted score. In particular, a criterion that already passes must still pass
after modernization.

From the golden repository root:

```powershell
dotnet build .\src\TaskOTime\TaskOTime.slnx
$project = '.\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj'
dotnet test $project --filter 'Category=AnalyzerUnit|Category=RepositoryScan'
dotnet test $project --filter 'Category=Modernization'
```

The last command intentionally returns a failing exit code on the baseline.
For a candidate-only checkout, run the same project from an external assessor
directory with `TASKOTIME_SOURCE_ROOT` set to the candidate's `src\TaskOTime`.
Do not add the oracle to the candidate application solution merely to run it.

## Traditional localization references

The requested infrastructure is the long-standing .NET resource pipeline,
not a new WPF-only localization library:

- [Creating .NET resource files](https://learn.microsoft.com/en-us/dotnet/core/extensions/create-resource-files)
- [StronglyTypedResourceBuilder](https://learn.microsoft.com/en-us/dotnet/api/system.resources.tools.stronglytypedresourcebuilder)

The latter remains exposed by the Windows Forms designer assemblies. A WPF
application can consume conventional strongly typed resources without
acquiring a WinForms UI dependency.
