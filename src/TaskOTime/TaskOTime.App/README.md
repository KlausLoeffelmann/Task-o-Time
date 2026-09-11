# Task-o-Time desktop

The Windows desktop application provides time bookings, task recording, and
master-data maintenance through the **Stammdaten** menu.

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
powershell.exe -NoProfile -Sta -File .\TaskOTime.App\VerifyDesktop.ps1
```

The desktop smoke uses the generated local SQL credentials, rolls back its
authentication and booking probes, loads service-backed categories, constructs
the windows and resources, and verifies the time list's collection source.
The STA desktop test also inventories menu, command-strip, time-panel, and
direct master-data handlers at runtime.
