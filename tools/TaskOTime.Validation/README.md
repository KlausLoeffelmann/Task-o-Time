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
self-test, a synthetic startup-driver run, and four synthetic negative startup
cases. Negative cases require both exit code 1 **and their specific diagnostic**;
an unrelated missing runtime/build failure cannot satisfy them. There is no
workflow filter excluding the WPF binding test. SQL requires
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
the selected admin service's `CreateProject` (twice), and `CreateCategory` using its
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

### Legacy and renamed service APIs

`FixtureRunner` selects typed aliases from the source layout under
`ApplicationRoot\TaskOTime.AppServer\Services`:

| `ValidationDataApi` | Required service file/type | Category DTO | Query request |
| --- | --- | --- | --- |
| `MasterData` | `AdminMasterDataService.cs` / `AdminMasterDataService` | `CategoryMasterDataDto` | `MasterDataQueryRequest` |
| `MainData` | `AdminMainDataService.cs` / `AdminMainDataService` | `CategoryMainDataDto` | `MainDataQueryRequest` |

Automatic selection requires exactly one known service filename. Missing,
ambiguous or unsupported layouts fail during restore/build instead of silently
choosing an API. `-p:ValidationDataApi=MasterData` or `MainData` explicitly selects
one layout (including deliberate disambiguation); its service file must exist.
The C# compiler must still resolve the actual production types. No duplicated
service implementation, runtime type substitution or reflection fallback is used.
Build output and fixture logs identify the selected API.
`IdealRegression.Tests` imports the same `FixtureRunner\ValidationDataApi.props`
selection and aliases its query type; its five correctness assertions are
unchanged. Preserve that sibling import when making a private tooling copy.

Run the seven source-layout selection checks without SQL:

```powershell
.\Test-FixtureApiSelection.ps1
```

Keep a fresh private runner copy/output per application root to avoid stale
assemblies/intermediates when switching between APIs. For an already-built,
read-only integration root, copy only the runner's top-level source/project/config/props
files to a fresh ignored artifact directory, then restore only that runner and
consume existing production project outputs:

```powershell
$copy = 'artifacts\fixture-main' # Use a new directory, not a previous stage's output.
New-Item -ItemType Directory $copy -ErrorAction Stop
Get-ChildItem FixtureRunner -File | Copy-Item -Destination $copy
$properties = @(
  '-p:ApplicationRoot=<absolute built integration src\TaskOTime>',
  '-p:ValidationFramework=net472',
  '-p:BuildProjectReferences=false'
)
dotnet restore "$copy\FixtureRunner.csproj" --no-dependencies @properties
dotnet build "$copy\FixtureRunner.csproj" --no-restore @properties
& "$copy\bin\Debug\net472\FixtureRunner.exe" self-test
& "$copy\bin\Debug\net472\FixtureRunner.exe" fixture-check
```

Both the original `MasterData` root and integrated `MainData` root have passed
real `net472` fixture checks using their actual production assemblies: owned
seeding/marker round trips, forced-password authentication, negative child cases,
timeouts and verified cleanup. This does **not** establish .NET 10 acceptance.
After the runtime stage, use its matching `ValidationFramework` and preserve
production project references rather than source-linking duplicate service types.

The private correctness project also compiles against both APIs. Its five tests
passed against the integrated Framework root after an **artifact-only test-host**
binding redirect: the copied output contains `System.Threading.Tasks.Extensions`
assembly 4.2.4.0, while MSTest requests 4.2.0.1. The copied
`IdealRegression.Tests.dll.config` redirected versions 0.0.0.0–4.2.4.0 to 4.2.4.0.
No production configuration/dependencies were changed. Initial zero-test discovery
was rejected, not counted as a pass. This Framework test-host compatibility step
must be retained for equivalent Framework reruns; do not assume the .NET 10 host
needs the same redirect. Always verify actual discovery/execution counts.

## .NET 10 Windows STA smoke

```powershell
dotnet run --project StaSmoke -- --self-test
dotnet build StaSmoke

# Construction-only compatibility smoke (does NOT run real App startup).
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

### Genuine application startup

```powershell
# S3: preserve its appearance/language and existing business semantics.
dotnet run --project FixtureRunner -- startup --required-features core `
  --app '<fresh S3 net10 output>\TaskOTime.App.dll' `
  --host '<absolute StaSmoke.dll>' --timeout-seconds 120

# Golden, after ideal startup/localization/theme/business integration.
dotnet run --project FixtureRunner `
  -p:ApplicationRoot='<absolute Golden src\TaskOTime>' `
  -p:ValidationFramework=net10.0-windows -- startup --required-features ideal `
  --app '<fresh Golden net10 output>\TaskOTime.App.dll' `
  --host '<absolute StaSmoke.dll>' --timeout-seconds 180
