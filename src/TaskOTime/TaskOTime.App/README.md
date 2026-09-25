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
assets. Localization was introduced on .NET Framework 4.7.2; the separately
approved integration baseline now targets **.NET 10 for Windows**.

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

The provider exposes framework-neutral `CultureInfo` and `CultureName` values.
The WPF markup and `XmlLanguage` conversion live under `Views\Localization`,
retaining the existing markup namespace for XAML compatibility. App startup
registers the dispatcher-backed `ICultureChangeContext` before applying saved
options and releases it on exit. Culture mutation on a worker thread is rejected
while the WPF host is registered; notification and binding updates stay on its
UI thread. Headless hosts need no WPF application and can supply their own access
policy. Scoped policies are released in reverse registration order.
`WeakNotifications` observes property and collection changes with weak recipients
and non-capturing callbacks, removes dead recipients on notification, and supports
immediate disposal. Providers and model code do not use WPF weak-event managers.

Sign-in and temporary-password changes show the translated `TenantInactive`
reason when the authentication service rejects an inactive, deleted, or
unavailable tenant. The diagnostic error code is preserved. Both booking
end-before-start checks share one resource key, and a failed completion with
failed compensation retains both underlying exceptions alongside its localized
summary.

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
Main-menu export/report placeholders retain their existing behavior, with live
localized headings, date formatting, details, and close tooltip. Editable sample
tasks, task-list defaults, and sample bookings use the selected language at
creation; later language changes do not overwrite these or user-authored content.
The Main task-list editor uses live labels and preserves its draft. Server task
due dates remain date values in the display model, so their short-date text
refreshes when language changes. Startup configuration errors use the same real
catalog and retain underlying database diagnostic details.
Other maintenance tabs remain outside the four-surface translation scope.
The unused empty App resource/designer pair and obsolete single-string XAML
dictionary have been removed; the close tooltip uses the shared real catalog.

Run localization resource, fallback, options, parsing, weak-subscription, and
STA UI-binding tests without accessing SQL:

```powershell
dotnet test .\TaskOTime.Localization.Tests\TaskOTime.Localization.Tests.csproj
dotnet test .\TaskOTime.Localization.Core.Tests\TaskOTime.Localization.Core.Tests.csproj
```

The second project compiles the actual provider, base classes, and representative
maintenance/booking models for plain `net10.0`, without WindowsDesktop references,
and exercises real satellite lookups, host-policy lifetime, and weak notifications.

Build on Windows using the .NET 10 SDK pinned by the repository's `global.json`.
The application uses SDK-style projects, production C#, and EF6 6.5.2 with SQL
Server; the collection tests remain in VB. LocalDB is required for local SQL
integration checks.

## Local demo

The default startup uses the named EF6 `TaskOTimeContext` connection in
`App.config`. It points to the existing `(localdb)\MSSQLLocalDB` database named
`TaskOTime`; the desktop neither creates nor regenerates that database.

From `src\TaskOTime`, using PowerShell on Windows:

```powershell
dotnet build .\TaskOTime.App\TaskOTime.App.csproj
& .\TaskOTime.App\bin\Debug\net10.0-windows\TaskOTime.App.exe
```

Generated local data uses the accounts configured in
`TaskOTime.Cli\demodataconfig.json`, including `AdminDE`, `LisaDE`, `AdminNL`,
and `DaanNL`. Use the configured preliminary password. Generated accounts must
replace it on first login before the main window opens. Incorrect credentials
are rejected, and five failed attempts lock an account for 15 minutes.

Bookings, tasks, tenant/project/collaboration edits, user operations, and password changes use the SQL services
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

Collaboration quick-create validates the existing SQL field limits: category names
up to 50 characters, tags up to 30, and note text up to 4,000. A note's required
mnemonic is derived from its first line, limited to 100 UTF-16 code units without
splitting a surrogate pair; its full text is preserved. Web links must be absolute,
well-formed HTTP/HTTPS URLs without embedded credentials. Their normalized URL is
limited to 2,000 characters and host to 200; the required domain comes from the URL
host, and a display title is derived from the first 100 code units. Invalid input
is reported without clearing the draft or invoking a mutation.

Maintenance mutation commands require an active, non-deleted administrator.
Server-side Main Data authorization remains authoritative. Create-user fields
include a temporary password, which is cleared after successful creation; the
existing service still enforces initial-password replacement on login.

Tenant name/active edits use `IAdminMainDataService.UpdateTenant` and persist in
the existing EF6 Tenant entity. The dedicated request contains only the tenant
and acting-user IDs, name, and active flag; metadata and other fields are preserved.
The service requires an active, non-deleted administrator of that tenant, rejects
deleted tenants, and validates required names, the 200-character schema limit, and
duplicate names. The workspace adopts the service result, and reopening it reads
the tenant through the authorized `GetTenant` API rather than a cached DTO.

An active administrator may reactivate an inactive tenant using this dedicated
operation. Recreating the maintenance model for an existing administrator first
reads the authorized tenant state, even if its caller supplies a stale active DTO.
An inactive tenant opens on the tenant tab with no normal Main Data collections
loaded; users remain readable, but only tenant updates are enabled. Normal Main
Data authorization is not relaxed. Reactivation reloads all collections before
enabling normal mutations. If reload fails, the saved tenant remains visible,
the error is reported, and another tenant save retries the load.

Closing maintenance with the tenant still inactive ends the desktop session
instead of attempting further project/booking queries for that tenant.
Deactivation requires confirmation explaining that the change is persistent,
blocks sign-in, and ends the session when maintenance closes. Reactivation remains
available to authorized administrators before closing or through the tenant
administration API; ordinary inactive-tenant login is not a recovery path.
Authentication and both password-change operations return `TenantInactive` for
inactive, deleted, or unavailable tenants without changing user login/password
state. Active-tenant login and forced initial-password replacement remain intact.
This branch
composes maintenance in `MainWindow.OnMainDataRequested` using `TenantFor` and the
`MainDataViewModel` service constructor; it has no separate desktop factory helper.

Application API types use `MainData` (for example `IAdminMainDataService`).
SQL table names, EF mappings, and persisted identifiers are unchanged.

## Development checks

```powershell
dotnet build .\TaskOTime.slnx
dotnet test .\TaskOTime.TimeTrackingServices.Tests\TaskOTime.TimeTrackingServices.Tests.vbproj
dotnet test .\TaskOTime.AppServer.Tests\TaskOTime.AppServer.Tests.csproj
dotnet test .\TaskOTime.AppServer.IntegrationTests\TaskOTime.AppServer.IntegrationTests.csproj
```

`MaintenanceViewModelTests` exercise
create/save/archive/delete, selection, service failures, request scope,
administrator gating, and temporary-password handling without creating views.
The owned-GUID SQL suite also executes the tenant editor against EF6, verifies
deactivation/reactivation and reload, and rejects unauthorized or invalid updates.
It references the production service, data-layer, and ViewModel assemblies rather
than recompiling a partial copy of their sources, so localization and other
cross-layer dependencies are exercised as deployed. Each SQL fixture creates and
removes only its own database; it never resets the shared demo database.

Build and publish deploy the model's CSDL, SSDL, and MSL files alongside the
application through the shared metadata target.

`VerifyDesktop.ps1` is retained as a legacy .NET Framework construction probe,
not a .NET 10 acceptance command. Windows PowerShell cannot load the modern
application assemblies. Desktop automation requires a .NET 10 Windows STA host
and an explicitly isolated database; do not run the legacy probe against shared
demo data. Unit and SQL tests alone do not establish desktop startup acceptance.
