# Private modernization validation

Do not export this directory to candidates. It contains preparation/evidence
tooling, not application code. All commands below run from this directory; its
`global.json` pins SDK **10.0.401** without prerelease/roll-forward selection.
The coordinator owns the repository-wide SDK choice.

## Repeatable baseline

```powershell
.\Run-Validation.ps1
.\Run-Validation.ps1 -IncludeSql
.\Run-Validation.ps1 -IncludeSql -IncludeIdeal
```

Default execution runs existing in-memory AppServer, collection, workflow tests,
non-SQL isolation guardrails, and the independent STA host self-test. SQL requires
the explicit switch and Windows LocalDB `MSSQLLocalDB`. Missing SQL is a failed
prerequisite, never silently a pass/skip. `IncludeIdeal` runs correctness
regressions separately and propagates their nonzero exit status.

Logs, TRX files and command/commit/SDK manifests go to ignored `artifacts`.
Baseline capture does **not** turn a failing ideal test into a passing legacy
assertion. Compare named outcomes at each preservation stage; ideal acceptance
requires every correctness regression to pass.

The initial baseline has two defect families: non-first selected projects are
attributed to the first project, and completion uses the minutes component rather
than the full duration. Private correctness tests cover non-first-project saves,
manual/recorded 90-minute intervals, and manual/recorded day-plus-fractional-minute
intervals. They deliberately fail against S0, and should become green only on the
ideal track. They have no production/demo SQL dependency.

`IdealRegression.Tests` accepts `-p:ApplicationRoot=<absolute stage src\TaskOTime>`
and `-p:ValidationFramework=net10.0-windows` after that stage's product **and test
double** projects have migrated. It discovers VB/C# ViewModel project names.
Use a fresh private tooling copy/output per stage; don't share intermediates.

## Database ownership and failure policy

`LocalDbPersistenceTestDatabase` is an instance-owned, per-test fixture, never a
static reset. It generates `TaskOTime_Validation_<32-hex-GUID>` and a separate
random ownership token. SQL `CREATE DATABASE` is unconditional: a collision
fails without adopting, clearing, or dropping the existing database. Contexts
cannot be obtained before creation or after disposal. Each test disposes its
contexts before fixture cleanup, including failed test initialization.

The old script created/selected `TaskOTime`. Its previous rewriting missed
double-quoted identifiers and trusted a destructive preamble. The new helper
validates and **removes** the exact known three-batch preamble, rejects unknown
quoting/routing/database DDL in the remaining body, then executes schema batches
through an already-selected newly-created database connection. Compatibility
level 110 is applied using `ALTER DATABASE CURRENT`. No `USE TaskOTime`, fixed
database reset, command-line demo initializer, or broad string rewriting occurs.
The SQL source is trusted repository input; the conservative checks are not a
general-purpose SQL sandbox/parser. Review script changes before broadening them.

Cleanup requires both successful creation by that fixture and the matching
database-level extended property `TaskOTime.Validation.Owner`. Prefix matching
alone never authorizes deletion. Tests cover independent lifetimes, cleanup,
changed/missing lifecycle state, name collisions, and ownership-marker refusal.
Local connection pooling is disabled; no global pool clearing/shared database
reset is used.

An abrupt process kill or a failure before persisting the marker can leave an
orphan. This is intentionally safer than guessing ownership. There is no
prefix-sweeping janitor. Investigate such a failure and establish exact ownership
manually; never reset `TaskOTime` or `TaskOTime_AppServerIntegrationTests`.

## .NET 10 Windows STA smoke

```powershell
dotnet run --project StaSmoke -- --self-test

# Only while the creating fixture is alive, with that fixture's seeded user:
$env:TASKOTIME_VALIDATION_PASSWORD = '<isolated fixture password>'
dotnet run --project StaSmoke -- `
  --app '<fresh net10 output>\TaskOTime.App.dll' `
  --connection 'Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;Initial Catalog=TaskOTime_Validation_<guid>' `
  --owner '<creating fixture OwnerToken>' `
  --user '<isolated fixture administrator>'
Remove-Item Env:\TASKOTIME_VALIDATION_PASSWORD
```

The host is an independent `net10.0-windows` WPF executable with `[STAThread]`;
Windows PowerShell never loads modern application assemblies. Its self-test
constructs a WPF window and checks identity/STA and connection-option rejection
without opening SQL. A real run requires all four explicit options and a password
environment variable. It rejects production servers, demo/fixed names, attach-file
connections and missing/mismatched ownership before loading application code.
It accepts an explicit provider connection, constructs absolute EF metadata paths,
sets only the child process's production-mode connection, resolves application
dependencies, authenticates (including required temporary-password change), and
checks the original ListView identity, categories and dialog construction.
Its own config registers the existing EF6 SQL provider but contains no default
connection or database initializer; EF assemblies resolve from the application
output rather than an unrelated host EF6 dependency.

Real smoke **may write to the owned fixture**, especially authentication and
password state; it does not promise rollback. The creating fixture must seed a
tenant/admin/projects/categories, keep ownership alive while the process runs,
wait for its exit, and dispose the owned database afterward. Never reuse demo
credentials/data. The host itself cannot create/adopt/drop a database.

### Remaining migration/integration work

- Run real desktop smoke against freshly built .NET 10 product output. S0
  Framework output is explicitly rejected; a passing host self-test is not a
  migrated-application smoke pass.
- Wire the fixture-owning stage runner to seed the administrator and invoke this
  child host while the fixture is alive. The current AppServer fixtures prove
  EF6 SQL persistence separately.
- Port additional booking/command-strip SQL probes and theme/localization visual
  checks as those application interfaces stabilize. The host currently covers
  login, collection identity, categories and construction, not full UI acceptance.
- Retarget integration/test-double projects and EF6 dependencies at the approved
  framework stage. Keep GUID ownership and the marker contract unchanged.
- The existing product `VerifyDesktop.ps1` remains untouched by this test-only
  packet and must not be used against shared/demo databases.
