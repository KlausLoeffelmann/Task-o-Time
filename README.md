# Task-o-Time candidate starting point (S3)

This branch is the **S3** starting point: production projects are SDK-style C#
projects targeting .NET 10, with Windows targets where WPF requires them.
Visual Basic remains only in candidate-visible tests. Read
[Candidate-Prompt.md](Candidate-Prompt.md) before changing the solution; it
defines the requested outcome and the boundaries of the exercise.

## Prerequisites

- Windows with PowerShell.
- The .NET 10 SDK selected by `global.json`.
- SQL Server Express LocalDB (`(localdb)\MSSQLLocalDB`) for the local demo.

Task-o-Time itself requires no product key, license registration, or external
service account. Do not commit database credentials.

## Restore and build

From the repository root:

```powershell
dotnet restore .\src\TaskOTime\TaskOTime.slnx
dotnet build .\src\TaskOTime\TaskOTime.slnx --no-restore
```

The solution includes the desktop application, SQL/EF6 services, CLI, and the
candidate-visible test projects.

## Create the local demo database

The desktop uses the named EF6 `TaskOTimeContext` connection from
`src\TaskOTime\TaskOTime.App\App.config`. In the default Demo mode it connects
to the LocalDB database `TaskOTime`; the desktop does not create that database.

From `src\TaskOTime`, generate and validate a fresh demo database:

```powershell
dotnet build .\TaskOTime.Cli\TaskOTime.Cli.csproj
& .\TaskOTime.Cli\bin\Debug\TaskOTime.Cli.exe --new --createDemoData --validateDemoData
```

`--new` drops and recreates the `TaskOTime` database. Back up anything that
must be retained before running it. Demo generation is configured in
`TaskOTime.Cli\demodataconfig.json`.

## Start and sign in

From `src\TaskOTime`:

```powershell
dotnet build .\TaskOTime.App\TaskOTime.App.csproj
& .\TaskOTime.App\bin\Debug\net10.0-windows\TaskOTime.App.exe
```

The generated users are `AdminDE`, `LisaDE`, `AdminNL`, and `DaanNL`. Their
initial password is `P@$$w0rd`. On first login, replace that preliminary
password before the main window opens. Five failed attempts lock the account
for 15 minutes.

## Use an existing SQL Server database

Set `TASKOTIME_MODE=Production` and set `TASKOTIME_CONNECTION_STRING` to an EF6
entity connection, `name=...`, or a SQL Server provider connection string for
an already provisioned Task-o-Time database. Provider strings reuse model
metadata from `TaskOTimeContext`. Production mode fails explicitly when the
connection or authentication is invalid; it does not fall back to demo data.

The production database must already contain the schema, system-marker
categories, an active administrator, and a tenant with projects and categories.
See [the desktop guide](src/TaskOTime/TaskOTime.App/README.md) for application
behavior, demo-data options, and development checks.
