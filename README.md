# Task-o-Time

This harness-template repository contains the staged modernization starting
points, reusable migration tools, evaluator material, and corrected reference
application.

| Stage | Branch or tag | Application state |
| --- | --- | --- |
| S0 | `modernization-original` | Original Framework application, including production VB |
| S1 | `candidate-vb-net472` | Framework 4.7.2 normalization checkpoint |
| S2 | `candidate-csharp-net472` | Production C#; Framework 4.7.2 |
| S2a | `modernization-S2a-sdk-net472` | SDK-style projects; still Framework 4.7.2 |
| S3 | `candidate-csharp-net10` | Production C#, SDK-style projects, .NET 10 |
| S4 | `TheGoldenBranch` / `modernization-S4-ideal` | Corrected reference application on .NET 10 |

S0-S3 retain the intentional nonmigration defects. Existing VB tests can remain;
S2 and S3 do not require production VB conversion. S4 includes business and MVVM
corrections, English documentation and Main Data naming, live English/German/
Dutch/Spanish localization, and light/dark/high-contrast presentation.
EF6, SQL Server, persisted schema compatibility, and desktop workflows remain.

Application prerequisites and launch instructions are in
`src\TaskOTime\TaskOTime.App\README.md`. Reusable language and project migration
commands are documented in `tools\TaskOTime.Migration\README.md` and
`tools\TaskOTime.ProjectMigration\README.md`. Stage provenance and previously
collected development evidence are recorded in `tools\StageBaselines.json`.

## Preparation is separate from assessment

The harness requires all relevant versions in this repository on `origin`,
including Golden, candidate snapshots, tools, and evaluator history. Publishing
them here is intentional. This complete repository and its everything/reference
branch must **never** be the model's working checkout.

Creating these stages and the Golden reference does not run the assessment or
require an isolated replay receipt. Formal grading happens later in a headless
Windows Docker container, using a completely new repository and working branch
containing only the selected candidate snapshot. Do not clone this complete repository into a
candidate environment: other refs and history expose the reference solution.

Use `tools\ExportCandidate.ps1` with the selected standalone stage prompt to
prepare the clean application payload, then initialize the new candidate
repository from that payload. Keep Golden, evaluator sources, preparation tools,
and private evidence out of the candidate repository. Do not retain a candidate
remote that exposes the template repository's other refs. Use the same frozen
snapshot and prompt for comparable trials.

The `modernization-*` tags preserve the original S0-S4 checkpoints.
`TheGoldenBranch` also carries subsequent prompt and handoff documentation
updates; the S4 tag is not moved for those changes. Supporting `ideal-*`,
`tooling-*`, `validation-isolation`, and `assessment-*` branches preserve
implementation provenance. In particular, `assessment-stage-profiles` includes
unfinished experimental replay work, not an approved harness configuration.

Golden's preparation is not a claim that a later formal assessment has run or
awarded 1.0. Experimental Windows Sandbox diagnostics are retained as supporting history;
they are not prerequisites for branch creation or the planned container setup.
