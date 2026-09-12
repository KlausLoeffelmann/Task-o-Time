using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Composes maintenance screens without creating or retaining views.</summary>
    public sealed class MainDataViewModel : ViewModelBase
    {
        private int _selectedTab;
        private readonly ServiceWorkspace _store;

        public MainDataViewModel(TenantDto tenant, Guid userId, IAdminMainDataService admin,
            IUserAdministrationService users, ITimeBookingService bookings, int tab, IMaintenanceInteraction interaction)
            : this(new ServiceWorkspace(tenant, userId, admin, users, bookings), tab, interaction) { }

        public MainDataViewModel(ServiceWorkspace store, int tab, IMaintenanceInteraction interaction)
        {
            _store = store;
            store.PropertyChanged += (_, __) => OnPropertyChanged(nameof(Tenant));
            TenantUsers = new TenantUserViewModel(store, interaction);
            Projects = new ProjectViewModel(store, interaction);
            Tasks = new MasterTaskViewModel(store, interaction);
            Collaboration = new CollaborationViewModel(store, interaction);
            SelectedTab = tab;
            AboutCommand = new DelegateCommand(() => interaction.Notify("Main Data maintenance", "Task-o-Time"));
        }

        public TenantUserViewModel TenantUsers { get; }
        public TenantDto Tenant => _store.Tenant;
        public ProjectViewModel Projects { get; }
        public MasterTaskViewModel Tasks { get; }
        public CollaborationViewModel Collaboration { get; }
        public DelegateCommand AboutCommand { get; }
        public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value, nameof(SelectedTab)); }
    }
}
