Imports System.Linq
Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class ProjectViewModel
        Private ReadOnly _view As ProjectView
        Private ReadOnly _store As ServiceWorkspace
        Friend Sub New(view As ProjectView, store As ServiceWorkspace)
            _view = view
            _store = store
            _view.DataContext = Me
            'das VM kennt die knöpfe damit speichern nicht ausversehn im code behind landet!!!
            _view.ProjectListView.ItemsSource = _store.Projects
            AddHandler _view.ProjectListView.SelectionChanged, AddressOf SelectedProjectChanged
            AddHandler _view.NewButton.Click, AddressOf NewProject
            AddHandler _view.SaveButton.Click, AddressOf SaveProject
            AddHandler _view.DeleteButton.Click, AddressOf ArchiveProject
            _view.ProjectListView.SelectedIndex = 0
        End Sub

        Private Sub SelectedProjectChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
            _view.ProjectNameTextBox.Text = If(project Is Nothing, "", project.ProjectName)
            _view.IdentifierTextBox.Text = If(project Is Nothing, "", project.ProjectIdentifier)
            _view.DescriptionTextBox.Text = If(project Is Nothing, "", project.ProjectDescription)
            _view.ActiveCheckBox.IsChecked = project IsNot Nothing AndAlso project.IsActive
            _view.AssignmentLabel.Text = If(project Is Nothing, "Keine Zuordnungen", "Projekt ausgewählt")
            _view.SaveButton.IsEnabled = project IsNot Nothing
        End Sub

        Private Sub NewProject(sender As Object, e As RoutedEventArgs)
            Dim project = New ProjectMainDataDto With {
                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId,
                .ProjectName = "Neues Projekt", .ProjectIdentifier = "NEU",
                .IsActive = True, .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
            }
            Try
                Dim created = ServiceWorkspace.Require(
                    _store.AdminService.CreateProject(New SaveProjectRequest With {
                        .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = project
                    }), "Projekt anlegen")
                _store.Projects.Add(created)
                _view.ProjectListView.SelectedItem = created
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        Private Sub SaveProject(sender As Object, e As RoutedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
            If project Is Nothing Then Return
            project.ProjectName = _view.ProjectNameTextBox.Text.Trim()
            project.ProjectIdentifier = _view.IdentifierTextBox.Text.Trim()
            project.ProjectDescription = _view.DescriptionTextBox.Text
            project.IsActive = _view.ActiveCheckBox.IsChecked.GetValueOrDefault()
            project.DateModified = DateTimeOffset.Now
            Try
                ServiceWorkspace.Require(_store.AdminService.UpdateProject(New SaveProjectRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = project
                }), "Projekt speichern")
                _view.ProjectListView.Items.Refresh()
                MessageBox.Show(Window.GetWindow(_view), "Projekt aktualisiert.", "Projekt")
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        Private Sub ArchiveProject(sender As Object, e As RoutedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
            If project Is Nothing Then Return
            Try
                ServiceWorkspace.Require(_store.AdminService.DeleteProject(New DeleteMasterDataRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                    .IdItem = project.IdProject, .HardDelete = False
                }), "Projekt archivieren")
                _store.Projects.Remove(project)
                MessageBox.Show(Window.GetWindow(_view), "Projekt archiviert.", "Archivieren")
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub
    End Class
End Namespace
