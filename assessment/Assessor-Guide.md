# Assessor guide: Task-o-Time modernization

This document is assessment material. Distribute only `Candidate-Prompt.md`
with a checkout of the application branch; do not give the candidate this
guide, evaluator sources, scope manifest, or diagnostic reports.

For candidate exports, select the standalone brief rather than the shared
stage-table template:

| Export stage | Standalone candidate brief |
| --- | --- |
| `original` / S0 | `assessment\Candidate-Prompt.S0.md` |
| `vb-net472` / S1 | `assessment\Candidate-Prompt.S1.md` |
| `csharp-net472` / S2 | `assessment\Candidate-Prompt.S2.md` |
| SDK net472 checkpoint / S2a | `assessment\Candidate-Prompt.S2a.md` |
| `csharp-net10` / S3 | `assessment\Candidate-Prompt.S3.md` |

Regenerate these versioned briefs with `assessment\Write-CandidatePrompts.ps1`
after editing the shared `Candidate-Prompt.md`. The S1, S2, and S3 candidate
branches carry only their selected brief as root `Candidate-Prompt.md`; keep it
in sync with the matching versioned brief. S0 receives its selected brief when
exported. Never export the template, generator, other stage briefs, evaluator,
or preparation tools. S2/S3 prohibit production VB, not VB test projects.

## Branch model

- `junior-dev-mvvm-imp` is the candidate application and its handover history.
  It retains ordinary code and the two character-comment commits, but no
  reachable evaluator introduction/removal commits.
- S0 is the original mixed-Framework production-VB snapshot; S1 normalizes
  all application/test projects to net472. S2 removes production VB while
  preserving nonmigration defects. S2a is the SDK-style net472 checkpoint.
  S3 is SDK-style C# .NET 10, still intentionally nonmigration-defective.
- S4 / `TheGoldenBranch` contains the fully corrected reference. The
  historical evaluator-custody Golden at `1f1b552` is not evidence of correctness.
  That revision is preserved as `assessment-baseline`.
- `main` and its history are not rewritten.
- The actual Git author remains the configured contributor. The
  `Klaus obo ...` titles describe fictional documentation voices; no fabricated
  email identities are used.

Publish all relevant versions, including Golden and evaluator history, to this
harness-template repository. The harness explicitly needs the complete source
of templates. That repository and its everything/reference branch must never
be a model's working checkout.

For each trial, create a repository and working branch completely from scratch
inside a headless Windows Docker container, using only the selected candidate
snapshot and its standalone prompt. Do not retain other refs, Git objects, or a
remote exposing the template repository. Identical frozen snapshots and prompts
are the basis for objective comparisons. A branch switch in a complete clone
does not provide this separation.

## Preparation versus later evaluation

Stage creation and Golden preparation do **not** run the assessment tests or
wait for isolated replay, signing, or process-profile experiments. Previously
collected development evidence remains historical evidence; it does not imply
that a formal assessment has awarded a score.

Actual trials happen later in a **headless Windows Docker container**, with a completely
new repository containing only the selected start/origin branch. Initialize it
from the clean stage export; do not copy this template repository's Git objects,
other branches, Golden implementation, preparation tools, or evaluator into the
candidate-visible repository. Provide private grading material separately when
evaluation takes place.

The acceptance commands and scoring requirements below describe that later
evaluation, not prerequisites for creating or advancing `TheGoldenBranch`.
Experimental Windows Sandbox and owned-reference replay utilities are retained
as private diagnostics, not the required infrastructure for the planned trials.
The S4 preparation record in `tools\StageBaselines.json` explicitly leaves formal
assessment deferred and its score unset.

## Scope and expected changes

