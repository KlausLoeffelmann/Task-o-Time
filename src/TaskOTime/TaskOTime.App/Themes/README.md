# Desktop theme resources (.NET Framework 4.7.2)

`ClassicDark.xaml` remains the application resource entry point. It merges the
dark fallback palette, shared controls, and calendar styles. Do not merge a
second copy into child views: application resources should own the styles.

## Lifetime and switching

The startup owner should store `ThemeService.Start(this)` before opening any
windows and dispose it in `OnExit`. The service lives in
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
`AccentBrush`, `FocusBrush`, and `CommandBackgroundBrush`.
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

`TaskOTime.Theme.Tests` contains `ThemeResourceTests`. Its STA tests
instantiate production templates, exercise two-way date selection and navigation,
resolve state brushes, check contrast, edit text, open a combo popup, switch tabs,
and change live process-local high-contrast resources. Mouse/focus state triggers
are driven through WPF dependency-property keys to avoid moving the user's cursor
or keyboard focus. They do not change machine settings. An additional scenario
hosts the real MainDataWindow with production ViewModels and test service data,
visits all maintenance tabs in each palette, and checks rendered text against its
painted background, disabled/focused editors, GridView column resizing, password
editing, project selection/edit bindings, and the task-project combo popup.
MainWindow calendar/startup wiring and visual acceptance remain separate
integration checks owned by the main-window/startup workstream.

Theme tests run in their own test project/host because the older application
binding test permanently shuts down WPF `Application`. This avoids shared
application lifetime and cross-AppDomain COM input-service teardown problems.
Every scenario closes its windows and shuts down its STA dispatcher. The project
uses the same Framework target and package versions as the existing test suite.
