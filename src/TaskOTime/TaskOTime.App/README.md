# Task-o-Time desktop

The Windows desktop application provides time bookings, task recording, and
master-data maintenance through the **Stammdaten** menu.

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

Bookings, tasks, master-data edits, and password changes use the SQL services
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
the current master-data API requires administrative authorization. Database
provisioning and system-marker categories must already be configured. The
desktop does not create the database or seed production users.

## Recording time

Choose a date to view bookings. The booking editor accepts a time, description,
project, and a category selected from SQL master data. The toolbar provides
pause, downtime, errand, stop, and checkout actions. Tasks can be started and
completed; manual completion uses the start and duration fields. Creating a new
activity or interruption ends the current task interval at that booking's
timestamp.

Downtime is represented by `EventInfo=DownTime` with the non-working stop
category. It survives timeline normalization and is excluded from booked work.
Errands use `EventInfo=Errand` and the same non-working category while remaining
continuous timeline markers rather than stop marks. Edits select their existing
category, and refreshed master data updates the category list. Service-returned
booking days are authoritative.

## Development checks

```powershell
dotnet build .\TaskOTime.slnx
dotnet test .\TaskOTime.TimeTrackingServices.Tests\TaskOTime.TimeTrackingServices.Tests.vbproj
dotnet test .\TaskOTime.AppServer.Tests\TaskOTime.AppServer.Tests.csproj
dotnet test .\TaskOTime.AppServer.IntegrationTests\TaskOTime.AppServer.IntegrationTests.csproj
```

SQL integration tests create GUID-named databases and remove only databases
whose ownership they established; they do not reset the shared demo database.
Build and publish deploy the model's CSDL, SSDL, and MSL files alongside the
application through the shared metadata target.

`VerifyDesktop.ps1` is retained as a legacy .NET Framework construction probe,
not a .NET 10 acceptance command. Windows PowerShell cannot load the modern
application assemblies. Desktop automation requires a .NET 10 Windows STA host
and an explicitly isolated database; do not run the legacy probe against shared
demo data. Unit and SQL tests alone do not establish desktop startup acceptance.