| Area | Expected outcome |
| --- | --- |
| Business correctness | Repair project attribution and elapsed-duration truncation without disabling their UI flows. |
| Working core | Preserve login, time collection, normal recording, breaks, downtime, errands, checkout, and task completion. |
| Architecture | Remove concrete-view/control coupling from presentation logic; retain proper observability and commands. |
| Language | Migrate all production VB, including time-tracking libraries, to C#. Test language is not a gate. |
| Comments | English prose and accurate XML documentation, without deleting essential explanations. |
| Project migration | Every production project is SDK-style first, then targets .NET 10; order and final evaluated project state both matter. |
| Localization | `Microsoft.Extensions.Localization` dependency and meaningful lookup/application; neutral English plus German, Dutch, and Spanish `.resx` versions with matching keys and non-duplicated translated content. |
| Localization surfaces | Applied localized keys on exactly Login Experience, Main time-collection UI, add/edit booking dialog, and Project Main Data dialog; runtime Options language selection changes culture/localization behavior. |
| Theme | Calendar state coverage and Main Data foreground/background coherence through reusable theme-aware resources. |
| Naming | Main Data in application/presentation naming; persisted schema compatibility remains stable. |
| Reuse | Independent actual CLI replay of applicable remaining migration operations: language/project for S0/S1, project/framework for S2/S2a, none for S3; Golden supplies the complete reference pipeline. |
| Persistence | Keep EF6 and SQL Server. EF Core remains explicitly out of scope and is prohibited for this exercise. |

The original source boundary protects the recording core from the intentionally
bad master-data architecture checks. That protection does **not** exempt it
from comment translation, production-language migration, or business-correctness
checks. Maintenance views and pseudo-ViewModels are now in the same assembly
as the core; project names alone cannot define assessment scope.

## Evidence and limitations

Static quality diagnostics are emitted by Roslyn `DiagnosticAnalyzer`
implementations. Structural XAML and `.resx` inspection uses AdditionalFiles.
Stage contracts and trusted actual CLI replay run outside analyzer callbacks.
Replay compiles and executes independently supplied behavioral canaries in a
child process; it does not execute the application or access SQL.
Runtime, SQL and visual acceptance remain separately required evidence, not
claims inferred from the static oracle.

Static evidence has deliberate limits:

- Comment language markers can identify remaining Dutch/German prose and
  missing documentation, but cannot prove the semantic quality of an English
  translation. Do not describe lexical evidence as natural-language
  understanding.
- Resource declarations alone do not establish localization; require real
  `Microsoft.Extensions.Localization` lookup and presentation consumption on
  all four named surfaces. Structural parity cannot certify a
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
structural checks and is not presented as a replacement for the private oracle.
Analyzer self-tests should pass on the baseline; modernization gates should
fail with explicit diagnostics until the candidate delivers the changes.

## Verified baseline and execution

The candidate application tip before adding the oracle is
`137c2c35692f8a8ee87c6dba3770aad47c64e5d8`. The two character-comment commits
are `e670362` (Dutch) and `137c2c3` (junior). Their changes are comment-only.

Historical, pre-stage-profile evidence: the portable assessment passed **233 analyzer fixture cases and one complete
repository scan**. Its separate modernization gate failed as expected on valid
compilations, and repeated scans produced identical JSON.

| Diagnostic family | Baseline findings |
| --- | --- |
| Project attribution / elapsed duration (`BUS001`, `BUS002`) | 1 / 1 |
| Comment-language evidence / documentation coverage (`ENG001`, `ENG002`) | 58 / 3 |
| Production VB (`LNG001`) | 42 |
| Resource infrastructure / literal UI strings (`LOC001`, `LOC002`) | 10 / 279 |
| SDK-style / .NET 10 projects (`PRJ001`, `PRJ002`) | 3 / 7 |
| Main Data terminology (`NAM001`) | 49 |
| Theme defects / unverified theme coverage (`THM001`, `THM002`) | 3 / 46 |
| Missing reusable migration pipeline (`TOOL001`) | 1 |

Architecture diagnostics remain in the `MOD` family. Input validity, source
scope, preserved core, and EF6 compatibility checks have no baseline failures.
Counts are diagnostic occurrences, not independent business defects. The CSV
assessment used bounded criterion scores and rubric version `2026-09-v2`:
business 28, MVVM 18, localization 14, VB-to-C# 9, theme 9, comments 5,
Main Data naming 5, migration tool 5, SDK-style 3.5, and .NET 10 target 3.5.
The weights total 100 and the OVERALL score is normalized to 0..1. Evaluation
validity, hard-gate status, and unverified evidence remain separate from score.
The CSV must be produced even when the baseline modernization gate fails.

