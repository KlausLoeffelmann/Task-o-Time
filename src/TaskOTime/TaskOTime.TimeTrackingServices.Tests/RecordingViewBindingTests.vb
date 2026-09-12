Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Threading
Imports ActiveDevelop.TimeTrackingServices
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.App
Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.TimeBooking
Imports TaskOTime.TimeTrackingServices.Tests.Doubles
Imports TaskOTime.ViewModel.ViewModels
Imports TaskOTime.ViewModel.Views

Namespace TaskOTime.TimeTrackingServices.Tests
    <TestClass>
    Public Class RecordingViewBindingTests
        <TestMethod>
        Public Sub OriginalListView_BindsToSameProductionCollectionInstanceOnStaThread()
            Dim failure As Exception = Nothing
            Dim thread As New Thread(
                Sub()
                    Try
                        VerifyRuntimeBinding()
                    Catch ex As Exception
                        failure = ex
                    End Try
                End Sub)
            thread.SetApartmentState(ApartmentState.STA)
            thread.IsBackground = True
            thread.Start()
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "The WPF binding verification did not finish.")
            If failure IsNot Nothing Then ExceptionDispatchInfo.Capture(failure).Throw()
        End Sub

        Private Shared Sub VerifyRuntimeBinding()
            Dim application As Application = Nothing
            Dim window As MainWindow = Nothing
            Dim login As LoginViewModel = Nothing
            Dim settings As Object = Nothing
            Dim placementProperty As Reflection.PropertyInfo = Nothing
            Dim restorePlacement As Object = Nothing
            Try
                application = New Application With {.ShutdownMode = ShutdownMode.OnExplicitShutdown}
                For Each resource In {"Themes/ClassicDark.xaml", "Resources/Strings.xaml"}
                    application.Resources.MergedDictionaries.Add(
                        DirectCast(Application.LoadComponent(
                            New Uri("/TaskOTime.App;component/" & resource, UriKind.Relative)), ResourceDictionary))
                Next
                Dim settingsType = GetType(MainWindow).Assembly.GetType("TaskOTime.App.Properties.Settings", throwOnError:=True)
                settings = settingsType.GetProperty("Default").GetValue(Nothing)
                placementProperty = settingsType.GetProperty("RestoreMainWindowPlacement")
                restorePlacement = placementProperty.GetValue(settings)
                placementProperty.SetValue(settings, False)

                Dim testServices As New TestApplicationServices()
                Dim user = testServices.GetTenantUsers(testServices.Tenant.IdTenant).Value.Single()
                Dim authentication = New TestAuthenticationService(user, "Binding-test-password-42")
                Dim services = DesktopServices.CreateForServices(
                    authentication, testServices, testServices, testServices, testServices.Tenant)
                login = New LoginViewModel(authentication)
                Assert.IsTrue(login.Login("a.adler", "Binding-test-password-42"))
                Dim vm = services.CreateMain(login.Session)
                window = New MainWindow(vm, services, login.Session)
                window.ShowInTaskbar = False
                window.ShowActivated = False
                window.Left = -32000
                window.Top = -32000
                window.Show()
                window.ApplyTemplate()
                window.Dispatcher.Invoke(New Action(Sub()
                                                    End Sub), DispatcherPriority.DataBind)

                Dim list = FindTimeList(window)
                Assert.IsNotNull(list, "The restored window must contain the recording ListView.")
                Assert.AreSame(vm, window.DataContext)
                Assert.IsInstanceOfType(vm.TimeCollection.TimeItems, GetType(TimeItemsBase(Of Guid, TimeEntryViewModel)))
                Assert.IsTrue(Object.ReferenceEquals(list.ItemsSource, vm.TimeCollection.TimeItems))
                Assert.IsTrue(Object.ReferenceEquals(list.ItemsSource, vm.TimeCollection.SelectedDayEntries))

                Dim originalSource = list.ItemsSource
                vm.SelectedDate = New DateTime(2026, 6, 15)
                window.Dispatcher.Invoke(New Action(Sub()
                                                    End Sub), DispatcherPriority.DataBind)
                Assert.AreSame(originalSource, list.ItemsSource)
                Assert.AreEqual(5, list.Items.Count)
                Assert.AreSame(vm.TimeCollection.TimeItems(0), list.Items(0))

                window.Measure(New Size(window.Width, window.Height))
                window.Arrange(New Rect(0, 0, window.Width, window.Height))
                window.UpdateLayout()
                AssertMainCommandInventory(window, vm)
                ExerciseForwardedRecordingActions(window, vm, testServices, login.Session)

                Dim master = New MainDataWindow()
                Dim masterViewModel = New MainDataViewModel(
                    testServices.Tenant, testServices.ActingUserId,
                    testServices, testServices, testServices, 1, New TestMaintenanceInteraction())
                master.DataContext = masterViewModel
                master.ShowInTaskbar = False
                master.Show()
                master.Dispatcher.Invoke(Sub() master.UpdateLayout(), DispatcherPriority.DataBind)
                AssertMainDataBindings(master, masterViewModel)
                GC.KeepAlive(masterViewModel)
                master.Close()
            Finally
                If window IsNot Nothing Then window.Close()
                If login IsNot Nothing Then login.Logout()
                If placementProperty IsNot Nothing AndAlso settings IsNot Nothing Then placementProperty.SetValue(settings, restorePlacement)
                If application IsNot Nothing Then application.Shutdown()
            End Try
        End Sub

        Private Shared Sub AssertMainCommandInventory(window As MainWindow, vm As VmMain)
            Dim commandBindings As New Dictionary(Of String, ICommand) From {
                {"ExportSelectedDayCommand", vm.ExportSelectedDayCommand},
                {"ExportPeriodCommand", vm.ExportPeriodCommand},
                {"ManageProjectsCommand", vm.ManageProjectsCommand},
                {"TaskManagement.NewTaskListCommand", vm.TaskManagement.NewTaskListCommand},
                {"ManageTaskListsCommand", vm.ManageTaskListsCommand},
                {"ManageTasksCommand", vm.ManageTasksCommand},
                {"ManageUsersAdminCommand", vm.ManageUsersAdminCommand},
                {"ShowDailyStatementCommand", vm.ShowDailyStatementCommand},
                {"ShowWeeklyStatementCommand", vm.ShowWeeklyStatementCommand},
                {"ShowMonthlyStatementCommand", vm.ShowMonthlyStatementCommand},
                {"ShowTenantAdminStatisticsCommand", vm.ShowTenantAdminStatisticsCommand},
                {"OptionsCommand", vm.OptionsCommand},
                {"TaskManagement.NewTaskCommand", vm.TaskManagement.NewTaskCommand},
                {"TaskManagement.EditTaskCommand", vm.TaskManagement.EditTaskCommand},
                {"TaskManagement.EditTaskListCommand", vm.TaskManagement.EditTaskListCommand},
                {"TaskManagement.StartTaskCommand", vm.TaskManagement.StartTaskCommand},
                {"TaskManagement.CompleteTaskCommand", vm.TaskManagement.CompleteTaskCommand},
                {"TaskManagement.MoreToDoCommand", vm.TaskManagement.MoreToDoCommand},
                {"TodayCommand", vm.TodayCommand},
                {"YesterdayCommand", vm.YesterdayCommand},
                {"TimeCollection.AddCommand", vm.TimeCollection.AddCommand},
                {"TimeCollection.EditCommand", vm.TimeCollection.EditCommand},
                {"TimeCollection.DeleteCommand", vm.TimeCollection.DeleteCommand},
                {"TimeCollection.InsertWorkBreakCommand", vm.TimeCollection.InsertWorkBreakCommand},
                {"TimeCollection.InsertDownTimeCommand", vm.TimeCollection.InsertDownTimeCommand},
                {"TimeCollection.InsertErrandCommand", vm.TimeCollection.InsertErrandCommand},
                {"TimeCollection.InsertStopMarkCommand", vm.TimeCollection.InsertStopMarkCommand}
            }
            For Each binding In commandBindings
                Assert.IsNotNull(binding.Value, "MainWindow command binding did not resolve: " & binding.Key)
            Next

            Dim forwardingNames = New HashSet(Of String) From {
                "CommandStripCheckOutButton",
                "CommandStripWorkBreakButton",
                "CommandStripDownTimeButton",
                "CommandStripErrandButton"
            }
            For Each button In FindLogicalDescendants(Of Button)(window).Where(Function(item) item.ToolTip IsNot Nothing)
                If forwardingNames.Contains(button.Name) Then
                    Assert.IsNull(button.Command, button.Name & " must forward exactly once through Click.")
                    Assert.IsTrue(HasRoutedHandler(button, Button.ClickEvent), button.Name & " has no Click handler.")
                Else
                    Assert.IsNotNull(button.Command, "Visible MainWindow button has no command: " & button.ToolTip.ToString())
                End If
            Next

            Dim menu = FindLogicalDescendants(Of Menu)(window).Single()
            For Each item In LeafMenuItems(menu.Items)
                Assert.IsTrue(item.Command IsNot Nothing OrElse HasRoutedHandler(item, MenuItem.ClickEvent),
                              "Visible leaf menu item has no command or click handler: " & item.Header.ToString())
            Next
            AssertContextMenuBindings(window)

            Dim clickMethods = {
                "OnLogoutClick",
                "OnQuitMenuItemClick",
                "OnCommandStripCheckOutClick",
                "OnCommandStripWorkBreakClick",
                "OnCommandStripDownTimeClick",
                "OnCommandStripErrandClick",
                "OnTaskListDoubleClick",
                "OnTimeEntryDoubleClick"
            }
            For Each methodName In clickMethods
                Assert.IsNotNull(GetType(MainWindow).GetMethod(
                    methodName,
                    Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic),
                    "MainWindow Click handler is missing: " & methodName)
            Next
        End Sub

        Private Shared Sub AssertContextMenuBindings(window As MainWindow)
            Dim owners = FindVisualDescendants(Of FrameworkElement)(window).
                Where(Function(element) element.ContextMenu IsNot Nothing).
                ToList()
            Assert.IsTrue(owners.Count >= 2, "Task list and task item context menus were not materialized.")
            For Each owner In owners
                owner.ContextMenu.PlacementTarget = owner
                owner.ContextMenu.ApplyTemplate()
                For Each item In LeafMenuItems(owner.ContextMenu.Items)
                    Dim expression = item.GetBindingExpression(MenuItem.CommandProperty)
                    If expression IsNot Nothing Then expression.UpdateTarget()
                    Assert.IsNotNull(item.Command, "Context-menu command binding did not resolve: " & item.Header.ToString())
                Next
            Next
        End Sub

        Private Shared Sub ExerciseForwardedRecordingActions(
            window As MainWindow,
            vm As VmMain,
            services As TestApplicationServices,
            session As TenantUserDto)
            Dim selectedDay = DateTime.Today.AddDays(10)
            vm.SelectedDate = selectedDay
            Assert.AreEqual(0, vm.TimeCollection.TimeItems.Count)
            Assert.IsNotNull(vm.TimeCollection.SelectedCategory)
            Assert.IsTrue(vm.TimeCollection.Categories.Any(
                Function(category) category.IdCategory = vm.TimeCollection.SelectedCategory.IdCategory))

            For Each buttonName In {
                "CommandStripWorkBreakButton",
                "CommandStripDownTimeButton",
                "CommandStripErrandButton",
                "CommandStripCheckOutButton"
            }
                Dim button = DirectCast(window.FindName(buttonName), Button)
                Assert.IsNotNull(button)
                button.RaiseEvent(New RoutedEventArgs(Button.ClickEvent))
            Next

            Dim access = New TimeBookingAccessContextDto With {
                .IdTenant = session.IdTenant,
                .IdActingUser = session.IdUser,
                .IdBookingUser = session.IdUser
            }
            Dim day = services.GetBookingDay(New GetBookingDayRequest With {
                .AccessContext = access,
                .BookingDate = selectedDay
            }).Value
            Assert.AreEqual(4, day.Items.Count)
            Assert.AreEqual(4, vm.TimeCollection.TimeItems.Count)
            Assert.AreEqual(1, day.Items.Where(Function(item) item.MarkerKind = SystemTimeMarkerKind.WorkBreak).Count())
            Assert.AreEqual(1, day.Items.Where(Function(item) item.MarkerKind = SystemTimeMarkerKind.DownTime).Count())
            Assert.AreEqual(1, day.Items.Where(Function(item) item.MarkerKind = SystemTimeMarkerKind.Errand).Count())
            Assert.AreEqual(1, day.Items.Where(Function(item) item.MarkerKind = SystemTimeMarkerKind.StopMark).Count())
            Assert.AreEqual(
                TimeBookingOptions.ErrandEventInfo,
                day.Items.Single(Function(item) item.MarkerKind = SystemTimeMarkerKind.Errand).EventInfo)
        End Sub

        Private Shared Sub AssertMainDataBindings(master As MainDataWindow, vm As MainDataViewModel)
            Assert.AreEqual("Projects", DirectCast(master.WorkspaceTabs.Items(1), TabItem).Header)
            Assert.AreEqual("Project Main Data", master.ProjectScreen.HeadingLabel.Content)
            Assert.AreEqual("New", master.ProjectScreen.NewButton.Content)
            Assert.AreEqual("Save", master.ProjectScreen.SaveButton.Content)
            Assert.AreEqual("Archive", master.ProjectScreen.DeleteButton.Content)

            Dim buttons = {
                master.AboutButton,
                master.TenantUserScreen.AddUserButton,
                master.TenantUserScreen.SaveTenantButton,
                master.TenantUserScreen.DeleteUserButton,
                master.ProjectScreen.NewButton,
                master.ProjectScreen.SaveButton,
                master.ProjectScreen.DeleteButton,
                master.TaskScreen.AddListButton,
                master.TaskScreen.AddTaskButton,
                master.TaskScreen.SaveTaskButton,
                master.TaskScreen.DeleteTaskButton,
                master.CollaborationScreen.AddButton,
                master.CollaborationScreen.DeleteButton
            }
            For Each button In buttons
                Assert.IsNotNull(button.Command, "Main Data action must bind a command: " & button.Name)
                Assert.IsTrue(System.Windows.Data.BindingOperations.IsDataBound(button, Button.CommandProperty))
                Assert.IsFalse(HasRoutedHandler(button, Button.ClickEvent),
                               "Main Data buttons must not wire action handlers: " & button.Name)
            Next
            Assert.AreSame(vm.Projects.Projects, master.ProjectScreen.ProjectListView.ItemsSource)
            Assert.AreSame(vm.Projects.SelectedProject, master.ProjectScreen.ProjectListView.SelectedItem)
            master.ProjectScreen.ProjectNameTextBox.Text = "Updated through binding"
            Assert.AreEqual("Updated through binding", vm.Projects.ProjectName)
            master.ProjectScreen.SaveButton.Command.Execute(Nothing)
            Assert.AreEqual("Updated through binding", vm.Projects.SelectedProject.ProjectName)
            Assert.AreSame(vm.Projects.SelectedProject, master.ProjectScreen.ProjectListView.SelectedItem)
            master.ProjectScreen.NewButton.Command.Execute(Nothing)
            Assert.AreSame(vm.Projects.SelectedProject, master.ProjectScreen.ProjectListView.SelectedItem)
            master.ProjectScreen.DeleteButton.Command.Execute(Nothing)
            Assert.AreSame(vm.Projects.SelectedProject, master.ProjectScreen.ProjectListView.SelectedItem)
            master.WorkspaceTabs.SelectedIndex = 2
            master.TaskScreen.AddTaskButton.Command.Execute(Nothing)
            Assert.AreSame(vm.Tasks.SelectedTask, master.TaskScreen.TaskListView.SelectedItem)
            master.TaskScreen.TaskNameTextBox.Text = "Bound task"
            master.TaskScreen.SaveTaskButton.Command.Execute(Nothing)
            Assert.AreEqual("Bound task", vm.Tasks.SelectedTask.TaskItemName)
            Assert.AreSame(vm.Tasks.SelectedTask, master.TaskScreen.TaskListView.SelectedItem)
            master.TaskScreen.DeleteTaskButton.Command.Execute(Nothing)
            Assert.AreSame(vm.Tasks.SelectedTask, master.TaskScreen.TaskListView.SelectedItem)

            master.WorkspaceTabs.SelectedIndex = 3
            Dim selectors As System.Windows.Controls.Primitives.Selector() = {
                master.CollaborationScreen.CategoryListView, master.CollaborationScreen.TagListBox,
                master.CollaborationScreen.NoteListView, master.CollaborationScreen.WebLinkListView,
                master.CollaborationScreen.LogListView
            }
            For index = 0 To 4
                master.CollaborationScreen.DetailTabs.SelectedIndex = index
                Assert.AreEqual(index, vm.Collaboration.SelectedTab)
                master.CollaborationScreen.QuickValueTextBox.Text = "https://example.invalid/bound"
                Dim previousCount = selectors(index).Items.Count
                master.CollaborationScreen.AddButton.Command.Execute(Nothing)
                Assert.AreEqual(previousCount + 1, selectors(index).Items.Count)
                Assert.IsNotNull(selectors(index).SelectedItem)
                master.CollaborationScreen.DeleteButton.Command.Execute(Nothing)
                Assert.AreEqual(previousCount, selectors(index).Items.Count)
            Next
            master.WorkspaceTabs.SelectedIndex = 0
            master.TenantUserScreen.TenantNameTextBox.Text = "Tenant saved through binding"
            master.TenantUserScreen.SaveTenantButton.Command.Execute(Nothing)
            Assert.AreEqual("Tenant saved through binding", vm.Tenant.TenantName)
            Assert.AreEqual("Tenant saved through binding", master.ApplicationStatusLabel.Text)
            Assert.AreSame(vm.TenantUsers.SelectedTenant, master.TenantUserScreen.TenantListView.SelectedItem)
            Dim passwordBox = DirectCast(master.TenantUserScreen.FindName("TemporaryPasswordBox"), PasswordBox)
            passwordBox.Password = "Binding-initial-password-42!"
            Assert.AreEqual(passwordBox.Password, vm.TenantUsers.TemporaryPassword)
            master.TenantUserScreen.AddUserButton.Command.Execute(Nothing)
            Assert.AreEqual("", passwordBox.Password)
            Assert.AreSame(vm.TenantUsers.SelectedUser, master.TenantUserScreen.UserListView.SelectedItem)
        End Sub

        Private Shared Function HasRoutedHandler(element As UIElement, routedEvent As RoutedEvent) As Boolean
            Dim storeProperty = GetType(UIElement).GetProperty(
                "EventHandlersStore",
                Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            Dim store = storeProperty.GetValue(element)
            If store Is Nothing Then Return False
            Dim getHandlers = store.GetType().GetMethod(
                "GetRoutedEventHandlers",
                Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic)
            Dim handlers = TryCast(getHandlers.Invoke(store, {routedEvent}), Array)
            Return handlers IsNot Nothing AndAlso handlers.Length > 0
        End Function

        Private Shared Iterator Function LeafMenuItems(items As ItemCollection) As IEnumerable(Of MenuItem)
            For Each value In items
                Dim item = TryCast(value, MenuItem)
                If item Is Nothing Then Continue For
                If item.Items.Count = 0 Then
                    Yield item
                Else
                    For Each child In LeafMenuItems(item.Items)
                        Yield child
                    Next
                End If
            Next
        End Function

        Private Shared Iterator Function FindLogicalDescendants(Of T As DependencyObject)(
            node As DependencyObject) As IEnumerable(Of T)
            Dim match = TryCast(node, T)
            If match IsNot Nothing Then Yield match
            For Each child In LogicalTreeHelper.GetChildren(node)
                Dim dependency = TryCast(child, DependencyObject)
                If dependency Is Nothing Then Continue For
                For Each result In FindLogicalDescendants(Of T)(dependency)
                    Yield result
                Next
            Next
        End Function

        Private Shared Iterator Function FindVisualDescendants(Of T As DependencyObject)(
            node As DependencyObject) As IEnumerable(Of T)
            Dim match = TryCast(node, T)
            If match IsNot Nothing Then Yield match
            For index = 0 To VisualTreeHelper.GetChildrenCount(node) - 1
                For Each result In FindVisualDescendants(Of T)(VisualTreeHelper.GetChild(node, index))
                    Yield result
                Next
            Next
        End Function

        Private Shared Function FindTimeList(node As DependencyObject) As ListView
            Dim list = TryCast(node, ListView)
            If list IsNot Nothing Then Return list
            For Each child In LogicalTreeHelper.GetChildren(node)
                Dim dependency = TryCast(child, DependencyObject)
                If dependency Is Nothing Then Continue For
                Dim result = FindTimeList(dependency)
                If result IsNot Nothing Then Return result
            Next
            Return Nothing
        End Function
    End Class
End Namespace
