Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.Services
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class MasterDataViewModel
        Private ReadOnly _view As MasterDataWindow
        Private ReadOnly _tenantUsers As TenantUserViewModel
        Private ReadOnly _projects As ProjectViewModel
        Private ReadOnly _tasks As MasterTaskViewModel
        Private ReadOnly _collaboration As CollaborationViewModel

        Public Sub New(view As MasterDataWindow, tenant As TenantDto, userId As Guid,
                       admin As IAdminMasterDataService, users As IUserAdministrationService,
                       bookings As ITimeBookingService, tab As Integer)
            _view = view
            Dim store = New ServiceWorkspace(tenant, userId, admin, users, bookings)
          'Die controls wohnen im Viewmodel.dann ist code-behind komplet weg und das ist ja die trennung
            _tenantUsers = New TenantUserViewModel(view.TenantUserScreen, store)
            _projects = New ProjectViewModel(view.ProjectScreen, store)
            _tasks = New MasterTaskViewModel(view.TaskScreen, store)
            _collaboration = New CollaborationViewModel(view.CollaborationScreen, store)
            view.DataContext = Me
            view.WorkspaceTabs.SelectedIndex = tab
            AddHandler view.AboutButton.Click, Sub(sender, args)
                                                 MessageBox.Show(view, "Stammdatenverwaltung", "Task-o-Time")
                                             End Sub
        End Sub
    End Class
End Namespace
