# Task-o-Time modernization harness

This public template contains four modernization starting points, one corrected
reference application, and one universal Roslyn/xUnit grader.

| Scenario | Candidate branch | Matching ideal branch | Starting point |
| --- | --- | --- | --- |
| S1 | `candidate-vb-net472` | `ideal-vb-net472` | VB and C#, .NET Framework 4.7.2 |
| S2 | `candidate-csharp-net472` | `ideal-csharp-net472` | Production C#, .NET Framework 4.7.2 |
| S2a | `candidate-csharp-sdkstyle-net472` | `ideal-csharp-sdkstyle-net472` | Production C#, SDK-style, .NET Framework 4.7.2 |
| S3 | `candidate-csharp-net10` | `ideal-csharp-net10` | Production C#, SDK-style, .NET 10 |

The scenarios have different starting conditions and require different amounts
of work, but all must converge on the same completed application, definition of
done, rubric, and ideal grade of `1.000`. S0 and the thematic `ideal-*` branches
are historical construction provenance, not active candidate scenarios.

The final application retains EF6, SQL Server, persisted schema compatibility,
and desktop workflows while providing corrected business behavior, MVVM
architecture, English documentation and Main Data terminology, English/German/
Dutch/Spanish localization, and light/dark/high-contrast presentation.

## Candidate isolation

All relevant template refs are intentionally published to `origin`; therefore a
trial model must not receive a clone of this repository. Export only the
selected candidate:

```powershell
.\tools\ExportCandidate.ps1 -Stage vb-net472 -OutputDirectory <path>
.\tools\ExportCandidate.ps1 -Stage csharp-net472 -OutputDirectory <path>
.\tools\ExportCandidate.ps1 -Stage csharp-sdkstyle-net472 -OutputDirectory <path>
.\tools\ExportCandidate.ps1 -Stage csharp-net10 -OutputDirectory <path>
```

Initialize the exported payload as a new candidate-only repository and run it in
a no-network/no-GitHub environment. Each active candidate includes its own
root `Candidate-Prompt.md`; the exporter rejects grader, assessment, ideal,
preparation-tool, and Git-history material.

## Grading

After a model claims completion, manually copy the canonical
`grader\TaskOTime.Grader` directory into its result, add the project to
`src\TaskOTime\TaskOTime.slnx`, build the application, and run the grader. The
grader has no stage selector and applies the same universal outcome rubric to
all four scenarios.

Migration tooling and migration history are not required final deliverables and
do not affect the score. Grading requires no replay plan, external executor,
signed receipt, Sandbox, or AppContainer.

See `assessment\Assessor-Guide.md` for the complete assessor workflow and
`grader\TaskOTime.Grader\README.md` for commands, report locations, and rubric.
Application setup is documented in
`src\TaskOTime\TaskOTime.App\README.md`.
