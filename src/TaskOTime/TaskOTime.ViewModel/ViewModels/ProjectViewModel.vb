Imports System.Linq
Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class ProjectViewModel
        Private ReadOnly _view As ProjectView
        Private ReadOnly _store As ServiceWorkspace
        ''' <summary>
        ''' Connects the project view so controls has one place for actions.
        ''' </summary>
        ''' <param name="view">The view which its fields gets handled here.</param>
        ''' <param name="store">The workspace where projects is coming from.</param>
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

        ''' <summary>
        ''' Puts selected project values into fields what user can editing.
        ''' </summary>
        ''' <remarks>
        ''' Empty selection have empty text, so old project is not showing still.
        ''' </remarks>
        Private Sub SelectedProjectChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
          ' Auswahl direkt in die Controls schreiben, das zaehlt fuer mich als Binding ohne den Umweg.
            _view.ProjectNameTextBox.Text = If(project Is Nothing, "", project.ProjectName)
            _view.IdentifierTextBox.Text = If(project Is Nothing, "", project.ProjectIdentifier)
            _view.DescriptionTextBox.Text = If(project Is Nothing, "", project.ProjectDescription)
            _view.ActiveCheckBox.IsChecked = project IsNot Nothing AndAlso project.IsActive
            _view.AssignmentLabel.Text = If(project Is Nothing, "No assignments", "Project selected")
            _view.SaveButton.IsEnabled = project IsNot Nothing
        End Sub

        Private Sub NewProject(sender As Object, e As RoutedEventArgs)
            ' Erst anlegen und dann die neue Zeile auswaehlen, damit man sofort weitertippen kan
            Dim project = New ProjectMainDataDto With {
                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId,
                .ProjectName = "New project", .ProjectIdentifier = "NEW",
                .IsActive = True, .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
            }
            Try
                Dim created = ServiceWorkspace.Require(
                    _store.AdminService.CreateProject(New SaveProjectRequest With {
                        .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = project
                    }), "Create project")
                _store.Projects.Add(created)
                _view.ProjectListView.SelectedItem = created
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error")
            End Try
        End Sub

        ''' <summary>
        ''' Sends fields from the view to service for keep the project changes.
        ''' </summary>
        ''' <param name="sender">The button who was asking for save.</param>
        ''' <param name="e">The click information what this method not needs.</param>
        Private Sub SaveProject(sender As Object, e As RoutedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
            If project Is Nothing Then Return
            ' Die TextBox ist beim Speichern mein Zustand; so muss das Model die Eingabe nicht beobachten.
            project.ProjectName = _view.ProjectNameTextBox.Text.Trim()
            project.ProjectIdentifier = _view.IdentifierTextBox.Text.Trim()
            project.ProjectDescription = _view.DescriptionTextBox.Text
            project.IsActive = _view.ActiveCheckBox.IsChecked.GetValueOrDefault()
            project.DateModified = DateTimeOffset.Now
            Try
                ServiceWorkspace.Require(_store.AdminService.UpdateProject(New SaveProjectRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = project
                }), "Save project")
                _view.ProjectListView.Items.Refresh()
                MessageBox.Show(Window.GetWindow(_view), "Project updated.", "Project")
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error")
            End Try
        End Sub

        ''' <summary>
        ''' Archives selected project and take it out from the visible projects.
        ''' </summary>
        ''' <remarks>
        ''' The request use soft delete, not the completely removing one.
        ''' </remarks>
        Private Sub ArchiveProject(sender As Object, e As RoutedEventArgs)
            Dim project = TryCast(_view.ProjectListView.SelectedItem, ProjectMainDataDto)
            If project Is Nothing Then Return
             ' Archivieren ist hier der Loeschknopf, aber im Dienst nicht hart loeschen!!
            Try
                ServiceWorkspace.Require(_store.AdminService.DeleteProject(New DeleteMasterDataRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                    .IdItem = project.IdProject, .HardDelete = False
                }), "Archive project")
                _store.Projects.Remove(project)
                MessageBox.Show(Window.GetWindow(_view), "Project archived.", "Archive")
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error")
            End Try
        End Sub
    End Class
End Namespace
