Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Windows
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Data
Imports System.Windows.Media
Imports System.Windows.Shapes
Imports System.Windows.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.App.Themes

Namespace TaskOTime.TimeTrackingServices.Tests
    <TestClass, DoNotParallelize>
    Public Class ThemeResourceTests
        Private Shared isolated As Boolean
        Private Shared ReadOnly isolatedRunner As New Lazy(Of ThemeScenarioRunner)(
            Function()
                ' WPF's native rendering threads give this one domain a test-host lifetime.
                Dim domain = AppDomain.CreateDomain("ThemeScenarios", Nothing, AppDomain.CurrentDomain.SetupInformation)
                Return DirectCast(domain.CreateInstanceAndUnwrap(
                    GetType(ThemeScenarioRunner).Assembly.FullName, GetType(ThemeScenarioRunner).FullName), ThemeScenarioRunner)
            End Function)
        <TestMethod>
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

        <TestMethod>
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

        <TestMethod>
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

        <TestMethod>
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

        Private Shared Sub SetState(element As UIElement, name As String, value As Boolean)
            ' Exercise real WPF triggers deterministically without moving the user's mouse or keyboard focus.
            Dim field = GetType(UIElement).GetField(name & "PropertyKey", BindingFlags.Static Or BindingFlags.NonPublic)
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

        Private Shared Sub OnSta(action As Action, <CallerMemberName> Optional testName As String = Nothing)
            If Not isolated Then
                ' Other Framework tests shut down Application, which is permanent within an AppDomain.
                isolatedRunner.Value.Run(testName)
                Return
            End If
            Dim failure As Exception = Nothing
            Dim thread As New Thread(
                Sub()
                    Try
                        action()
                    Catch ex As Exception
                        failure = ex
                    Finally
                        Dispatcher.CurrentDispatcher.InvokeShutdown()
                    End Try
                End Sub)
            thread.SetApartmentState(ApartmentState.STA)
            thread.IsBackground = True
            thread.Start()
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(45)), "Theme verification did not finish.")
            If failure IsNot Nothing Then ExceptionDispatchInfo.Capture(failure).Throw()
        End Sub

        Public NotInheritable Class ThemeScenarioRunner
            Inherits MarshalByRefObject
            Public Overrides Function InitializeLifetimeService() As Object
                Return Nothing
            End Function
            Public Sub Run(testName As String)
                isolated = True
                Try
                    GetType(ThemeResourceTests).GetMethod(testName).Invoke(New ThemeResourceTests(), Nothing)
                Catch ex As TargetInvocationException
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw()
                End Try
            End Sub
        End Class

        Private NotInheritable Class ThemeHost
            Implements IDisposable
            Public ReadOnly Window As New Window With {
                .Width = 420, .Height = 720, .Left = -32000, .Top = -32000,
                .ShowInTaskbar = False, .ShowActivated = False}
            Public ReadOnly Panel As New StackPanel()
            Public ReadOnly Environment As New TestThemeEnvironment()
            Public ReadOnly Service As ThemeService
            Public Sub New()
                Window.Resources.MergedDictionaries.Add(New ResourceDictionary With {
                    .Source = New Uri("/TaskOTime.App;component/Themes/ClassicDark.xaml", UriKind.Relative)})
                Window.Content = Panel
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
