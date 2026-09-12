Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Linq
Imports System.Reflection
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.AppServer.Models
Imports TaskOTime.TimeTrackingServices.Tests.Doubles
Imports TaskOTime.ViewModel
Imports TaskOTime.ViewModel.ViewModels

Namespace TaskOTime.TimeTrackingServices.Tests
    Public Class TestMaintenanceInteraction
        Implements IMaintenanceInteraction

        Public ReadOnly Messages As New List(Of String)
        Public Property ConfirmResult As Boolean = True
        Public Sub Notify(message As String, title As String) Implements IMaintenanceInteraction.Notify
            Messages.Add(title & ": " & message)
        End Sub
        Public Function Confirm(message As String, title As String) As Boolean Implements IMaintenanceInteraction.Confirm
            Return ConfirmResult
        End Function
    End Class

    <TestClass>
    Public Class MaintenanceViewModelTests
        Private Class Fixture
            Public ReadOnly Services As New TestApplicationServices()
            Public ReadOnly Interaction As New TestMaintenanceInteraction()
            Public ReadOnly Store As ServiceWorkspace
            Public ReadOnly Main As MainDataViewModel
            Public Sub New()
                Store = New ServiceWorkspace(Services.Tenant, Services.ActingUserId, Services, Services, Services)
                Main = New MainDataViewModel(Store, 1, Interaction)
            End Sub
        End Class

        <TestMethod>
        Public Sub MaintenanceModels_AreObservableAndDoNotRetainViews()
            For Each modelType In {GetType(MainDataViewModel), GetType(ProjectViewModel),
                GetType(TenantUserViewModel), GetType(MasterTaskViewModel), GetType(CollaborationViewModel)}
                Assert.IsTrue(GetType(INotifyPropertyChanged).IsAssignableFrom(modelType))
                For Each field In modelType.GetFields(BindingFlags.NonPublic Or BindingFlags.Public Or BindingFlags.Instance)
                    Assert.IsFalse(GetType(System.Windows.DependencyObject).IsAssignableFrom(field.FieldType), field.Name)
                Next
                For Each constructor In modelType.GetConstructors()
                    Assert.IsFalse(constructor.GetParameters().Any(
                        Function(parameter) GetType(System.Windows.DependencyObject).IsAssignableFrom(parameter.ParameterType)))
                Next
            Next
        End Sub

        <TestMethod>
        Public Sub Projects_CreateSaveArchive_PreserveSelectionIdentityAndRequestScope()
            Dim f As New Fixture()
            Dim vm = f.Main.Projects
            Dim originalCollection = vm.Projects
            vm.NewCommand.Execute(Nothing)
            Dim created = vm.SelectedProject
            Assert.IsTrue(vm.Projects.Contains(created))
            Assert.AreEqual(f.Services.Tenant.IdTenant, f.Services.LastMutationTenant)
            Assert.AreEqual(f.Services.ActingUserId, f.Services.LastMutationActingUser)
            created.ExternalId = "preserved"
            vm.ProjectName = " Updated "
            vm.Identifier = " ID "
            vm.Description = "Description"
            vm.IsActive = False
            Assert.AreEqual("New project", created.ProjectName, "Draft changes must not mutate persisted objects.")
            vm.SaveCommand.Execute(Nothing)
            Assert.AreEqual(created.IdProject, vm.SelectedProject.IdProject)
            Assert.AreEqual("Updated", vm.SelectedProject.ProjectName)
            Assert.AreEqual("ID", vm.SelectedProject.ProjectIdentifier)
            Assert.AreEqual("Description", vm.SelectedProject.ProjectDescription)
            Assert.IsFalse(vm.SelectedProject.IsActive)
            Assert.AreEqual("preserved", vm.SelectedProject.ExternalId)
            Assert.AreSame(vm.SelectedProject, f.Services.GetProject(New MainDataItemRequest With {.IdItem = created.IdProject}).Value)
            vm.ArchiveCommand.Execute(Nothing)
            Assert.IsFalse(f.Services.LastDeleteRequest.HardDelete)
            Assert.AreEqual(created.IdProject, f.Services.LastDeleteRequest.IdItem)
            Assert.AreEqual(f.Services.Tenant.IdTenant, f.Services.LastDeleteRequest.IdTenant)
            Assert.AreEqual(f.Services.ActingUserId, f.Services.LastDeleteRequest.IdActingUser)
            Assert.IsFalse(vm.Projects.Any(Function(item) item.IdProject = created.IdProject))
            Assert.IsTrue(vm.Projects.Contains(vm.SelectedProject))
            Assert.AreSame(originalCollection, vm.Projects)
        End Sub

        <TestMethod>
        Public Sub ProjectFailures_KeepDraftAndSelectionWithoutOptimisticMutation()
            Dim f As New Fixture()
            Dim vm = f.Main.Projects
            Dim original = vm.SelectedProject
            Dim originalName = original.ProjectName
            Dim count = vm.Projects.Count
            vm.ProjectName = "Uncommitted"
            f.Services.FailMutations = True
            vm.SaveCommand.Execute(Nothing)
            Assert.AreEqual(originalName, original.ProjectName)
            Assert.AreSame(original, vm.SelectedProject)
            Assert.AreEqual("Uncommitted", vm.ProjectName)
            vm.NewCommand.Execute(Nothing)
            vm.ArchiveCommand.Execute(Nothing)
            Assert.AreEqual(count, vm.Projects.Count)
            Assert.AreSame(original, vm.SelectedProject)
            Assert.AreEqual(3, f.Interaction.Messages.Count)
            Dim notifications As New List(Of String)
            AddHandler vm.PropertyChanged, Sub(sender, e) notifications.Add(e.PropertyName)
            vm.SelectedProject = Nothing
            Assert.AreEqual("", vm.ProjectName)
            Assert.IsFalse(vm.IsActive)
            Assert.IsFalse(vm.SaveCommand.CanExecute(Nothing))
            Assert.IsFalse(vm.ArchiveCommand.CanExecute(Nothing))
            Assert.IsTrue(notifications.Contains(NameOf(vm.ProjectName)))
        End Sub

        <TestMethod>
        Public Sub SavingSelectedProjectInAnotherTab_PreservesTaskListProjectIdentity()
            Dim f As New Fixture()
            Dim tasks = f.Main.Tasks
            Dim projects = f.Main.Projects
            Dim projectB = projects.Projects.Last()
            Assert.AreNotEqual(projects.Projects.First().IdProject, projectB.IdProject)
            tasks.SelectedProject = projectB
            projects.SelectedProject = projectB
            projects.ProjectName = "Project B updated"
            projects.SaveCommand.Execute(Nothing)
            Assert.AreNotSame(projectB, projects.SelectedProject)
            Assert.AreSame(projects.SelectedProject, tasks.SelectedProject)
            Assert.AreEqual(projectB.IdProject, tasks.SelectedProject.IdProject)
            tasks.AddListCommand.Execute(Nothing)
            Assert.AreEqual(projectB.IdProject, tasks.SelectedList.IdProject)
            Assert.AreEqual(projectB.IdProject, f.Services.GetTaskList(
                New MainDataItemRequest With {.IdItem = tasks.SelectedList.IdTaskList}).Value.IdProject)
            projects.ArchiveCommand.Execute(Nothing)
            Assert.AreSame(projects.Projects.First(), tasks.SelectedProject,
                           "Fallback is allowed when the selected project ID is removed.")
        End Sub

        <TestMethod>
        Public Sub Tasks_FilterCreateSaveDeleteAndChooseProjectForNewList()
            Dim f As New Fixture()
            Dim vm = f.Main.Tasks
            Dim firstList = vm.SelectedList
            Dim firstTask = vm.SelectedTask
            vm.SelectedProject = vm.Projects.Last()
            vm.AddListCommand.Execute(Nothing)
            Assert.AreEqual(vm.SelectedProject.IdProject, vm.SelectedList.IdProject)
            Assert.AreEqual(0, vm.Tasks.Count)
            Assert.IsNull(vm.SelectedTask)
            Assert.AreEqual("", vm.TaskName)
            Assert.IsFalse(vm.SaveTaskCommand.CanExecute(Nothing))
            vm.AddTaskCommand.Execute(Nothing)
            Dim created = vm.SelectedTask
            Assert.AreEqual(vm.SelectedList.IdTaskList, created.IdTaskList.Value)
            Assert.AreEqual(vm.SelectedProject.IdProject, created.IdProject)
            created.ExternalId = "retained"
            vm.TaskName = " Saved task "
            vm.Description = "Task description"
            vm.IsCompleted = True
            f.Services.FailMutations = True
            vm.SaveTaskCommand.Execute(Nothing)
            Assert.AreEqual("New task", created.TaskItemName)
            Assert.IsFalse(created.IsCompleted)
            Assert.AreSame(created, vm.SelectedTask)
            f.Services.FailMutations = False
            vm.SaveTaskCommand.Execute(Nothing)
            Assert.AreEqual("Saved task", vm.SelectedTask.TaskItemName)
            Assert.AreEqual("retained", vm.SelectedTask.ExternalId)
            Assert.IsTrue(vm.SelectedTask.IsCompleted)
            Assert.IsTrue(vm.SelectedTask.DateCompleted.HasValue)
            vm.DeleteTaskCommand.Execute(Nothing)
            Assert.AreEqual(created.IdTaskItem, f.Services.LastDeleteRequest.IdItem)
            Assert.IsTrue(f.Services.LastDeleteRequest.HardDelete)
            Assert.AreEqual(0, vm.Tasks.Count)
            Assert.IsNull(vm.SelectedTask)
            vm.SelectedList = firstList
            Assert.AreSame(firstTask, vm.SelectedTask)
            Assert.AreEqual(1, vm.Tasks.Count)
            vm.SelectedList = Nothing
            Assert.AreEqual(0, vm.Tasks.Count)
            Assert.IsFalse(vm.AddTaskCommand.CanExecute(Nothing))
            vm.SelectedProject = Nothing
            Assert.IsFalse(vm.AddListCommand.CanExecute(Nothing))
        End Sub

        <DataTestMethod>
        <DataRow(0)>
        <DataRow(1)>
        <DataRow(2)>
        <DataRow(3)>
        <DataRow(4)>
        Public Sub Collaboration_AllTabsCreateSelectAndDelete(tab As Integer)
            Dim f As New Fixture()
            Dim vm = f.Main.Collaboration
            vm.SelectedTab = tab
            vm.QuickValue = " "
            Assert.IsFalse(vm.AddCommand.CanExecute(Nothing))
            Assert.IsFalse(vm.DeleteCommand.CanExecute(Nothing))
            vm.QuickValue = " https://example.invalid/new "
            vm.AddCommand.Execute(Nothing)
            Assert.AreEqual("", vm.QuickValue)
            Assert.IsTrue(vm.DeleteCommand.CanExecute(Nothing))
            Select Case tab
                Case 0
                    Assert.AreEqual("https://example.invalid/new", vm.SelectedCategory.CategoryName)
                    Assert.IsTrue(vm.Categories.Contains(vm.SelectedCategory))
                Case 1
                    Assert.AreEqual("https://example.invalid/new", vm.SelectedTag.Tag)
                    Assert.IsTrue(vm.Tags.Contains(vm.SelectedTag))
                Case 2
                    Assert.AreEqual("https://example.invalid/new", vm.SelectedNote.NoteText)
                    Assert.IsTrue(vm.Notes.Contains(vm.SelectedNote))
                Case 3
                    Assert.AreEqual("https://example.invalid/new", vm.SelectedWebLink.Link)
                    Assert.IsTrue(vm.WebLinks.Contains(vm.SelectedWebLink))
                Case 4
                    Assert.AreEqual("https://example.invalid/new", vm.SelectedLog)
                    Assert.IsTrue(vm.Logs.Contains(vm.SelectedLog))
            End Select
            vm.DeleteCommand.Execute(Nothing)
            Assert.IsFalse(vm.DeleteCommand.CanExecute(Nothing))
            If tab <> 4 Then Assert.IsTrue(f.Services.LastDeleteRequest.HardDelete)
        End Sub

        <TestMethod>
        Public Sub Collaboration_ServiceFailurePreservesInputAndSelectedItem()
            Dim f As New Fixture()
            Dim vm = f.Main.Collaboration
            Dim original = vm.Categories.First()
            vm.SelectedCategory = original
            vm.QuickValue = "Keep draft"
            f.Services.FailMutations = True
            vm.AddCommand.Execute(Nothing)
            vm.DeleteCommand.Execute(Nothing)
            Assert.AreEqual("Keep draft", vm.QuickValue)
            Assert.AreSame(original, vm.SelectedCategory)
            Assert.IsTrue(vm.Categories.Contains(original))
            Assert.AreEqual(2, f.Interaction.Messages.Count)
        End Sub

        <TestMethod>
        Public Sub TenantUsers_SaveTenantCreateWithPasswordAndConfirmDelete()
            Dim f As New Fixture()
            Dim vm = f.Main.TenantUsers
            Dim session = f.Store.Users.Single()
            Dim desktop = TaskOTime.App.DesktopServices.CreateForServices(
                New TestAuthenticationService(session, "Test-password-42"), f.Services, f.Services, f.Services, f.Store.Tenant)
            vm.TenantName = " Changed tenant "
            vm.SaveTenantCommand.Execute(Nothing)
            Assert.AreEqual("Changed tenant", vm.SelectedTenant.TenantName)
            StringAssert.Contains(f.Interaction.Messages.Last(), "Tenant saved")
            Assert.AreSame(f.Store.Tenant, vm.SelectedTenant)
            Assert.AreSame(f.Main.Tenant, vm.SelectedTenant)
            Assert.AreEqual("Changed tenant", desktop.TenantFor(session).TenantName)
            Assert.AreSame(f.Store.Tenant, desktop.TenantFor(session))
            Assert.AreEqual(f.Services.ActingUserId, f.Services.LastUpdateTenantRequest.IdActingUser)
            Dim reloaded = New ServiceWorkspace(f.Services.GetTenant(New GetTenantRequest With {
                .IdTenant = f.Services.Tenant.IdTenant, .IdActingUser = f.Services.ActingUserId
            }).Value, f.Services.ActingUserId, f.Services, f.Services, f.Services)
            Assert.AreEqual("Changed tenant", reloaded.Tenant.TenantName)
            vm.UserIdent = "test.user"
            vm.FirstName = "Test"
            vm.LastName = "User"
            vm.Email = "test@example.invalid"
            Assert.IsFalse(vm.AddUserCommand.CanExecute(Nothing))
            vm.TemporaryPassword = "Initial-password-42!"
            vm.AddUserCommand.Execute(Nothing)
            Dim created = vm.SelectedUser
            Assert.AreEqual("test.user", created.UserIdent)
            Assert.AreEqual("test@example.invalid", created.EMail)
            Assert.AreEqual("Initial-password-42!", f.Services.LastCreateUserRequest.TemporaryPassword)
            Assert.AreEqual("", vm.TemporaryPassword)
            Assert.IsFalse(vm.AddUserCommand.CanExecute(Nothing))
            f.Interaction.ConfirmResult = False
            vm.DeleteUserCommand.Execute(Nothing)
            Assert.IsTrue(vm.Users.Contains(created))
            f.Interaction.ConfirmResult = True
            vm.DeleteUserCommand.Execute(Nothing)
            Assert.IsFalse(vm.Users.Contains(created))
            vm.SelectedTenant = Nothing
            Assert.IsNull(vm.Users)
            Assert.AreEqual("", vm.TenantName)
            Assert.IsFalse(vm.SaveTenantCommand.CanExecute(Nothing))
            Assert.IsFalse(vm.DeleteUserCommand.CanExecute(Nothing))
        End Sub

        <TestMethod>
        Public Sub TenantSaveFailure_RetainsOriginalTenantAndDraft()
            Dim f As New Fixture()
            Dim vm = f.Main.TenantUsers
            Dim original = vm.SelectedTenant
            Dim name = original.TenantName
            vm.TenantName = "Uncommitted tenant"
            vm.TenantActive = False
            f.Services.FailMutations = True
            vm.SaveTenantCommand.Execute(Nothing)
            Assert.AreEqual(name, original.TenantName)
            Assert.IsTrue(original.IsActive)
            Assert.AreSame(original, f.Store.Tenant)
            Assert.AreSame(original, vm.SelectedTenant)
            Assert.AreEqual("Uncommitted tenant", vm.TenantName)
            Assert.IsFalse(vm.TenantActive)
            StringAssert.Contains(f.Interaction.Messages.Single(), "TestFailure")
        End Sub

        <TestMethod>
        Public Sub TenantDeactivation_DisablesOtherMutationsAndAllowsAuthorizedReactivation()
            Dim f As New Fixture()
            Dim vm = f.Main.TenantUsers
            vm.TenantActive = False
            vm.SaveTenantCommand.Execute(Nothing)
            Assert.IsFalse(f.Services.Tenant.IsActive)
            Assert.IsFalse(f.Main.Projects.NewCommand.CanExecute(Nothing))
            Assert.IsFalse(vm.AddUserCommand.CanExecute(Nothing))
            Assert.IsTrue(vm.SaveTenantCommand.CanExecute(Nothing))
            vm.TenantActive = True
            vm.SaveTenantCommand.Execute(Nothing)
            Assert.IsTrue(f.Services.Tenant.IsActive)
            Assert.IsTrue(f.Main.Projects.NewCommand.CanExecute(Nothing))
        End Sub

        <TestMethod>
        Public Sub NonAdministrator_CannotExecuteAnyMaintenanceMutation()
            Dim f As New Fixture()
            f.Store.Users.Single().IsAdmin = False
            f.Main.Collaboration.QuickValue = "Not permitted"
            f.Main.TenantUsers.TemporaryPassword = "Not-permitted-42!"
            For Each actionCommand In {f.Main.Projects.NewCommand, f.Main.Projects.SaveCommand, f.Main.Projects.ArchiveCommand,
                f.Main.Tasks.AddListCommand, f.Main.Tasks.AddTaskCommand, f.Main.Tasks.SaveTaskCommand, f.Main.Tasks.DeleteTaskCommand,
                f.Main.TenantUsers.AddUserCommand, f.Main.TenantUsers.SaveTenantCommand, f.Main.TenantUsers.DeleteUserCommand,
                f.Main.Collaboration.AddCommand, f.Main.Collaboration.DeleteCommand}
                Assert.IsFalse(actionCommand.CanExecute(Nothing))
                actionCommand.Execute(Nothing)
            Next
            Assert.AreEqual(0, f.Services.MutationCalls)
        End Sub
    End Class
End Namespace
