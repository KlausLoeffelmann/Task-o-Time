# Task-o-Time desktop

The Windows desktop application provides time bookings, task recording, and
main-data maintenance through the **Main Data** menu.

## Live language selection

Choose **Tools > Options > Language**, then **OK**, to switch the running
application between English, German, Dutch, and Spanish. **Cancel** discards the
draft without changing the application language. The saved language is loaded
before the next sign-in window; a restart is not required to apply it.

`TaskOTime.ViewModel\Localization` provides a shared
`Microsoft.Extensions.Localization` resource-manager localizer. The pinned
10.0.0 package contains .NET Framework 4.6.2, .NET Standard 2.0, and .NET 10
assets; this application remains on **.NET Framework 4.7.2**. Framework
migration is a separate change.

`Resources\Strings.resx` is neutral English. `Strings.de.resx`,
`Strings.nl.resx`, and `Strings.es.resx` have the same keys and translated
values. Regional cultures use their parent language resources while retaining
regional formatting. Unsupported or malformed culture names use English;
unknown resource keys follow the Microsoft localizer's key-name fallback.
User-entered titles, descriptions, and server diagnostic details are not
translated.

Views bind labels using `{loc:Loc ResourceKey}` and set
`Language="{loc:Culture}"` at their root. Both are normal WPF bindings, so
already-created controls refresh and inherit the selected formatting/parsing
culture. `LocalizedViewModelBase` uses weak property-change subscriptions to
refresh computed text without retaining discarded view models. Stateful
messages retain resource keys and arguments, not pretranslated strings.
Clock input accepts the culture's short time and the editor's 24-hour form;
elapsed durations remain durations, not calendar dates.

All four required surfaces consume these bindings: Login (including forced
password changes), Main time collection, the booking editor, and Project Main
Data. Project labels, assignment state, validation, operation names, and
success/error status use `Project_*` and `Common_*` resources. The Main Data
host title and Projects tab also update while the window is open.

`MaintenanceViewModel` derives from `LocalizedViewModelBase` and retains its
`OperationStatusText` as a `LocalizedMessage` key/argument object. Nested operation
messages are translated again after a culture switch, while server error
codes and diagnostic details remain intact. Notifications still go through
`IMaintenanceInteraction`; changing culture does not repeat a notification or
service request. The Project editor validates a blank name before saving.
User-entered drafts and selected project identity survive language changes.
New project names are localized when created, but their persisted identifier
`NEW` remains invariant.
Other maintenance and placeholder report dialogs are outside this scope.

Run localization resource, fallback, options, parsing, weak-subscription, and
STA UI-binding tests without accessing SQL:

```powershell
dotnet test .\TaskOTime.Localization.Tests\TaskOTime.Localization.Tests.csproj
```

## Local demo

The default startup uses the named EF6 `TaskOTimeContext` connection in
`App.config`. It points to the existing `(localdb)\MSSQLLocalDB` database named
`TaskOTime`; the desktop neither creates nor regenerates that database.

From `src\TaskOTime`, using PowerShell on Windows:

```powershell
dotnet build .\TaskOTime.App\TaskOTime.App.csproj
& .\TaskOTime.App\bin\Debug\net472\TaskOTime.App.exe
```

Generated local data uses the accounts configured in
`TaskOTime.Cli\demodataconfig.json`, including `AdminDE`, `LisaDE`, `AdminNL`,
and `DaanNL`. Use the configured preliminary password. Generated accounts must
replace it on first login before the main window opens. Incorrect credentials
are rejected, and five failed attempts lock an account for 15 minutes.

Bookings, tasks, project/collaboration edits, user operations, and password changes use the SQL services
and remain in the database after the application closes. Startup and service
errors are reported; there is no in-memory fallback.

### Regenerating local data

`demodataconfig.json` currently creates 360 time entries covering every date in
the 45-day range ending today. To rebuild the local database from that
configuration:

```powershell
dotnet build .\TaskOTime.Cli\TaskOTime.Cli.csproj
& .\TaskOTime.Cli\bin\Debug\TaskOTime.Cli.exe --new --createDemoData --validateDemoData
```

`--new` drops and recreates the `TaskOTime` database. Back up any data that must
be retained before running it. Date coverage can be changed through
`timeItemDates.days`, `includeToday`, and `skipWeekends`; validation requires
current-day data and enough entries to form complete intervals on every
configured date.

## Database connection

Set `TASKOTIME_MODE=Production` and `TASKOTIME_CONNECTION_STRING` to an EF6
entity connection, a named connection (`name=...`), or a SQL Server provider
connection string for an existing Task-o-Time database. Provider strings reuse
the model metadata from the named `TaskOTimeContext` connection. Do not commit
connection credentials. Failed production connections or authentication do not
fall back to the local database.

Use an active administrator account and a tenant with projects and categories;
the current main-data API requires administrative authorization. Database
provisioning and system-marker categories must already be configured. The
desktop does not create the database or seed production users.

## Recording time

Choose a date to view bookings. The booking editor accepts a time, description,
project, and a category selected from SQL main data. The toolbar provides
pause, downtime, errand, stop, and checkout actions. Tasks can be started and
completed; manual completion uses the start and duration fields. Creating a new
activity or interruption ends the current task interval at that booking's
timestamp.

Downtime is represented by `EventInfo=DownTime` with the non-working stop
category. It survives timeline normalization and is excluded from booked work.
Errands use `EventInfo=Errand` and the same non-working category while remaining
continuous timeline markers rather than stop marks. Edits select their existing
category, and refreshed main data updates the category list. Service-returned
booking days are authoritative.

## Main Data maintenance

Maintenance screens bind observable properties, selections, and `DelegateCommand`
instances. `MainDataViewModel` composes the child view models using a shared
`ServiceWorkspace`; it never creates or retains controls. The desktop supplies
`IMaintenanceInteraction` for notifications and confirmations. Views own visual
behavior, including transfer and clearing of the create-user password box.

Project and task editors keep drafts separate from service objects. Successful
saves replace the collection item with the authoritative service result; failed
operations retain the draft and selection. Archiving a project uses soft deletion.
Task lists use the explicitly selected project. Category, tag, note, web-link,
and task deletion retain their existing hard-delete behavior. Activity log edits
remain local to the workspace.

Maintenance mutation commands require an active, non-deleted administrator.
Server-side Main Data authorization remains authoritative. Create-user fields
include a temporary password, which is cleared after successful creation; the
existing service still enforces initial-password replacement on login.

**Tenant name/active edits are workspace-only:** the current user-administration
contract has no tenant-update operation. The screen and success notification make
this limitation explicit rather than claiming database persistence.

Application API types use `MainData` (for example `IAdminMainDataService`).
SQL table names, EF mappings, and persisted identifiers are unchanged.

## Development checks

```powershell
dotnet build .\TaskOTime.slnx
dotnet test .\TaskOTime.TimeTrackingServices.Tests\TaskOTime.TimeTrackingServices.Tests.vbproj
dotnet test .\TaskOTime.AppServer.Tests\TaskOTime.AppServer.Tests.csproj
powershell.exe -NoProfile -Sta -File .\TaskOTime.App\VerifyDesktop.ps1
```

The desktop smoke uses the generated local SQL credentials, rolls back its
authentication and booking probes, loads service-backed categories, constructs
the windows and resources, and verifies the time list's collection source.
The STA desktop test also inventories menu, command-strip, time-panel, and
bound Main Data commands at runtime. `MaintenanceViewModelTests` also exercise
create/save/archive/delete, selection, service failures, request scope,
administrator gating, and temporary-password handling without creating views.
