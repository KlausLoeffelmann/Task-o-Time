# Task-o-Time assessor guide

## Branch matrix

| Candidate | Matching ideal |
| --- | --- |
| `candidate-vb-net472` | `ideal-vb-net472` |
| `candidate-csharp-net472` | `ideal-csharp-net472` |
| `candidate-csharp-sdkstyle-net472` | `ideal-csharp-sdkstyle-net472` |
| `candidate-csharp-net10` | `ideal-csharp-net10` |

The candidates have different starting conditions but one required outcome.
Every ideal contains the same completed `src\TaskOTime` tree and the same
`grader\TaskOTime.Grader` tree. Candidate prompts differ only in which migration
steps are already complete and must not be repeated.

## Candidate run

Create a repository containing only the selected candidate snapshot and its
root `Candidate-Prompt.md`. The public harness/template repository contains
ideal and grader refs, so the candidate environment must have no network or
GitHub access. Do not put the grader, another branch, Git objects from this
template, expected diagnostics, or ideal implementation files in the candidate
repository.

## Grade a completed result

After the model claims completion:

1. Copy the canonical `grader\TaskOTime.Grader` directory into the completed
   result.
2. Add `grader\TaskOTime.Grader\TaskOTime.Grader.csproj` to
   `src\TaskOTime\TaskOTime.slnx`.
3. Restore and build the full solution in Debug and Release.
4. Run the grader project directly in both configurations.
5. Collect `roslyn-diagnostics.json` and `repository-assessment.csv`.

This copy is currently manual; automating it is out of scope.

The grader uses no stage selector. It does not require replay plans, signed
receipts, Sandbox/AppContainer, an external executor, or a migration-tool
contract. Migration process and tooling are not part of the required score.

## Interpret results

The normalized `OverallScore` is the weighted universal final-outcome grade.
Every ideal must produce `1.000`, valid compilation, and no
definition-of-done diagnostics. An incomplete candidate still receives JSON
and CSV before the `DefinitionOfDone` test fails. Loader/compiler/scope failures
make evaluation invalid rather than returning a misleading low grade.

Analyzer test count measures grader implementation coverage; it is not the
grading scale. Fixed prompt-derived criterion/subrule weights define the grade.
