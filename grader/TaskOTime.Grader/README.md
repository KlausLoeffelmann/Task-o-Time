# Task-o-Time grader

This xUnit project is the complete post-result grader for every Task-o-Time
candidate scenario. S1, S2, S2a, and S3 begin at different points but share
one final application outcome and therefore one grader, rubric, and definition
of done.

The grader is not provided while a candidate works. After the candidate claims
completion, copy the complete `grader\TaskOTime.Grader` directory to the
repository root and add `grader\TaskOTime.Grader\TaskOTime.Grader.csproj` to
`src\TaskOTime\TaskOTime.slnx`. No stage selector, replay plan, signed receipt,
Sandbox, external executor, or migration history is required.

## Run

Build the application first so WPF-generated compiler inputs exist:

```powershell
dotnet restore .\src\TaskOTime\TaskOTime.slnx
dotnet build .\src\TaskOTime\TaskOTime.slnx -c Debug --no-restore
dotnet test .\grader\TaskOTime.Grader\TaskOTime.Grader.csproj -c Debug --no-restore
```

Repeat the build and grader in Release. Normally the grader finds
`src\TaskOTime` by walking up from its own directory. For grader development
only, `TASKOTIME_SOURCE_ROOT` may point at another directory containing
`TaskOTime.slnx`; reports record that an override was used.

Reports are written below:

```text
grader\TaskOTime.Grader\Artifacts\Reports\<Configuration>\
```

`roslyn-diagnostics.json` contains all diagnostics, criterion results, source
inventory, and the normalized score. `repository-assessment.csv` is the compact
weighted grade.

## Universal rubric

The required score excludes migration process, history, and tooling. Those may
be useful engineering evidence, but the four candidate scenarios are graded
only on their shared final outcome.

| Criterion | Points | Fixed subrules |
| --- | ---: | --- |
| Business correctness | 28 | Correct booking-project attribution; elapsed duration retains hours |
| WPF MVVM architecture | 18 | `MOD001`-`MOD006` and protected-core rule `COR001`, averaged equally |
| Localization | 14 | Required localization infrastructure; required UI consumes localized text |
| Production language | 9 | No authored production Visual Basic |
| Theme | 9 | No static theme conflicts; all required states/surfaces are covered |
| English documentation | 5 | English prose 70%; essential XML documentation 30% |
| Main Data naming | 5 | No obsolete presentation/application terminology |
| SDK-style projects | 3.5 | Every production project is SDK-style |
| .NET 10 | 3.5 | Every application and test project has its required .NET 10 target |

The 95 outcome points are normalized to `0.000`-`1.000`. Repeated diagnostics
for one subrule do not change that subrule's weight. Independently verifiable
subrules receive independent partial credit.

The `RepositoryScan` test writes a report whenever the source can be evaluated.
The separate `DefinitionOfDone` test requires a score of `1.000` and no
definition-of-done diagnostics. This distinguishes an incomplete candidate from
a broken grader.

## Scope

Projects are discovered from `src\TaskOTime`; tests are compiled but excluded
from production-language and architecture diagnostics. The grader directory,
build output, and analyzer fixtures are outside the product source root.
`ScenarioScope.xml` contains product requirements only, not Golden-specific
tool or fixture paths.

Analyzer fixtures exercise positive, negative, renamed-symbol,
alternate-implementation, empty-scope, compiler-error, and deletion cases.
`RubricTests` verifies fixed-subrule partial credit and ensures duplicate
diagnostics cannot distort the grade. `CandidateBaselines.json` records the
exact score and criterion results produced by clean exports of the four frozen
candidate commits.
