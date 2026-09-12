# Desktop theme resources (.NET Framework 4.7.2)

`ClassicDark.xaml` remains the application resource entry point. It merges the
dark fallback palette, shared controls, and calendar styles. Do not merge a
second copy into child views: application resources should own the styles.

## Lifetime and switching

`App.OnStartup` stores `ThemeService.Start(this)` before opening login or desktop
windows, and `App.OnExit` disposes it. Saved culture setup remains before
`base.OnStartup`. The service lives in
`TaskOTime.App.Themes`; `SetTheme(AppTheme.System | Dark | Light | HighContrast)`
changes only this application's resources. `System` is the initial preference.
`SelectedTheme` records that preference; `EffectiveTheme` reports the palette
actually used. Operating-system high contrast always takes precedence.
`ThemeChanged` is raised on the supplied UI dispatcher.

The service reads Windows app-color preferences and observes
`SystemEvents.UserPreferenceChanged` and `SystemParameters.StaticPropertyChanged`.
It never writes Windows settings. High-contrast brushes dynamically reference
`SystemColors` so the user's chosen contrast colors remain authoritative.
Dispose unsubscribes the system event handlers and leaves the last palette in
place. One service should own an application's palette.

Tests can construct `ThemeService(resources, dispatcher, IThemeEnvironment)`
with a read-only preference provider. The caller owns an injected provider.
The service replaces only its own merged palette, not strings or other resources.

## Public brush pairs

Use **DynamicResource** references, not copied brushes or literal colors.

| Surface/state | Foreground | Background |
| --- | --- | --- |
| Window | `ContentForegroundBrush` | `WindowBackgroundBrush` |
| Panels and reading labels | `ContentForegroundBrush` | `PanelBackgroundBrush` |
| Editors and calendar dates | `ContentForegroundBrush` | `ContentBackgroundBrush` |
| Lists | `ListItemForegroundBrush` | `ListItemBackgroundBrush` |
| Selected dates/items | `SelectedForegroundBrush` | `ListItemSelectedBackgroundBrush` |
| Hover | `HoverForegroundBrush` | `HoverBackgroundBrush` |
| Disabled | `DisabledForegroundBrush` | `DisabledBackgroundBrush` |
| Inactive dates/secondary text | `MutedForegroundBrush` | `ContentBackgroundBrush` |
| Menus/tooltips | `MenuForegroundBrush` | `MenuPopupBackgroundBrush` |

Other public brushes: `PanelBorderBrush`, `ListItemSelectedBorderBrush`,
`AccentBrush`, `FocusBrush`, `RecordingForegroundBrush`, and `CommandBackgroundBrush`.
The legacy `*Color` entries remain palette implementation details; consume the
brush keys above to obtain high-contrast system overrides.

## Public styles and integration

- `PanelBorderStyle`, `SectionHeaderTextStyle`, `ClassicButtonStyle`,
  `ClassicToolBarStyle` retain the previous entry-point keys.
- `MainDataControlStyle`, `MainDataTextStyle`, `MainDataLabelStyle`,
  `MainDataWindowStyle`, `MainDataViewStyle`, `MainDataHeadingTextStyle`,
  `MainDataHeadingLabelStyle`, `MainDataTextBoxStyle`, `MainDataPasswordBoxStyle`,
  `MainDataButtonStyle`, `MainDataCheckBoxStyle`,
  `MainDataComboBoxStyle`, `MainDataComboBoxItemStyle`,
  `MainDataListBoxStyle`, `MainDataListBoxItemStyle`,
  `MainDataListViewStyle`, `MainDataListViewItemStyle`, `MainDataGridViewColumnHeaderStyle`,
  `MainDataTabControlStyle`, `MainDataTabItemStyle`.
- `ThemedCalendarStyle`, `ThemedCalendarItemStyle`,
  `ThemedCalendarDayButtonStyle`, `ThemedCalendarButtonStyle`,
  `ThemedCalendarNavigationButtonStyle`, `ThemeFocusVisual`.
- Existing implicit TextBox, StatusBar, ContextMenu, and ToolTip styling remains.
  Other controls opt in via the named styles.

