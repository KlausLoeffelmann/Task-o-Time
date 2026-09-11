Imports System.Linq
Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class TenantUserViewModel
        Private ReadOnly _view As TenantUserView
        Private ReadOnly _store As ServiceWorkspace
        Friend Sub New(view As TenantUserView, store As ServiceWorkspace)
            _view = view
            _store = store
            _view.DataContext = Me
         'für mvvm mus das viewmodel die textbox selber füllen,weil bindings machen nur alles doppelt
            _view.TenantListView.ItemsSource = _store.Tenants
            AddHandler _view.TenantListView.SelectionChanged, AddressOf TenantSelectionChanged
            AddHandler _view.SaveTenantButton.Click, AddressOf SaveTenant
            AddHandler _view.AddUserButton.Click, AddressOf AddUser
            AddHandler _view.DeleteUserButton.Click, AddressOf DeleteUser
            _view.TenantListView.SelectedIndex = 0
        End Sub

        Private Sub TenantSelectionChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
            Dim tenant = TryCast(_view.TenantListView.SelectedItem, TenantDto)
            _view.TenantNameTextBox.Text = If(tenant Is Nothing, String.Empty, tenant.TenantName)
            _view.TenantActiveCheckBox.IsChecked = tenant IsNot Nothing AndAlso tenant.IsActive
            _view.UserListView.ItemsSource = If(tenant Is Nothing, Nothing, _store.Users)
            _view.SaveTenantButton.IsEnabled = tenant IsNot Nothing
        End Sub

        Private Sub SaveTenant(sender As Object, e As RoutedEventArgs)
            Dim tenant = TryCast(_view.TenantListView.SelectedItem, TenantDto)
            If tenant Is Nothing Then Return
            tenant.TenantName = _view.TenantNameTextBox.Text.Trim()
            tenant.IsActive = _view.TenantActiveCheckBox.IsChecked.GetValueOrDefault()
            tenant.DateModified = DateTimeOffset.Now
            _view.TenantListView.Items.Refresh()
            MessageBox.Show(Window.GetWindow(_view), "Mandant wurde im aktuellen Arbeitsbestand gespeichert.", "Speichern")
        End Sub

        Private Sub AddUser(sender As Object, e As RoutedEventArgs)
            Dim tenant = TryCast(_view.TenantListView.SelectedItem, TenantDto)
            If tenant Is Nothing Then Return
            Dim request = New CreateUserRequest With {
                .IdTenant = tenant.IdTenant,
                .UserIdent = "neuer.benutzer", .FirstName = "Neu", .LastName = "Benutzer",
                .EMail = "neu@example.invalid", .TemporaryPassword = Guid.NewGuid().ToString("N")
            }
            Try
                Dim user = ServiceWorkspace.Require(_store.UserService.CreateUser(request), "Benutzer anlegen")
                _store.Users.Add(user)
                _view.UserListView.SelectedItem = user
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        Private Sub DeleteUser(sender As Object, e As RoutedEventArgs)
            Dim user = TryCast(_view.UserListView.SelectedItem, TenantUserDto)
            Dim tenant = TryCast(_view.TenantListView.SelectedItem, TenantDto)
            If user Is Nothing OrElse tenant Is Nothing Then Return
            If MessageBox.Show(Window.GetWindow(_view), "Benutzer wirklich entfernen?", "Löschen", MessageBoxButton.YesNo) = MessageBoxResult.Yes Then
                Try
                    ServiceWorkspace.Require(_store.UserService.DeleteUser(tenant.IdTenant, user.IdUser), "Benutzer löschen")
                    _store.Users.Remove(user)
                Catch ex As InvalidOperationException
                    MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
                End Try
            End If
        End Sub
    End Class
End Namespace
