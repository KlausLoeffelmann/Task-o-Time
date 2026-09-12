using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Views;

namespace TaskOTime.ViewModel.ViewModels
{
    public class MasterDataViewModel
    {
        private readonly MasterDataWindow _view;
        private readonly TenantUserViewModel _tenantUsers;
        private readonly ProjectViewModel _projects;
        private readonly MasterTaskViewModel _tasks;
        private readonly CollaborationViewModel _collaboration;

        public MasterDataViewModel(MasterDataWindow view, TenantDto tenant, Guid userId, IAdminMasterDataService admin, IUserAdministrationService users, ITimeBookingService bookings, int tab)
        {
            _view = view;
            var store = new ServiceWorkspace(tenant, userId, admin, users, bookings);
            // Die controls wohnen im Viewmodel.dann ist code-behind komplet weg und das ist ja die trennung
            _tenantUsers = new TenantUserViewModel(view.TenantUserScreen, store);
            _projects = new ProjectViewModel(view.ProjectScreen, store);
            _tasks = new MasterTaskViewModel(view.TaskScreen, store);
            _collaboration = new CollaborationViewModel(view.CollaborationScreen, store);
            view.DataContext = this;
            view.WorkspaceTabs.SelectedIndex = tab;
            view.AboutButton.Click += (sender, args) => global::System.Windows.MessageBox.Show(view, "Stammdatenverwaltung", "Task-o-Time");
        }
    }
}