## Trusted stage selection and acceptance

Run from the **private assessment directory** so `global.json` selects the
supported .NET 10 SDK; Roslyn 5.0 handles its C#/VB compiler inputs. Extraction
uses that exact SDK even when the candidate has a different `global.json`.
Build/restore candidate projects before scanning. Both Debug and Release are
applicable configurations: run them separately; the report records evaluated
configuration, available configurations, framework identifier/version/platform,
language and SDK style, including test projects. Test language is unrestricted.

```powershell
Set-Location .\assessment
$project = '.\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj'
$env:ASSESSMENT_STAGE = 'S0' # S0, S1, S2, S2a, S3, or S4
$env:ASSESSMENT_CONFIGURATION = 'Debug' # repeat for Release
$env:ASSESSMENT_MODE = 'starting-point-integrity'
dotnet test $project --filter 'Category=AnalyzerUnit|Category=ReplayUnit|Category=RepositoryScan|Category=StagePreservation'
$env:ASSESSMENT_MODE = 'final-delivery'
dotnet test $project --filter 'Category=Modernization'
```

The final command intentionally fails on a baseline. **Never include
StagePreservation in Golden/final acceptance**: exactly-one BUS001/BUS002
assertions live only in stage-integrity policy, not generic RepositoryScan.
The generic scan permits zero business findings. All nonmigration quality
rules, protected source/core contracts, renaming detection and no-EF-Core rules
remain active. EF6 6.5.1 is preserved through S2a; S3/final requires 6.5.2.

Selectors are assessor process configuration, not candidate-controlled rubric
files. Run in an isolated assessor checkout with the candidate source root
read-only where practical. Neither a stage selector nor an integrity pass
waives final-delivery quality rules. StageIntegrityPassed describes the selected
starting snapshot; it will normally be false on its subsequently corrected
final submission. HardGatePassed is final-delivery only.

Reports live under `Artifacts\Reports\<stage>-<mode>-<configuration>`.
Compiler intermediates additionally hash full project path/configuration/profile.
CSV/JSON rubric `2026-09-stage-v4` preserves the 100-point full-outcome score,
reports applicability and an applicable-only normalized remaining-work score.
Deferred work is not a pass; pre-satisfied work is not newly earned. A
pre-satisfied regression still fails final acceptance. S3's tool criterion is
not applicable and contributes no earned reuse credit (its universal full-outcome
score can be 0.95 while all applicable work passes). S4 has no exemptions.
Do not compare remaining-work scores as equal task difficulty across stages.
Invalid compilation/empty scope scores zero with INVALID rows and no remaining
score; no applicable denominator yields null, not 1.0.

All diagnostics remain in JSON. TOOL001 describes **static evidence only** and
cannot establish conversion correctness. TOOL002 reports missing/failed trusted
replay for applicable work. AcceptanceDiagnostics retains all mandatory quality,
stage-target and replay failures; only TOOL001 is separated from acceptance
because a genuine wrapper need not match a custom emitter's source shape.
Later formal acceptance requires valid inputs and **zero AcceptanceDiagnostics**;
S4 additionally needs full score 1.0 and separate runtime/SQL/visual evidence.
A rich companion fixture beside a converter that emits an empty class is not
conversion acceptance; the replay negative regression exercises that exact case.

