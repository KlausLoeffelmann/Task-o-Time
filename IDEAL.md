# S2 ideal result

This branch is the ideal result for `candidate-csharp-net472`. It starts at that
candidate's exact tip and overlays the completed common application plus the
canonical grader from `TheGoldenBranch` commit `9689f33`.

The candidate prompt and setup README remain at the repository root as
provenance. The ideal application is under `src\TaskOTime`; the post-result
grader is under `grader\TaskOTime.Grader`.

```powershell
dotnet restore .\src\TaskOTime\TaskOTime.slnx
dotnet build .\src\TaskOTime\TaskOTime.slnx -c Debug --no-restore
dotnet test .\grader\TaskOTime.Grader\TaskOTime.Grader.csproj -c Debug --no-restore
```

Repeat in Release. Both configurations must report valid compilation, no
definition-of-done diagnostics, and the universal score `1.000`. Reports are
written below `grader\TaskOTime.Grader\Artifacts\Reports\<Configuration>`.
