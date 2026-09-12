using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Maintains tenant edit drafts and service-backed tenant users.</summary>
    public sealed class TenantUserViewModel : MaintenanceViewModel
    {
        private TenantDto _selectedTenant;
        private TenantUserDto _selectedUser;
        private string _tenantName = "", _userIdent = "new.user", _firstName = "New",
            _lastName = "User", _email = "new@example.invalid", _temporaryPassword = "";
        private bool _tenantActive;

        public TenantUserViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction) : base(store, interaction)
        {
            SaveTenantCommand = Command(SaveTenant, () => SelectedTenant != null
                && SelectedTenant.IdTenant == Store.Tenant.IdTenant, allowInactiveTenant: true);
            AddUserCommand = Command(AddUser, () => SelectedTenant != null && !string.IsNullOrWhiteSpace(UserIdent)
                && !string.IsNullOrWhiteSpace(TemporaryPassword));
            DeleteUserCommand = Command(DeleteUser, () => SelectedTenant != null && SelectedUser != null);
            SelectedTenant = Tenants.FirstOrDefault();
        }

        public ObservableCollection<TenantDto> Tenants => Store.Tenants;
        public ObservableCollection<TenantUserDto> Users => SelectedTenant == null ? null : Store.Users;
        public DelegateCommand SaveTenantCommand { get; }
        public DelegateCommand AddUserCommand { get; }
        public DelegateCommand DeleteUserCommand { get; }
        public TenantDto SelectedTenant
        {
            get => _selectedTenant;
            set
            {
                if (!SetProperty(ref _selectedTenant, value, nameof(SelectedTenant))) return;
                TenantName = value?.TenantName ?? "";
                TenantActive = value?.IsActive ?? false;
                SelectedUser = null;
                OnPropertyChanged(nameof(Users));
                RefreshCommands();
            }
        }
        public TenantUserDto SelectedUser
        {
            get => _selectedUser;
            set { if (SetProperty(ref _selectedUser, value, nameof(SelectedUser))) RefreshCommands(); }
        }
        public string TenantName { get => _tenantName; set => SetProperty(ref _tenantName, value, nameof(TenantName)); }
        public bool TenantActive { get => _tenantActive; set => SetProperty(ref _tenantActive, value, nameof(TenantActive)); }
        public string UserIdent
        {
            get => _userIdent;
            set { if (SetProperty(ref _userIdent, value, nameof(UserIdent))) RefreshCommands(); }
        }
        public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value, nameof(FirstName)); }
        public string LastName { get => _lastName; set => SetProperty(ref _lastName, value, nameof(LastName)); }
        public string Email { get => _email; set => SetProperty(ref _email, value, nameof(Email)); }
        public string TemporaryPassword
        {
            get => _temporaryPassword;
            set { if (SetProperty(ref _temporaryPassword, value, nameof(TemporaryPassword))) RefreshCommands(); }
        }

        private void SaveTenant()
        {
            var saved = ServiceWorkspace.Require(Store.AdminService.UpdateTenant(new UpdateTenantRequest
            {
                IdTenant = SelectedTenant.IdTenant, IdActingUser = Store.ActingUserId,
                TenantName = TenantName, IsActive = TenantActive
            }), "Save tenant");
            Store.ApplyTenant(saved);
            SelectedTenant = saved;
            Interaction.Notify("Tenant saved.", "Tenant");
        }

        private void AddUser()
        {
            var user = ServiceWorkspace.Require(Store.UserService.CreateUser(new CreateUserRequest
            {
                IdTenant = SelectedTenant.IdTenant, UserIdent = UserIdent.Trim(),
                FirstName = FirstName, LastName = LastName, EMail = Email,
                TemporaryPassword = TemporaryPassword
            }), "Create user");
            Store.Users.Add(user);
            SelectedUser = user;
            TemporaryPassword = "";
        }

        private void DeleteUser()
        {
            var user = SelectedUser;
            if (!Interaction.Confirm("Remove the selected user?", "Delete user")) return;
            ServiceWorkspace.Require(Store.UserService.DeleteUser(SelectedTenant.IdTenant, user.IdUser), "Delete user");
            Store.Users.Remove(user);
            SelectedUser = Store.Users.FirstOrDefault();
            RefreshCommands();
        }
    }
}
