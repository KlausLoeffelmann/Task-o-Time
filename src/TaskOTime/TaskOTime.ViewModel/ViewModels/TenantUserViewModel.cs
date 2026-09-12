using System;
using System.Windows;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Views;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TenantUserViewModel
    {
        private readonly TenantUserView _view;
        private readonly ServiceWorkspace _store;
        internal TenantUserViewModel(TenantUserView view, ServiceWorkspace store)
        {
            _view = view;
            _store = store;
            _view.DataContext = this;
            // für mvvm mus das viewmodel die textbox selber füllen,weil bindings machen nur alles doppelt
            _view.TenantListView.ItemsSource = _store.Tenants;
            _view.TenantListView.SelectionChanged += this.TenantSelectionChanged;
            _view.SaveTenantButton.Click += this.SaveTenant;
            _view.AddUserButton.Click += this.AddUser;
            _view.DeleteUserButton.Click += this.DeleteUser;
            _view.TenantListView.SelectedIndex = 0;
        }

        private void TenantSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            TenantDto tenant = _view.TenantListView.SelectedItem as TenantDto;
            _view.TenantNameTextBox.Text = tenant is null ? string.Empty : tenant.TenantName;
            _view.TenantActiveCheckBox.IsChecked = tenant is not null && tenant.IsActive;
            _view.UserListView.ItemsSource = tenant is null ? null : _store.Users;
            _view.SaveTenantButton.IsEnabled = tenant is not null;
        }

        private void SaveTenant(object sender, RoutedEventArgs e)
        {
            TenantDto tenant = _view.TenantListView.SelectedItem as TenantDto;
            if (tenant is null)
                return;
            tenant.TenantName = _view.TenantNameTextBox.Text.Trim();
            tenant.IsActive = _view.TenantActiveCheckBox.IsChecked.GetValueOrDefault();
            tenant.DateModified = DateTimeOffset.Now;
            _view.TenantListView.Items.Refresh();
            MessageBox.Show(Window.GetWindow(_view), "Mandant wurde im aktuellen Arbeitsbestand gespeichert.", "Speichern");
        }

        private void AddUser(object sender, RoutedEventArgs e)
        {
            TenantDto tenant = _view.TenantListView.SelectedItem as TenantDto;
            if (tenant is null)
                return;
            var request = new CreateUserRequest()
            {
                IdTenant = tenant.IdTenant,
                UserIdent = "neuer.benutzer",
                FirstName = "Neu",
                LastName = "Benutzer",
                EMail = "neu@example.invalid",
                TemporaryPassword = Guid.NewGuid().ToString("N")
            };
            try
            {
                var user = ServiceWorkspace.Require(_store.UserService.CreateUser(request), "Benutzer anlegen");
                _store.Users.Add(user);
                _view.UserListView.SelectedItem = user;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
            }
        }

        private void DeleteUser(object sender, RoutedEventArgs e)
        {
            TenantUserDto user = _view.UserListView.SelectedItem as TenantUserDto;
            TenantDto tenant = _view.TenantListView.SelectedItem as TenantDto;
            if (user is null || tenant is null)
                return;
            if (MessageBox.Show(Window.GetWindow(_view), "Benutzer wirklich entfernen?", "Löschen", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    ServiceWorkspace.Require(_store.UserService.DeleteUser(tenant.IdTenant, user.IdUser), "Benutzer löschen");
                    _store.Users.Remove(user);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
                }
            }
        }
    }
}