The maintenance window and its Project, Tenant/User, Task, and Collaboration
views now use these shared styles without local color or font-family overrides.
Their bindings, commands, password adapter, and control structures are unchanged.
GridView headers retain their resize gripper and floating-header canvas.
MainWindow uses `ThemedCalendarStyle`, retains its two-way date binding, and uses
dynamic palette brush references throughout its local resources and content.
Its old system-color overrides and private calendar templates are removed.
Recording indicators pulse opacity over a palette brush rather than animating
hardcoded colors over a user's high-contrast scheme.

For other views, remove local hardcoded colors, initial code-behind color
assignments, and local calendar templates that override these resources.
Apply `Style="{DynamicResource ThemedCalendarStyle}"` to the calendar and retain
its existing `SelectedDate` binding (and any range, culture, or selection-mode
settings). The style does not assign dates or replace the calendar control.
Month/year/decade grids, navigation parts, blackout markers, date automation,
today highlighting, and keyboard focus indicators are retained.

Use `MainDataTextStyle` for reading text on panels, not text inside selected
list/tab/button content. Content inside stateful controls should inherit
`Foreground` from its container rather than applying a local color. Apply a
matching foreground and background together; an inherited light foreground
on an unstyled native light surface is not a themed reading surface.
The list-view item style supports plain items and GridView rows. Set
`GridView.ColumnHeaderContainerStyle` to `MainDataGridViewColumnHeaderStyle`.
Applications adding a custom ViewBase must theme its custom surfaces explicitly.

`TaskOTime.Theme.TestHost` contains the real WPF scenarios. They
instantiate production templates, exercise two-way date selection and navigation,
resolve state brushes, check contrast, edit text, open a combo popup, switch tabs,
and change live process-local high-contrast resources. Mouse/focus state triggers
are driven through WPF dependency-property keys to avoid moving the user's cursor
or keyboard focus. They do not change machine settings. An additional scenario
hosts the real MainDataWindow with production ViewModels and test service data,
visits all maintenance tabs in each palette, and checks rendered text against its
painted background, disabled/focused editors, GridView column resizing, password
editing, project selection/edit bindings, and the task-project combo popup.
Another scenario constructs the real MainWindow with injected service data and
checks its calendar binding, live palette changes, menu/button states, glyph
foregrounds, and recording-indicator contrast at minimum pulse opacity.
The application-lifetime scenario calls `ThemeService.Start`, switches palettes,
and disposes the service on an application resource scope. These tests do not
connect to SQL or automate production authentication.

## Strict process test protocol

`TaskOTime.Theme.Tests` starts a fresh executable for each UI scenario:

```powershell
dotnet test .\TaskOTime.Theme.Tests\TaskOTime.Theme.Tests.vbproj
.\TaskOTime.Theme.TestHost\bin\Debug\net472\TaskOTime.Theme.TestHost.exe --case main-window
```

Accepted cases are `calendar-states`, `control-states`, `list-tab-states`,
`runtime-preferences`, `maintenance-views`, `main-window`, and
`application-lifetime`. `self-test-failure` deliberately fails an assertion for
the runner's error-path check. Missing, unknown, or additional arguments return
exit code 64. Failed scenarios return 1.

Each host owns its main STA thread and dispatcher, closes its windows, and
completes dispatcher shutdown before printing `THEME-CASE-PASS:<case>`. The runner
waits for process termination and requires all three conditions: exit code 0,
empty stderr, and the exact success marker. A late CLR/COM shutdown failure cannot
be counted as a passing UI case. Separate tests verify invalid commands, failed
assertions, and rejection of a success marker followed by a failed or dirty exit.

There are no AppDomain/remoting APIs or background STA lifetime workarounds.
The console apphost, process APIs, and STA dispatcher are supported by Framework
and modern Windows .NET. Both projects retain the current net472 target and
existing package versions; actual .NET 10 retargeting remains on its separate
branch. The build copies the host's own output directory, including satellite
resources and (after retargeting) apphost/runtime files, without assuming that
runner and host framework folder names match.
