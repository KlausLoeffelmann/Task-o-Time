Imports System
Imports System.Collections.Generic
Imports CultureInfo = System.Globalization.CultureInfo
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Data
Imports System.Windows.Media
Imports System.Windows.Markup
Imports System.Windows.Shapes
Imports System.Windows.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.App.Themes
Imports TaskOTime.App
Imports TaskOTime.TimeTrackingServices.Tests.Doubles
Imports TaskOTime.ViewModel.ViewModels
Imports TaskOTime.ViewModel.Views

Namespace TaskOTime.Theme.TestHost
    Public Class ThemeScenarios
        Public Sub CalendarPalettes_ResolveStateBrushesAndPreserveSelectionNavigationAndBlackout()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        Dim source As New Calendar With {.SelectedDate = New DateTime(2026, 6, 15)}
                        Dim calendar As New Calendar With {.DisplayDate = source.SelectedDate.Value}
                        host.Panel.Children.Add(calendar)
                        calendar.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCalendarStyle")
                        calendar.SetBinding(Calendar.SelectedDateProperty, New Binding("SelectedDate") With {
                            .Source = source, .Mode = BindingMode.TwoWay})
                        calendar.BlackoutDates.Add(New CalendarDateRange(New DateTime(2026, 6, 10)))
                        host.UseContrastScheme()
                        host.Show()

                        For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast}
                            host.Service.SetTheme(palette)
                            calendar.DisplayMode = CalendarMode.Month
                            calendar.DisplayDate = New DateTime(2026, 6, 15)
                            calendar.SelectedDate = New DateTime(2026, 6, 15)
                            host.Layout()
                            Dim days = Descendants(Of CalendarDayButton)(calendar).ToList()
                            Assert.AreEqual(42, days.Count, "The six-week calendar grid must remain populated.")
                            Dim selected = days.Single(Function(day) day.IsSelected)
                            AssertPair(selected, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            Dim normal = days.First(Function(day) Not day.IsInactive AndAlso Not day.IsSelected AndAlso Not day.IsBlackedOut)
                            AssertPair(normal, "ContentForegroundBrush", "ContentBackgroundBrush")
                            Dim inactive = days.First(Function(day) day.IsInactive)
                            AssertPair(inactive, "MutedForegroundBrush", "ContentBackgroundBrush")
                            Assert.AreEqual(FontStyles.Italic, inactive.FontStyle)
                            Dim blackout = days.Single(Function(day) day.IsBlackedOut)
                            Assert.AreEqual(Visibility.Visible,
                                DirectCast(blackout.Template.FindName("BlackoutMark", blackout), Path).Visibility)

                            SetState(normal, "IsMouseOver", True)
                            AssertPair(normal, "HoverForegroundBrush", "HoverBackgroundBrush")
                            SetState(normal, "IsMouseOver", False)
                            SetState(selected, "IsMouseOver", True)
                            AssertPair(selected, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            SetState(selected, "IsMouseOver", False)
                            SetState(selected, "IsKeyboardFocused", True)
                            Dim focus = DirectCast(selected.Template.FindName("FocusMark", selected), Rectangle)
                            Assert.AreEqual(Visibility.Visible, focus.Visibility)
                            Assert.IsTrue(Contrast(focus.Stroke, selected.Background) >= 4.5)
                            SetState(selected, "IsKeyboardFocused", False)

                            normal.IsEnabled = False
                            AssertPair(normal, "DisabledForegroundBrush", "DisabledBackgroundBrush")
                            Assert.AreEqual(1.0, normal.Opacity, "Disabled content must not be faded into an unreadable background.")
                            normal.IsEnabled = True
                            Dim peer = UIElementAutomationPeer.CreatePeerForElement(normal)
                            Assert.IsNotNull(peer, "Calendar date automation must survive retemplating.")
                            Assert.IsFalse(String.IsNullOrWhiteSpace(peer.GetName()))
                            Dim datePeer = New CalendarAutomationPeer(calendar).GetChildren().
                                FirstOrDefault(Function(child) child.GetName() = DirectCast(normal.DataContext, DateTime).ToString("D") AndAlso
                                                   child.GetPattern(PatternInterface.SelectionItem) IsNot Nothing)
                            Assert.IsNotNull(datePeer, "Calendar must expose selectable date automation peers.")
                            DirectCast(datePeer.GetPattern(PatternInterface.SelectionItem), ISelectionItemProvider).Select()
                            Assert.AreEqual(DirectCast(normal.DataContext, DateTime), calendar.SelectedDate.Value)
                            Assert.AreEqual(calendar.SelectedDate, source.SelectedDate, "Two-way date bindings must be preserved.")

                            Dim item = Descendants(Of CalendarItem)(calendar).Single()
                            Dim nextButton = DirectCast(item.Template.FindName("PART_NextButton", item), Button)
                            Assert.IsTrue(UIElementAutomationPeer.CreatePeerForElement(nextButton).GetName().Length > 1,
                                          "Navigation must retain the framework's localized automation name.")
                            nextButton.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
                            Assert.AreEqual(7, calendar.DisplayDate.Month, "Next-month navigation must remain wired.")
                            Dim previousButton = DirectCast(item.Template.FindName("PART_PreviousButton", item), Button)
                            previousButton.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
                            Assert.AreEqual(6, calendar.DisplayDate.Month)
                            Dim header = DirectCast(item.Template.FindName("PART_HeaderButton", item), Button)
                            header.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
                            Assert.AreEqual(CalendarMode.Year, calendar.DisplayMode)
                            host.Layout()
                            Dim months = Descendants(Of CalendarButton)(calendar).Where(Function(button) button.IsVisible).ToList()
                            Assert.AreEqual(12, months.Count)
                            Dim selectedMonth = months.Single(Function(button) button.HasSelectedDays)
                            AssertPair(selectedMonth, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            Dim normalMonth = months.First(Function(button) Not button.HasSelectedDays)
                            AssertPair(normalMonth, "ContentForegroundBrush", "ContentBackgroundBrush")
                            SetState(normalMonth, "IsMouseOver", True)
                            AssertPair(normalMonth, "HoverForegroundBrush", "HoverBackgroundBrush")
                            SetState(normalMonth, "IsMouseOver", False)
                            SetState(selectedMonth, "IsKeyboardFocused", True)
                            Assert.AreEqual(Visibility.Visible,
                                DirectCast(selectedMonth.Template.FindName("FocusMark", selectedMonth), Rectangle).Visibility)
                            SetState(selectedMonth, "IsKeyboardFocused", False)
                            selectedMonth.IsEnabled = False
                            AssertPair(selectedMonth, "DisabledForegroundBrush", "DisabledBackgroundBrush")
                            selectedMonth.IsEnabled = True
                            header.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
                            Assert.AreEqual(CalendarMode.Decade, calendar.DisplayMode)
                            host.Layout()
                            Dim outsideYear = Descendants(Of CalendarButton)(calendar).First(Function(button) button.IsInactive)
                            AssertPair(outsideYear, "MutedForegroundBrush", "ContentBackgroundBrush")
                        Next
                        calendar.DisplayMode = CalendarMode.Month
                        calendar.DisplayDate = DateTime.Today
                        host.Layout()
                        Dim today = Descendants(Of CalendarDayButton)(calendar).Single(Function(day) day.IsToday)
                        Assert.AreEqual(FontWeights.Bold, today.FontWeight)
                        calendar.IsTodayHighlighted = False
                        host.Layout()
                        Assert.AreEqual(FontWeights.Normal, today.FontWeight)
                        calendar.IsEnabled = False
                        AssertPair(today, "DisabledForegroundBrush", "DisabledBackgroundBrush")
                    End Using
                End Sub)
        End Sub

        Public Sub CalendarWeekdayHeaders_RenderSevenLocalizedLabelsAcrossPalettes()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        host.UseContrastScheme()
                        host.Show()
                        For Each cultureName In {"en-US", "de-DE", "nl-NL", "es-ES"}
                            Dim culture = CultureInfo.GetCultureInfo(cultureName)
                            Dim calendar As New Calendar With {
                                .DisplayDate = New DateTime(2026, 6, 15),
                                .Language = XmlLanguage.GetLanguage(cultureName),
                                .FirstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek}
                            host.Panel.Children.Add(calendar)
                            calendar.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCalendarStyle")
                            For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast}
                                host.Service.SetTheme(palette)
                                host.Layout()
                                Dim item = Descendants(Of CalendarItem)(calendar).Single()
                                Dim month = DirectCast(item.Template.FindName("PART_MonthView", item), Grid)
                                Dim headers = month.Children.Cast(Of FrameworkElement)().
                                    Where(Function(child) Grid.GetRow(child) = 0).
                                    OrderBy(Function(child) Grid.GetColumn(child)).ToList()
                                Assert.AreEqual(7, headers.Count)
                                For column = 0 To 6
                                    Dim header = headers(column)
                                    Dim labels = If(TypeOf header Is TextBlock,
                                        New List(Of TextBlock) From {DirectCast(header, TextBlock)},
                                        Descendants(Of TextBlock)(header).ToList())
                                    Assert.AreEqual(1, labels.Count, cultureName & ": missing rendered weekday in column " & column)
                                    Dim label = labels.Single()
                                    Dim dayIndex = (CInt(calendar.FirstDayOfWeek) + column) Mod 7
                                    Assert.AreEqual(culture.DateTimeFormat.ShortestDayNames(dayIndex), label.Text, cultureName)
                                    Assert.IsTrue(label.IsVisible AndAlso label.ActualWidth > 0 AndAlso label.ActualHeight > 0)
                                    AssertBrush(label.Foreground, calendar.FindResource("ContentForegroundBrush"))
                                    Assert.IsTrue(Contrast(label.Foreground, PaintedBackground(label)) >= 4.5)
                                Next
                            Next
                            host.Panel.Children.Remove(calendar)
                        Next
                    End Using
                End Sub)
        End Sub

        Public Sub CalendarRangePreview_UsesNativePendingHighlightBeforeCommit()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        Dim first = New DateTime(2026, 6, 15)
                        Dim last = first.AddDays(3)
                        Dim calendar As New Calendar With {
                            .DisplayDate = first, .SelectionMode = CalendarSelectionMode.SingleRange}
                        host.Panel.Children.Add(calendar)
                        calendar.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCalendarStyle")
                        host.UseContrastScheme()
                        host.Show()
                        For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast}
                            host.Service.SetTheme(palette)
                            For Each reverse In {False, True}
                                calendar.SelectedDates.Clear()
                                SetNativeHoverRange(calendar, If(reverse, last, first), If(reverse, first, last))
                                host.Layout()
                                Assert.AreEqual(0, calendar.SelectedDates.Count, "Preview must not commit the selected range.")
                                Dim preview = Descendants(Of CalendarDayButton)(calendar).
                                    Where(Function(day) day.IsHighlighted).ToList()
                                Assert.AreEqual(4, preview.Count, "Native range preview must include both endpoints.")
                                For Each day As CalendarDayButton In preview
                                    Assert.IsFalse(day.IsSelected, "This must exercise pending, not committed, selection.")
                                    Assert.IsTrue(DirectCast(day.DataContext, DateTime) >= first AndAlso
                                                  DirectCast(day.DataContext, DateTime) <= last)
                                    AssertPreviewPair(day, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                                Next
                                Dim hovered = preview.Last()
                                SetState(hovered, "IsMouseOver", True)
                                AssertPreviewPair(hovered, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                                hovered.IsEnabled = False
                                AssertPreviewPair(hovered, "DisabledForegroundBrush", "DisabledBackgroundBrush")
                                hovered.IsEnabled = True
                                SetState(hovered, "IsMouseOver", False)
                                AssertPreviewPair(hovered, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                                calendar.SelectedDates.AddRange(first, last)
                                SetNativeHoverRange(calendar, Nothing, Nothing)
                                host.Layout()
                                Assert.AreEqual(4, calendar.SelectedDates.Count)
                                For Each day As CalendarDayButton In preview
                                    Assert.IsTrue(day.IsSelected)
                                    Assert.IsFalse(day.IsHighlighted)
                                    AssertPreviewPair(day, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                                Next
                            Next
                        Next
                    End Using
                End Sub)
        End Sub

        Private Shared Sub SetNativeHoverRange(calendar As Calendar, first As DateTime?, last As DateTime?)
            ' Seed the same pending range as native drag handling, without capturing the user's mouse.
            ' WPF computes IsHighlighted on its generated buttons; the test never assigns that state.
            Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
            Dim startProperty = GetType(Calendar).GetProperty("HoverStart", flags)
            Dim endProperty = GetType(Calendar).GetProperty("HoverEnd", flags)
            Dim refresh = GetType(Calendar).GetMethod("UpdateCellItems", flags)
            Assert.IsNotNull(startProperty)
            Assert.IsNotNull(endProperty)
            Assert.IsNotNull(refresh)
            startProperty.SetValue(calendar, first)
            endProperty.SetValue(calendar, last)
            refresh.Invoke(calendar, Nothing)
        End Sub

        Private Shared Sub AssertPreviewPair(day As CalendarDayButton, foreground As String, background As String)
            AssertPair(day, foreground, background)
            Dim border = DirectCast(day.Template.FindName("DayBorder", day), Border)
            AssertBrush(border.Background, day.FindResource(background))
            Dim label = Descendants(Of TextBlock)(day).Single()
            AssertBrush(label.Foreground, day.FindResource(foreground))
            Assert.IsTrue(Contrast(label.Foreground, border.Background) >= 4.5)
        End Sub

        Public Sub MainDataStyles_KeepReadingPairsFocusDisabledAndEditingBehaviorAcrossLivePalettes()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        Dim text As New TextBlock With {.Text = "Project"}
                        host.Panel.Children.Add(text)
                        text.SetResourceReference(FrameworkElement.StyleProperty, "MainDataTextStyle")
                        Dim controls As Control() = {
                            New Label With {.Content = "_Name"},
                            New TextBox With {.Text = "Project name"},
                            New Button With {.Content = "_Save"},
                            New CheckBox With {.Content = "_Active", .IsThreeState = True},
                            New ComboBox With {.ItemsSource = New String() {"Alpha", "Beta"}, .SelectedIndex = 0},
                            New ListBox With {.ItemsSource = New String() {"Alpha", "Beta"}, .SelectedIndex = 0}}
                        Dim keys = {"MainDataLabelStyle", "MainDataTextBoxStyle", "MainDataButtonStyle",
                                    "MainDataCheckBoxStyle", "MainDataComboBoxStyle", "MainDataListBoxStyle"}
                        For i = 0 To controls.Length - 1
                            host.Panel.Children.Add(controls(i))
                            controls(i).SetResourceReference(FrameworkElement.StyleProperty, keys(i))
                        Next
                        host.UseContrastScheme()
                        host.Show()
                        For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast, AppTheme.Dark}
                            host.Service.SetTheme(palette)
                            host.Layout()
                            AssertBrush(text.Foreground, text.FindResource("ContentForegroundBrush"))
                            Assert.IsTrue(Contrast(text.Foreground, text.Background) >= 4.5)
                            For Each control In controls
                                AssertBrush(control.Foreground, control.FindResource("ContentForegroundBrush"))
                                Assert.IsTrue(Contrast(control.Foreground, control.Background) >= 4.5, control.GetType().Name)
                                SetState(control, "IsKeyboardFocusWithin", True)
                                AssertBrush(control.BorderBrush, control.FindResource("FocusBrush"))
                                SetState(control, "IsKeyboardFocusWithin", False)
                                control.IsEnabled = False
                                AssertPair(control, "DisabledForegroundBrush", "DisabledBackgroundBrush")
                                control.IsEnabled = True
                            Next
                            Dim list = DirectCast(controls(5), ListBox)
                            Dim selected = DirectCast(list.ItemContainerGenerator.ContainerFromIndex(0), ListBoxItem)
                            AssertPair(selected, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            SetState(selected, "IsKeyboardFocusWithin", True)
                            Assert.IsTrue(Contrast(selected.BorderBrush, selected.Background) >= 4.5)
                            SetState(selected, "IsKeyboardFocusWithin", False)
                            Dim check = DirectCast(controls(3), CheckBox)
                            check.IsChecked = True
                            Assert.AreEqual(Visibility.Visible, DirectCast(check.Template.FindName("CheckMark", check), Path).Visibility)
                            check.IsChecked = Nothing
                            Assert.AreEqual(Visibility.Visible, DirectCast(check.Template.FindName("IndeterminateMark", check), Rectangle).Visibility)
                            Dim edit = DirectCast(controls(1), TextBox)
                            Assert.IsNotNull(edit.Template.FindName("PART_ContentHost", edit))
                            edit.SelectAll()
                            edit.SelectedText = "Changed"
                            Assert.AreEqual("Changed", edit.Text)
                            Dim combo = DirectCast(controls(4), ComboBox)
                            combo.IsEditable = True
                            host.Layout()
                            Dim comboEdit = DirectCast(combo.Template.FindName("PART_EditableTextBox", combo), TextBox)
                            Assert.IsTrue(comboEdit.IsVisible)
                            combo.Text = "Beta"
                            Assert.AreEqual("Beta", comboEdit.Text)
                            combo.IsDropDownOpen = True
                            host.Layout()
                            Dim popup = DirectCast(combo.Template.FindName("PART_Popup", combo), Popup)
                            Assert.IsTrue(popup.IsOpen)
                            Dim optionItem = DirectCast(combo.ItemContainerGenerator.ContainerFromIndex(1), ComboBoxItem)
                            Assert.IsNotNull(optionItem)
                            Assert.IsTrue(Contrast(optionItem.Foreground, optionItem.Background) >= 4.5)
                            combo.IsDropDownOpen = False
                        Next
                    End Using
                End Sub)
        End Sub

        Public Sub SharedListAndTabStyles_PreserveContentSelectionAndLiveContrastSchemeChanges()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        host.UseContrastScheme()
                        Dim list As New ListView With {.ItemsSource = New String() {"Alpha", "Beta"}, .SelectedIndex = 0}
                        host.Panel.Children.Add(list)
                        list.SetResourceReference(FrameworkElement.StyleProperty, "MainDataListViewStyle")
                        Dim firstContent As New TextBlock With {.Text = "First content"}
                        Dim secondContent As New TextBlock With {.Text = "Second content"}
                        Dim tabs As New TabControl()
                        host.Panel.Children.Add(tabs)
                        tabs.SetResourceReference(FrameworkElement.StyleProperty, "MainDataTabControlStyle")
                        tabs.Items.Add(New TabItem With {.Header = "_First", .Content = firstContent})
                        tabs.Items.Add(New TabItem With {.Header = "_Second", .Content = secondContent})
                        host.Show()
                        For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast}
                            host.Service.SetTheme(palette)
                            host.Layout()
                            Dim row = DirectCast(list.ItemContainerGenerator.ContainerFromIndex(0), ListViewItem)
                            AssertPair(row, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            Dim text = Descendants(Of TextBlock)(row).Single()
                            Assert.AreEqual("Alpha", text.Text)
                            AssertBrush(text.Foreground, row.Foreground)
                            Dim tab = DirectCast(tabs.Items(0), TabItem)
                            tabs.SelectedIndex = 0
                            host.Layout()
                            AssertPair(tab, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                            Assert.IsTrue(firstContent.IsVisible)
                            tabs.SelectedIndex = 1
                            host.Layout()
                            Assert.IsTrue(secondContent.IsVisible)
                            Assert.IsFalse(firstContent.IsVisible)
                            AssertPair(tab, "ContentForegroundBrush", "ContentBackgroundBrush")
                        Next
                        host.UseContrastScheme(True)
                        host.Layout()
                        Dim selectedRow = DirectCast(list.ItemContainerGenerator.ContainerFromIndex(0), ListViewItem)
                        AssertPair(selectedRow, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                        Assert.AreEqual(Colors.White, DirectCast(selectedRow.Foreground, SolidColorBrush).Color)
                        Assert.AreEqual(Colors.Black, DirectCast(selectedRow.Background, SolidColorBrush).Color)
                        Assert.AreEqual(Colors.White, DirectCast(list.Background, SolidColorBrush).Color)
                        Dim grid As New GridView()
                        grid.Columns.Add(New GridViewColumn With {.Header = "Name", .DisplayMemberBinding = New Binding()})
                        list.View = grid
                        host.Layout()
                        selectedRow = DirectCast(list.ItemContainerGenerator.ContainerFromIndex(0), ListViewItem)
                        Assert.IsTrue(Descendants(Of GridViewRowPresenter)(selectedRow).Single().IsVisible)
                    End Using
                End Sub)
        End Sub

        Public Sub ThemeService_ObservesProcessPreferencesOnDispatcherAndHighContrastAlwaysWins()
            OnSta(
                Sub()
                    Using host As New ThemeHost()
                        Dim label As New Label With {.Content = "Runtime"}
                        host.Panel.Children.Add(label)
                        label.SetResourceReference(FrameworkElement.StyleProperty, "MainDataLabelStyle")
                        host.Show()
                        Assert.AreEqual(AppTheme.Dark, host.Service.EffectiveTheme)
                        Dim original = DirectCast(label.Background, SolidColorBrush).Color
                        Dim notifications = 0
                        AddHandler host.Service.ThemeChanged,
                            Sub()
                                Assert.IsTrue(host.Window.Dispatcher.CheckAccess())
                                notifications += 1
                            End Sub
                        Dim worker As New Thread(Sub()
                                                     host.Environment.UseLightTheme = True
                                                     host.Environment.Notify()
                                                 End Sub)
                        worker.Start()
                        Assert.IsTrue(worker.Join(5000))
                        host.Layout()
                        Assert.AreEqual(AppTheme.Light, host.Service.EffectiveTheme)
                        Assert.AreNotEqual(original, DirectCast(label.Background, SolidColorBrush).Color)
                        Assert.AreEqual(1, notifications)
                        host.Environment.HighContrast = True
                        host.Environment.Notify()
                        host.Layout()
                        host.Service.SetTheme(AppTheme.Light)
                        Assert.AreEqual(AppTheme.HighContrast, host.Service.EffectiveTheme)
                        Assert.AreEqual(SystemColors.WindowColor, DirectCast(label.Background, SolidColorBrush).Color)
                        Assert.AreEqual(SystemColors.WindowTextColor, DirectCast(label.Foreground, SolidColorBrush).Color)
                        host.Environment.HighContrast = False
                        host.Environment.Notify()
                        host.Layout()
                        Assert.AreEqual(AppTheme.Light, host.Service.EffectiveTheme)
                        Dim dictionaryCount = host.Window.Resources.MergedDictionaries.Count
                        For i = 1 To 10
                            host.Service.SetTheme(AppTheme.Dark)
                            host.Service.SetTheme(AppTheme.Light)
                        Next
                        Assert.AreEqual(dictionaryCount, host.Window.Resources.MergedDictionaries.Count)
                        host.Service.Dispose()
                        Dim countAfterDispose = notifications
                        host.Environment.Notify()
                        host.Layout()
                        Assert.AreEqual(countAfterDispose, notifications)
                        Assert.ThrowsException(Of ObjectDisposedException)(Sub() host.Service.SetTheme(AppTheme.Dark))
                    End Using
                End Sub)
        End Sub

        Public Sub MainDataWindow_RealViewsKeepReadableContentBindingsAndEditingAcrossPalettes()
            OnSta(
                Sub()
                    Dim services As New TestApplicationServices()
                    Dim model As New MainDataViewModel(services.Tenant, services.ActingUserId,
                        services, services, services, 0, New ThemeInteraction())
                    Dim window As New MainDataWindow With {.DataContext = model}
                    Using host As New ThemeHost(window)
                        host.UseContrastScheme()
                        host.Show()
                        For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast, AppTheme.Dark}
                            host.Service.SetTheme(palette)
                            For tabIndex As Integer = 0 To 3
                                model.SelectedTab = tabIndex
                                host.Layout()
                                AssertReadableSubtree(window)
                                If tabIndex = 3 Then
                                    For detail = 0 To 4
                                        model.Collaboration.SelectedTab = detail
                                        host.Layout()
                                        AssertReadableSubtree(window)
                                    Next
                                End If
                            Next

                            model.SelectedTab = 0
                            host.Layout()
                            Dim users = window.TenantUserScreen
                            Dim password = DirectCast(users.FindName("TemporaryPasswordBox"), PasswordBox)
                            Assert.AreSame(window.FindResource("MainDataPasswordBoxStyle"), password.Style)
                            Assert.IsNotNull(password.Template.FindName("PART_ContentHost", password))
                            password.Password = "Theme-only-test-42"
                            Assert.IsTrue(password.SecurePassword.Length > 0)
                            password.Clear()
                            AssertEditorStates(password)
                            AssertEditorStates(users.TenantNameTextBox)
                            Dim header = Descendants(Of GridViewColumnHeader)(users.UserListView).
                                First(Function(item) item.Role = GridViewColumnHeaderRole.Normal)
                            Dim gripper = DirectCast(header.Template.FindName("PART_HeaderGripper", header), Thumb)
                            Assert.IsNotNull(gripper, "The themed header must retain column resizing.")
                            Dim width = header.Column.Width
                            gripper.RaiseEvent(New DragStartedEventArgs(0, 0) With {.RoutedEvent = Thumb.DragStartedEvent})
                            gripper.RaiseEvent(New DragDeltaEventArgs(20, 0) With {.RoutedEvent = Thumb.DragDeltaEvent})
                            gripper.RaiseEvent(New DragCompletedEventArgs(20, 0, False) With {.RoutedEvent = Thumb.DragCompletedEvent})
                            Assert.AreEqual(width + 20, header.Column.Width)
                            header.Column.Width = width

                            model.SelectedTab = 1
                            host.Layout()
                            Dim projects = window.ProjectScreen
                            Assert.AreSame(model.Projects.Projects, projects.ProjectListView.ItemsSource)
                            projects.ProjectListView.SelectedIndex = 1
                            Assert.AreSame(projects.ProjectListView.SelectedItem, model.Projects.SelectedProject)
                            projects.ProjectNameTextBox.SetCurrentValue(TextBox.TextProperty, "Themed draft")
                            host.Layout()
                            Assert.AreEqual("Themed draft", model.Projects.ProjectName)
                            AssertEditorStates(projects.ProjectNameTextBox)
                            AssertEditorStates(projects.ActiveCheckBox)
                            AssertReadableSubtree(projects)

                            model.SelectedTab = 2
                            host.Layout()
                            Dim projectPicker = Descendants(Of ComboBox)(window.TaskScreen).Single()
                            Assert.AreSame(model.Tasks.Projects, projectPicker.ItemsSource)
                            projectPicker.SelectedIndex = 1
                            Assert.AreSame(projectPicker.SelectedItem, model.Tasks.SelectedProject)
                            AssertEditorStates(projectPicker)
                            projectPicker.IsDropDownOpen = True
                            host.Layout()
                            Dim optionItem = DirectCast(projectPicker.ItemContainerGenerator.ContainerFromIndex(1), ComboBoxItem)
                            Assert.IsNotNull(optionItem)
                            AssertReadableSubtree(optionItem)
                            projectPicker.IsDropDownOpen = False
                        Next
                    End Using
                End Sub)
        End Sub

        Public Sub MainWindow_UsesLiveCalendarAndPaletteResources()
            OnSta(
                Sub()
                    Dim application As New Application With {.ShutdownMode = ShutdownMode.OnExplicitShutdown}
                    application.Resources.MergedDictionaries.Add(New ResourceDictionary With {
                        .Source = New Uri("/TaskOTime.App;component/Themes/ClassicDark.xaml", UriKind.Relative)})
                    application.Resources.MergedDictionaries.Add(New ResourceDictionary With {
                        .Source = New Uri("/TaskOTime.App;component/Resources/Strings.xaml", UriKind.Relative)})
                    Dim services As New TestApplicationServices()
                    Dim user = services.GetTenantUsers(services.Tenant.IdTenant).Value.Single()
                    Dim authentication As New TestAuthenticationService(user, "Theme-test-password-42")
                    Dim desktop = DesktopServices.CreateForServices(authentication, services, services, services, services.Tenant)
                    Dim model = desktop.CreateMain(user)
                    Dim settingsType = GetType(MainWindow).Assembly.GetType("TaskOTime.App.Properties.Settings", True)
                    Dim settings = settingsType.GetProperty("Default").GetValue(Nothing)
                    Dim placement = settingsType.GetProperty("RestoreMainWindowPlacement")
                    placement.SetValue(settings, False)
                    Dim window As MainWindow = Nothing
                    Try
                        Using theme As New ThemeService(application.Resources, application.Dispatcher, New TestThemeEnvironment())
                            window = New MainWindow(model, desktop, user) With {
                                .Left = -32000, .Top = -32000, .ShowActivated = False, .ShowInTaskbar = False}
                            window.Show()
                            For Each palette In {AppTheme.Dark, AppTheme.Light, AppTheme.HighContrast, AppTheme.Dark}
                                application.Resources(SystemColors.WindowColorKey) = Colors.Black
                                application.Resources(SystemColors.WindowTextColorKey) = Colors.White
                                application.Resources(SystemColors.HighlightColorKey) = Colors.Yellow
                                application.Resources(SystemColors.HighlightTextColorKey) = Colors.Black
                                application.Resources(SystemColors.GrayTextColorKey) = Colors.Silver
                                theme.SetTheme(palette)
                                Pump(window)
                                AssertBrush(window.Background, window.FindResource("WindowBackgroundBrush"))
                                Dim recording = DirectCast(window.FindResource("RecordingForegroundBrush"), SolidColorBrush).Color
                                Dim commandBackground = DirectCast(window.FindResource("CommandBackgroundBrush"), SolidColorBrush)
                                Dim backgroundColor = commandBackground.Color
                                Dim minimumPulse As New SolidColorBrush(Color.FromRgb(
                                    CByte(Math.Round(recording.R * 0.7 + backgroundColor.R * 0.3)),
                                    CByte(Math.Round(recording.G * 0.7 + backgroundColor.G * 0.3)),
                                    CByte(Math.Round(recording.B * 0.7 + backgroundColor.B * 0.3))))
                                Assert.IsTrue(Contrast(minimumPulse, commandBackground) >= 4.5)
                                Dim calendar = Descendants(Of Calendar)(window).Single()
                                Assert.AreSame(window.FindResource("ThemedCalendarStyle"), calendar.Style)
                                Assert.AreEqual(0, calendar.Resources.Count)
                                model.SelectedDate = New DateTime(2026, 6, 15)
                                calendar.DisplayDate = model.SelectedDate
                                Pump(window)
                                Dim selected = Descendants(Of CalendarDayButton)(calendar).Single(Function(day) day.IsSelected)
                                AssertPair(selected, "SelectedForegroundBrush", "ListItemSelectedBackgroundBrush")
                                calendar.SelectedDate = New DateTime(2026, 6, 16)
                                Assert.AreEqual(calendar.SelectedDate.Value, model.SelectedDate)
                                Dim menu = Descendants(Of Menu)(window).Single()
                                AssertBrush(menu.Background, window.FindResource("CommandBackgroundBrush"))
                                AssertBrush(menu.Foreground, window.FindResource("MenuForegroundBrush"))
                                Dim menuItem = DirectCast(menu.Items(0), MenuItem)
                                Assert.AreEqual(TaskOTime.ViewModel.Localization.LocalizationService.Current("FileMenuHeader"), menuItem.Header)
                                SetState(menuItem, "IsHighlighted", True, GetType(MenuItem))
                                AssertPair(menuItem, "HoverForegroundBrush", "HoverBackgroundBrush")
                                SetState(menuItem, "IsHighlighted", False, GetType(MenuItem))
                                Dim button = Descendants(Of Button)(window).
                                    First(Function(item) item.IsVisible AndAlso item.IsEnabled AndAlso
                                              item.Template.FindName("ButtonBorder", item) IsNot Nothing)
                                SetState(button, "IsMouseOver", True)
                                Dim border = DirectCast(button.Template.FindName("ButtonBorder", button), Border)
                                AssertBrush(border.Background, window.FindResource("HoverBackgroundBrush"))
                                AssertBrush(button.Foreground, window.FindResource("HoverForegroundBrush"))
                                Assert.IsTrue(Contrast(button.Foreground, border.Background) >= 4.5)
                                For Each glyph As TextBlock In Descendants(Of TextBlock)(button)
                                    AssertBrush(glyph.Foreground, button.Foreground)
                                Next
                                SetState(button, "IsMouseOver", False)
                            Next
                        End Using
                    Finally
                        If window IsNot Nothing Then window.Close()
                        application.Shutdown()
                    End Try
                End Sub)
        End Sub

        Public Sub ApplicationTheme_StartSwitchAndDispose()
            OnSta(
                Sub()
                    Dim application As New Application With {.ShutdownMode = ShutdownMode.OnExplicitShutdown}
                    application.Resources.MergedDictionaries.Add(New ResourceDictionary With {
                        .Source = New Uri("/TaskOTime.App;component/Themes/ClassicDark.xaml", UriKind.Relative)})
                    Dim service = ThemeService.Start(application)
                    Dim window As New Window With {
                        .Left = -32000, .Top = -32000, .ShowActivated = False, .ShowInTaskbar = False,
                        .Width = 300, .Height = 150}
                    Dim label As New Label With {.Content = "Application theme"}
                    window.Content = label
                    label.SetResourceReference(FrameworkElement.StyleProperty, "MainDataLabelStyle")
                    Try
                        window.Show()
                        For Each palette In {AppTheme.Light, AppTheme.Dark, AppTheme.System}
                            service.SetTheme(palette)
                            Pump(window)
                            Assert.AreEqual(palette, service.SelectedTheme)
                            If SystemParameters.HighContrast Then Assert.AreEqual(AppTheme.HighContrast, service.EffectiveTheme)
                            AssertBrush(label.Background, application.Resources("PanelBackgroundBrush"))
                            AssertBrush(label.Foreground, application.Resources("ContentForegroundBrush"))
                        Next
                        service.Dispose()
                        Assert.ThrowsException(Of ObjectDisposedException)(Sub() service.SetTheme(AppTheme.Dark))
                    Finally
                        service.Dispose()
                        window.Close()
                        application.Shutdown()
                    End Try
                End Sub)
        End Sub

        Private Shared Sub Pump(window As Window)
            window.Dispatcher.Invoke(New Action(Sub()
                                                End Sub), DispatcherPriority.ApplicationIdle)
            window.UpdateLayout()
        End Sub

        Private Shared Sub AssertEditorStates(editor As Control)
            SetState(editor, "IsKeyboardFocusWithin", True)
            AssertBrush(editor.BorderBrush, editor.FindResource("FocusBrush"))
            SetState(editor, "IsKeyboardFocusWithin", False)
            editor.IsEnabled = False
            AssertPair(editor, "DisabledForegroundBrush", "DisabledBackgroundBrush")
            AssertReadableSubtree(editor)
            editor.ClearValue(UIElement.IsEnabledProperty)
        End Sub

        Private Shared Sub AssertReadableSubtree(root As DependencyObject)
            For Each control In Descendants(Of Control)(root).Where(Function(item) item.IsVisible)
                If TypeOf control Is Label OrElse TypeOf control Is TextBox OrElse TypeOf control Is PasswordBox OrElse
                   TypeOf control Is Button OrElse TypeOf control Is CheckBox OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is ListBox OrElse TypeOf control Is ListBoxItem OrElse
                   TypeOf control Is TabControl OrElse TypeOf control Is TabItem OrElse TypeOf control Is UserControl Then
                    Assert.IsNotNull(control.Style, control.GetType().Name & " " & control.Name & " has no shared style.")
                    Assert.IsInstanceOfType(control.Background, GetType(SolidColorBrush), control.GetType().Name & " " & control.Name)
                    Assert.IsTrue(Contrast(control.Foreground, control.Background) >= 4.5,
                                  control.GetType().Name & " " & control.Name & " has an unreadable control pair.")
                End If
            Next
            For Each textBlock As TextBlock In Descendants(Of TextBlock)(root).Where(Function(item) item.IsVisible AndAlso Not String.IsNullOrWhiteSpace(item.Text))
                Dim background = PaintedBackground(textBlock)
                Assert.IsNotNull(background, "No painted background for " & textBlock.Text)
                Assert.IsTrue(Contrast(textBlock.Foreground, background) >= 4.5, "Rendered text: " & textBlock.Text)
            Next
        End Sub

        Private Shared Function PaintedBackground(element As DependencyObject) As Brush
            While element IsNot Nothing
                Dim background As Brush = Nothing
                If TypeOf element Is Border Then background = DirectCast(element, Border).Background
                If TypeOf element Is Panel Then background = DirectCast(element, Panel).Background
                If TypeOf element Is Control Then background = DirectCast(element, Control).Background
                If TypeOf element Is TextBlock Then background = DirectCast(element, TextBlock).Background
                Dim solid = TryCast(background, SolidColorBrush)
                If solid IsNot Nothing AndAlso solid.Color.A = 255 AndAlso solid.Opacity = 1 Then Return solid
                element = VisualTreeHelper.GetParent(element)
            End While
            Return Nothing
        End Function

        Private Shared Sub AssertPair(control As Control, foreground As String, background As String)
            AssertBrush(control.Foreground, control.FindResource(foreground))
            AssertBrush(control.Background, control.FindResource(background))
            Assert.IsTrue(Contrast(control.Foreground, control.Background) >= 4.5,
                          control.GetType().Name & " " & foreground & "/" & background)
        End Sub

        Private Shared Sub AssertBrush(actual As Brush, expected As Object)
            Assert.IsInstanceOfType(actual, GetType(SolidColorBrush))
            Assert.AreEqual(DirectCast(expected, SolidColorBrush).Color, DirectCast(actual, SolidColorBrush).Color)
            Assert.AreEqual(1.0, actual.Opacity)
            Assert.AreEqual(CByte(255), DirectCast(actual, SolidColorBrush).Color.A)
        End Sub

        Private Shared Function Contrast(foreground As Brush, background As Brush) As Double
            Dim light = Luminance(DirectCast(foreground, SolidColorBrush).Color)
            Dim dark = Luminance(DirectCast(background, SolidColorBrush).Color)
            Return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05)
        End Function

        Private Shared Function Luminance(color As Color) As Double
            Dim linear As Func(Of Byte, Double) =
                Function(value)
                    Dim channel = value / 255.0
                    Return If(channel <= 0.04045, channel / 12.92, Math.Pow((channel + 0.055) / 1.055, 2.4))
                End Function
            Return 0.2126 * linear(color.R) + 0.7152 * linear(color.G) + 0.0722 * linear(color.B)
        End Function

        Private Shared Sub SetState(element As UIElement, name As String, value As Boolean, Optional ownerType As Type = Nothing)
            ' Exercise real WPF triggers deterministically without moving the user's mouse or keyboard focus.
            Dim field = If(ownerType, GetType(UIElement)).GetField(name & "PropertyKey", BindingFlags.Static Or BindingFlags.NonPublic)
            Assert.IsNotNull(field, name)
            element.SetValue(DirectCast(field.GetValue(Nothing), DependencyPropertyKey), value)
        End Sub

        Private Shared Iterator Function Descendants(Of T As DependencyObject)(root As DependencyObject) As IEnumerable(Of T)
            For i = 0 To VisualTreeHelper.GetChildrenCount(root) - 1
                Dim child = VisualTreeHelper.GetChild(root, i)
                If TypeOf child Is T Then Yield DirectCast(child, T)
                For Each descendant In Descendants(Of T)(child)
                    Yield descendant
                Next
            Next
        End Function

        Private Shared Sub OnSta(action As Action)
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState())
            action()
        End Sub

        Private NotInheritable Class ThemeInteraction
            Implements TaskOTime.ViewModel.IMaintenanceInteraction
            Public Sub Notify(message As String, title As String) Implements TaskOTime.ViewModel.IMaintenanceInteraction.Notify
                Assert.Fail(title & ": " & message)
            End Sub
            Public Function Confirm(message As String, title As String) As Boolean Implements TaskOTime.ViewModel.IMaintenanceInteraction.Confirm
                Return True
            End Function
        End Class

        Private NotInheritable Class ThemeHost
            Implements IDisposable
            Public ReadOnly Window As Window
            Public ReadOnly Panel As New StackPanel()
            Public ReadOnly Environment As New TestThemeEnvironment()
            Public ReadOnly Service As ThemeService
            Public Sub New(Optional contentWindow As Window = Nothing)
                Window = If(contentWindow, New Window With {.Width = 420, .Height = 720})
                Window.Left = -32000
                Window.Top = -32000
                Window.ShowInTaskbar = False
                Window.ShowActivated = False
                Window.Resources.MergedDictionaries.Add(New ResourceDictionary With {
                    .Source = New Uri("/TaskOTime.App;component/Themes/ClassicDark.xaml", UriKind.Relative)})
                If contentWindow Is Nothing Then Window.Content = Panel
                Service = New ThemeService(Window.Resources, Window.Dispatcher, Environment)
            End Sub
            Public Sub Show()
                Window.Show()
                Layout()
            End Sub
            Public Sub UseContrastScheme(Optional light As Boolean = False)
                ' Override only this window's resources, never the Windows theme or user preferences.
                Window.Resources(SystemColors.WindowColorKey) = If(light, Colors.White, Colors.Black)
                Window.Resources(SystemColors.WindowTextColorKey) = If(light, Colors.Black, Colors.White)
                Window.Resources(SystemColors.HighlightColorKey) = If(light, Colors.Black, Colors.Yellow)
                Window.Resources(SystemColors.HighlightTextColorKey) = If(light, Colors.White, Colors.Black)
                Window.Resources(SystemColors.GrayTextColorKey) = If(light, Colors.DarkSlateGray, Colors.Silver)
            End Sub
            Public Sub Layout()
                Window.Dispatcher.Invoke(New Action(Sub()
                                                    End Sub), DispatcherPriority.ApplicationIdle)
                Window.UpdateLayout()
            End Sub
            Public Sub Dispose() Implements IDisposable.Dispose
                Service.Dispose()
                Window.Close()
            End Sub
        End Class

        Private NotInheritable Class TestThemeEnvironment
            Implements IThemeEnvironment
            Public Property HighContrast As Boolean Implements IThemeEnvironment.HighContrast
            Public Property UseLightTheme As Boolean Implements IThemeEnvironment.UseLightTheme
            Public Event Changed As EventHandler Implements IThemeEnvironment.Changed
            Public Sub Notify()
                RaiseEvent Changed(Me, EventArgs.Empty)
            End Sub
        End Class
    End Class
End Namespace