```

The fixture owner and child credential/cleanup contract are identical for
`desktop` and `startup`. Neither mode defaults to a demo connection. Startup
requires an explicit `core` or `ideal` gate; missing/unknown gates fail before
SQL creation. The child receives a dispatcher deadline ten seconds shorter
than the parent's process deadline (minimum one second). The parent remains the
hard timeout for blocked native dialogs or synchronous application code.

Startup loads the **real `TaskOTime.App.App`**, validates its startup override,
calls its `InitializeComponent`, and runs its application dispatcher. It does
not create replacement services, manually install application resources, or
construct substitute production windows. It finds only that application's
windows and uses named WPF controls, bound option controls, default routed
button clicks and actual viewmodel commands within the process. No SendKeys,
global input, registry writes or machine preference changes are used.

Before constructing App, the host replaces `Properties.Settings.Default`'s
providers with a private **in-memory `SettingsProvider`**, clears cached values
and uses declared defaults. All later placement/options saves remain in that
provider through product `OnExit`; the real provider is never restored during
shutdown. No real user app configuration is written. Missing/incompatible
settings types or provider replacement fail explicitly. Applications introducing
another settings store require a reviewed isolation adapter before execution.

Both gates verify forced initial-password change through the actual login
buttons, real startup's `MainWindow`, original ListView/collection identity,
fixture categories, opening/saving Options through `OptionsCommand`, and a
booking through `TimeCollection.AddCommand` and the actual booking editor.
A fresh SQL query must find exactly one booking with the chosen project/category.
Core selects the first project so S3 is not required to repair the preserved
non-first-project defect; ideal deliberately selects the last of at least two.
The app must log out, close its windows and finish dispatcher shutdown.

Ideal additionally opens the real Main Data and booking dialogs through commands
while Options/Main remain open, and:

- Requires the actual localization singleton and one uniquely identifiable
  startup-owned `ThemeService` instance. It never calls `ThemeService.Start` to
  compensate for missing product startup integration.
- Changes the real localization service through en/de/nl/es while login is open,
  then while all four later windows remain open. Visible bindings must resolve
  to the current provider values, each window must visibly change in every
  language transition, and root `Language` must follow culture.
  Checking only the singleton's `Culture` is insufficient.
- After closing the nested booking/Main Data dialogs, selects another culture
  through Options `SelectedValue`. Selection must update only its pending clone.
  The default OK button must then commit the culture to the real main viewmodel,
  localization service, visible main bindings and in-memory persisted settings.
  This deliberately respects the product's commit-on-OK semantics.
- Switches the startup instance through Light/Dark/HighContrast/System. It
  checks effective-theme notifications, the OS high-contrast override,
  live window backgrounds, required brushes and the main calendar's shared
  `ThemedCalendarStyle`. It changes only application theme selection, never OS
  contrast settings.
- Checks that product `OnExit` disposed that same instance, rather than disposing
  it itself to manufacture lifecycle evidence.

Reflection contracts fail closed. Localized target bindings must use the real
singleton as Source and `[Key]`/`Item[Key]` paths. The confirmed localization
contract (`9d8470b`) identifies the unnamed Options language selector by
`SelectedValue` bound to `CultureName`, `ItemsSource` bound to `AvailableCultures`,
`SelectedValuePath=Name`, and `DisplayMemberPath=NativeName`. Culture is committed
only on OK; changing the clone is not itself a live-language operation.
The theme contract requires `ThemeChanged` as `EventHandler`, enum selection,
and the confirmed private `bool disposed` (`3777b21`, startup wiring `c452cb1`).
App's private `themes` field is discovered by its exact service type; the host
checks its disposal flag after real application/dispatcher shutdown. It does not
call `SetTheme` after shutdown: dispatcher cancellation could mask whether the
service's disposal guard was reached. These ideal integration
contracts still require verification against the parent's final built output;
unsupported signatures are failures, not skipped feature checks.

The low-level host adds `--mode startup --required-features core|ideal
--timeout-seconds <1-1800>` to its explicit connection contract below.
Omitting `--mode` retains **construction-only** compatibility, never startup
acceptance.

Synthetic checks deliberately load no product assemblies and open no SQL:

```powershell
dotnet run --project StaSmoke -- --startup-self-test core
# Each following command MUST fail with its specific diagnostic (exit 1).
dotnet run --project StaSmoke -- --startup-self-test invalid-login
dotnet run --project StaSmoke -- --startup-self-test early-exit
dotnet run --project StaSmoke -- --startup-self-test missing-ideal
dotnet run --project StaSmoke -- --startup-self-test timeout
```

The standard host self-test also checks the exact Options selector with all four
`CultureInfo` items and rejects missing languages/incompatible value paths.
The synthetic theme lifetime check accepts only a true private boolean disposal
flag and rejects false, missing and incorrectly typed state.
The synthetic core check exercises InitializeComponent/OnStartup/OnExit, the
nested modal dispatcher sequence, UI credential entry, option bindings,
in-memory settings save/reload and the booking callback. It is labelled
**SYNTHETIC / NOT desktop acceptance**. Negative cases reject bypassed password
change, premature application exit, missing ideal services and elapsed timeout.

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
checks the original ListView identity, categories and dialog construction in
construction mode. The genuine-startup path is described above.
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

- Run real `startup --required-features core` against freshly built S3 .NET 10
  product output. S0
  Framework output is explicitly rejected; a passing host self-test is not a
  migrated-application smoke pass.
- Run `startup --required-features ideal` after confirming the integrated
  reflection contracts. Neither real .NET 10 startup gate has been executed in
  this tooling packet. Framework `fixture-check` proves SQL/service/process
  lifecycle, not migrated desktop behavior.
- Port additional booking/command-strip SQL probes and theme/localization visual
  checks as those application interfaces stabilize. The implemented startup
  probes are not exhaustive visual/keyboard/accessibility acceptance.
- Retarget integration/test-double projects and EF6 dependencies at the approved
  framework stage. Keep GUID ownership and the marker contract unchanged.
- The existing product `VerifyDesktop.ps1` remains untouched by this test-only
  packet and must not be used against shared/demo databases.
