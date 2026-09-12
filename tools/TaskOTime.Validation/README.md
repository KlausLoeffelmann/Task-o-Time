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

Default execution runs all 30 existing AppServer and all 33 TimeTrackingServices
tests, non-SQL isolation/fixture-runner guardrails, and the independent STA host
self-test. There is no workflow filter excluding the WPF binding test. SQL requires
the explicit switch and Windows LocalDB `MSSQLLocalDB`. Missing SQL is a failed
prerequisite, never silently a pass/skip. It also runs the real-service fixture
setup/child-process checks described below. `IncludeIdeal` runs correctness
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
`Run-Validation.ps1` also accepts `-ApplicationRoot` and `-ValidationFramework`
(default `net472`), forwarding them to the private fixture/correctness projects.

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

## Fixture-owning runner

`FixtureRunner` is an executable referencing the **real application services**,
not source-linked service doubles. It defaults to `net472`, links the already-safe
integration database helper from `ApplicationRoot`, and preserves its GUID/token
ownership lifecycle. It extracts its private EF metadata from the stage EDMX;
this is fixture metadata, not proof that the desktop packaged its own metadata.
Its config has no default connection or database initializer.

```powershell
# No SQL: CLI, runtime, connection and Windows argument-quoting guardrails.
dotnet run --project FixtureRunner -- self-test

# Real owned LocalDB setup/authentication and child lifecycle checks.
# Explicitly reports "Desktop NOT RUN"; this is not desktop acceptance.
dotnet run --project FixtureRunner -- fixture-check
```

The fixture calls production `UserAdministrationService.CreateTenantAdmin`,
`AdminMasterDataService.CreateProject` (twice), and `CreateCategory` using its
explicit EF context factory. The administrator has a random per-run identifier
and password, a one-hour temporary-password expiration, and required initial
password change. Seeding and authentication are checked through real EF6 queries.
It also calls `SystemTimeMarkerSeed.EnsureCategories` with the owned administrator
before any booking smoke. Fresh-context and child-process queries verify the
reserved work-break/stop category IDs, owner, public flags, names and lookup IDs.
The fixture check corrupts marker metadata only in its owned database, requires
rejection, then verifies idempotent reseeding. Pause/stop foreign-key targets
therefore exist without relying on shared demo data.
No demo config or shared database is read or reset.

`fixture-check` launches a real child which verifies the ownership marker,
authenticates, changes the initial password, and verifies replacement credentials.
Negative checks reject missing child passwords and mismatched owner tokens,
terminate a timed-out child, and prove cleanup while propagating child failure.
It confirms the parent environment was unchanged and the owned database was
removed. Only successful fixture creation authorizes the linked helper's cleanup.

## .NET 10 Windows STA smoke

```powershell
dotnet run --project StaSmoke -- --self-test
dotnet build StaSmoke

# Preferred orchestration once real .NET 10 application output exists.
dotnet run --project FixtureRunner -- desktop `
  --app '<fresh net10 output>\TaskOTime.App.dll' `
  --host '<absolute tooling output>\StaSmoke\bin\Debug\net10.0-windows\StaSmoke.dll' `
  --timeout-seconds 120

# Build the fixture against the migrated stage's real services:
dotnet run --project FixtureRunner `
  -p:ApplicationRoot='<absolute stage src\TaskOTime>' `
  -p:ValidationFramework=net10.0-windows -- desktop `
  --app '<fresh net10 output>\TaskOTime.App.dll' `
  --host '<absolute StaSmoke.dll>'
```

The fixture runner validates both explicit DLL/runtimeconfig paths and the
installed .NET 10 WindowsDesktop runtime **before creating SQL**. Missing modes,
arguments, Framework application binaries, missing runtime, child failure and
timeout all produce nonzero exit status; there is no automatic self-test fallback
or skipped desktop pass. Default child timeout is 120 seconds (allowed 1–1800).
Missing SQL is likewise a failure. EF defaults are 6.5.1 for `net472`, 6.5.2 for
modern targets; `ValidationEfVersion` is an explicit compatibility override.

The runner holds the fixture alive while the host runs, passes the password only
through that child's `TASKOTIME_VALIDATION_PASSWORD` environment, redacts it from
captured child output, and waits for exit before cleanup. It does not change the
parent password environment or place credentials in command arguments/files.
On timeout the directly launched host is terminated before cleanup. On Framework
this assumes the supplied smoke host does not spawn descendants (the supplied
`StaSmoke` does not); modern targets terminate the process tree.

For external orchestrators the host's low-level contract remains
`--app <net10 DLL> --connection <fixture.ProviderConnectionString>
--owner <fixture.OwnerToken> --user <seeded user>` with the password in the child
environment only. `probe` and `probe-timeout` are internal fixture-check commands,
not alternative desktop acceptance modes.

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

Maintenance composition supports exactly two layouts:

- Legacy S3 candidate: `MasterDataWindow` plus
  `MasterDataViewModel(window, tenant, userId, admin, users, bookings, tab)`.
- Ideal MVVM: `MainDataWindow`, `MaintenanceInteraction(Window)` and
  `MainDataViewModel(tenant, userId, admin, users, bookings, tab, IMaintenanceInteraction)`.

The host validates exact constructor parameter types, requires the interaction
interface to belong to the loaded ViewModel assembly, assigns `window.DataContext`
explicitly, and tracks the window for cleanup before composing its viewmodel.
Incomplete/mixed layouts, incompatible services and unexpected signatures fail;
it never silently falls back from a broken new layout to the old one. Its
self-test exercises both compositions using isolated synthetic windows and
negative contracts, not real application acceptance.

Real smoke **may write to the owned fixture**, especially authentication and
password state; it does not promise rollback. `FixtureRunner` owns creation,
seeding and disposal; the host itself cannot create/adopt/drop a database.
Never reuse demo credentials/data.

### Remaining migration/integration work

- Run real desktop smoke against freshly built .NET 10 product output. S0
  Framework output is explicitly rejected; a passing host self-test is not a
  migrated-application smoke pass.
- Run the implemented fixture-owner/host orchestration against S3, including the
  real forced-password-change UI flow. Framework `fixture-check` proves the
  SQL/service/process lifecycle, not migrated desktop behavior.
- Exercise localization/theme through real application startup once their
  integration interfaces are stable. Generic-Application construction smoke
  does not prove provider startup, live language/theme changes or OS contrast
  behavior. This tooling does not change registry or machine-wide contrast state.
- Port additional booking/command-strip SQL probes and theme/localization visual
  checks as those application interfaces stabilize. The host currently covers
  login, collection identity, categories and construction, not full UI acceptance.
- Retarget integration/test-double projects and EF6 dependencies at the approved
  framework stage. Keep GUID ownership and the marker contract unchanged.
- The existing product `VerifyDesktop.ps1` remains untouched by this test-only
  packet and must not be used against shared/demo databases.