See the analyzer README's replay contract for private plans and fixtures.
Its optional trusted `EvidenceFile` declaration separates one exact execution
JSON artifact from source equality, validates its success status on every run,
and retains raw artifact hashes. All other output remains independently checked;
manifest file lists cannot select scope or hide defective source. No declaration
means no exclusion, and input/source/binary integrity hashes are unchanged.
Formal replay now defaults to a **signed receipt from a separately trusted
isolated executor**; this bundle does not supply or provision that executor.
Explicit `ASSESSMENT_REPLAY_EXECUTION=local-reviewed` is only for source-reviewed
owned tooling. It can produce passing local case evidence, but never sets
`Verified=true` or satisfies TOOL002. Expectation snapshots and filtered child
environments detect some mistakes, not same-identity adversarial access.
Do not run candidate MSBuild projects, CLI binaries or emitted code locally:
the full untrusted build/scan must also run inside the external boundary.
The README specifies request hashing, pinned RSA-PSS receipt verification and
executor obligations. Missing isolation remains a formal acceptance blocker.
`Sandbox\README.md` now documents a runnable Windows Sandbox producer/CLI
execution primitive, exercised with actual SDK and net472 WPF builds. Producer
signing is disabled: submitted MSBuild can replace both compared output files,
so byte equality cannot establish source-to-binary provenance. Historical
producer-only signatures must not be trusted. Full host replay-policy signing and
compiler-input routing remain separate integration requirements; no completed
formal grading is implied.
For the source-reviewed owned Golden reference, `Reference-Replay.md` documents
a separate runnable `reference-reviewed` path: exact-source independent approval,
fresh evaluated builds and actual local replay provide diagnostics only.
Both `ReferenceVerified` and `Verified` stay false; tracked payloads can replace
both outputs after compilation without changing source. No reference TOOL002 or
formal Golden grade is granted; no other quality rule is waived. This does not
prevent preparation of the Golden branch.
Preparation tooling, expected outputs, logs and evaluator files are never
candidate handover material. Export only application allowlisted files and the
stage-appropriate candidate brief, without Git history or private support refs.

### Stage-profile implementation verification

Owned-reference follow-up: **305 combined tests pass**, including a real fresh
managed producer build, compiler-output/target byte binding, rejection of
post-build binary substitution, stale supplied binaries and unapproved
fixture-copying source. Both metadata-only request export commands also passed
against deliberately invalid project XML/non-executable artifacts without
evaluating them. Rubric v4 records the explicit reference/formal acceptance basis;
weights and other quality rules are unchanged.

Independent-review follow-up: the combined filter below now passes **296 tests**
on the owned S0 Debug baseline, including absent/uncompilable/out-of-discovery
declared projects, pre-execution expectation snapshots, environment filtering,
fixture-copying refusal of formal credit, and signed-receipt binding/expiry/key
regressions. The baseline generic scan still passes and the final gate still
fails as expected. Receipt tests use ephemeral in-memory test keys and
non-executable dummy artifacts; only assessor-authored regression CLIs were
executed. No untrusted submission or real external executor was run.

Verified against the original application in the private assessor worktree:

- Pinned .NET SDK 10.0.401: solution builds in Debug and Release, zero warnings
  and errors. No application, desktop or SQL execution was needed.
- The combined `AnalyzerUnit|ReplayUnit|RepositoryScan|StagePreservation` filter
  passed **272 tests**, zero skipped, with S0 integrity in both Debug and Release.
  This includes all retained analyzer fixtures and actual converter-shaped
  empty-class CLI rejection.
- S0 final-delivery `RepositoryScan|Modernization`: generic scan passed; final
  gate failed as expected (one pass, one failure). Business diagnostics remain
  exactly BUS001=1 and BUS002=1; no quality rule was relaxed to green the baseline.
- A deliberately missing source root failed RepositoryScan and still wrote an
  invalid report: evaluation-valid=false, hard-gate=false, overall=0 and
  remaining-work score=null.

These are evaluator/baseline checks, **not Golden acceptance**. S1/S2/S2a/S3/S4
profile contracts have synthetic regression coverage, but their real branch
scans remain integration work. No submission-specific trusted replay plan or
baseline/checkpoint fixtures have been supplied yet; TOOL002 deliberately
remains unsatisfied. Populate reviewed held-out cases, execute the actual
submitted CLI, reconcile generated/manual changes and historical checkpoint
identities, then perform the separate runtime/SQL/visual acceptance.

For a candidate-only checkout, run the same project from an external assessor
directory with `TASKOTIME_SOURCE_ROOT` set to the candidate's `src\TaskOTime`.
Do not add the oracle to the candidate application solution merely to run it.

## Localization references

The requested infrastructure is the Microsoft extensions localization pipeline
using conventional `.resx` resources; it is not a custom WPF string dictionary:

- [Creating .NET resource files](https://learn.microsoft.com/en-us/dotnet/core/extensions/create-resource-files)
- [Microsoft.Extensions.Localization](https://learn.microsoft.com/en-us/dotnet/core/extensions/localization)
