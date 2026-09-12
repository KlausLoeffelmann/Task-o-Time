# Opt-in WPF visual capture

This standalone, read-only application viewer renders the **actual ProjectView
and calendar templates from an explicitly supplied application build**. It does
not reference or rebuild the production projects, load private validation
drivers, open a database, authenticate, or execute application commands.

The output is for human visual inspection, not a substitute for the strict
state/binding tests. Project data is synthetic and is identified as such in the
manifest. Product labels and localization come from the selected build; this
viewer does not replace missing translations with its own labels.

## Framework build and capture

From the repository root:

```powershell
dotnet build .\src\TaskOTime\TaskOTime.Theme.CaptureHost\TaskOTime.Theme.CaptureHost.csproj -c Release

& .\src\TaskOTime\TaskOTime.Theme.CaptureHost\bin\Release\net472\TaskOTime.Theme.CaptureHost.exe `
    --capture `
    --application-root "C:\builds\integrated\TaskOTime.App\bin\Release\net472" `
    --output "C:\captures\golden-visual-qc"
```

The argument order is strict. Both paths must be absolute. The application root
must contain `TaskOTime.App.exe` (Framework) or `TaskOTime.App.dll` (modern .NET),
`TaskOTime.ViewModel.dll`, their dependencies, and culture subdirectories.
The output directory **must not exist**, and its parent must already exist.
There is no default output location or overwrite mode. Directory creation is
exclusive, and files use `CreateNew`; unrelated existing files are never reused.

## Final integrated .NET 10 build

Build this project alone for the runtime of the target application:

```powershell
dotnet build .\src\TaskOTime\TaskOTime.Theme.CaptureHost\TaskOTime.Theme.CaptureHost.csproj `
    -c Release -p:CaptureTargetFramework=net10.0-windows

& .\src\TaskOTime\TaskOTime.Theme.CaptureHost\bin\Release\net10.0-windows\TaskOTime.Theme.CaptureHost.exe `
    --capture `
    --application-root "C:\builds\integrated\TaskOTime.App\bin\Release\net10.0-windows" `
    --output "C:\captures\golden-net10-visual-qc"
```

`CaptureTargetFramework` changes only this host. There are no project references
to the older branch's application or private helpers. The runtime resolver loads
the explicitly selected build and its satellites; a bad or incompatible target
fails rather than silently using host-side production assemblies. Use a host
built for the application's runtime.
When building/running `TaskOTime.Theme.Tests` against a retargeted application,
also pass `-p:CaptureTargetFramework=net10.0-windows` so its copied capture host
uses the matching runtime.

## Output and interpretation

A successful capture creates **30 PNGs and `capture.xml`**:

- Light, Dark, and HighContrast palettes.
- en-US and de-DE cultures.
- Calendar: selected date, pending June 15-18 range, and disabled selection.
  All frames include the weekday row and a blackout-date marker.
- Project Main Data: normal/selected content and disabled controls.

Calendar images are 380x330 and Project images 1060x660, rendered at 96 DPI.
Names identify palette, culture, surface, and state, for example
`highcontrast-de-DE-calendar-pending-range.png`. The XML manifest records the
application root, loaded App/ViewModel MVIDs, host runtime/framework, image
dimensions, and use of synthetic data.

High contrast uses the target's HighContrast dictionary with black/white/yellow
`SystemColors` overrides **only in this process's application resources**.
Culture changes are also process-local. Rendering uses off-screen, nonactivating
WPF windows and `RenderTargetBitmap` on the owned control tree, not desktop
capture. Software rendering and WPF's no-display-device rendering switch are
enabled only in the host process, including disconnected Windows sessions.
Blank/unrendered bitmaps fail the run rather than producing a success marker.
The host also checks seven visible localized weekday headings, native pending
range/selection/disabled flags, and ProjectView's sample-data bindings before
encoding images; these checks do not replace visual review of the captured pixels.
No mouse movement, keyboard injection, OS theme changes, or product
settings writes are performed. Pending dates are computed by WPF from its native
hover endpoints, not painted or substituted by this viewer.

Review weekday labels, selection/range boundaries, disabled readability, editor
and list foreground/background pairs, clipping, and translated captions in the
PNGs. Report any issue against the manifest's target build; the viewer does not
repair production UI.

Success requires process exit **0**, empty stderr, and exactly
`THEME-CAPTURE-PASS:<absolute-output-directory>` on stdout after STA shutdown.
Invalid arguments return 64; capture/runtime failures return 1. Failed runs may
leave partial images in their newly claimed output directory, but never produce
the success marker. The existing strict Theme.TestHost case protocol is unchanged.
