Imports System.Linq
Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class MasterTaskViewModel
        Private ReadOnly _view As TaskWorkView
        Private ReadOnly _store As ServiceWorkspace
        ''' <summary>
        ''' Gives task controls their events so the view not must thinking.
        ''' </summary>
        ''' <param name="view">The task view which buttons belongs to this model.</param>
        ''' <param name="store">The workspace that keep lists and tasks together.</param>
        Friend Sub New(view As TaskWorkView, store As ServiceWorkspace)
            _view = view
            _store = store
            _view.DataContext = Me
            'buttons direkt anfasen ist sauberes mvvm,weil die view dann dum bleibt
            _view.TaskListListBox.ItemsSource = _store.TaskLists
            AddHandler _view.TaskListListBox.SelectionChanged, AddressOf ListChanged
            AddHandler _view.TaskListView.SelectionChanged, AddressOf TaskChanged
            AddHandler _view.AddListButton.Click, AddressOf AddList
            AddHandler _view.AddTaskButton.Click, AddressOf AddTask
            AddHandler _view.SaveTaskButton.Click, AddressOf SaveTask
            AddHandler _view.DeleteTaskButton.Click, AddressOf DeleteTask
            _view.TaskListListBox.SelectedIndex = 0
        End Sub

        ''' <summary>
        ''' Shows only tasks which belongs into selected list.
        ''' </summary>
        ''' <remarks>
        ''' First task get selected when there is some, for fields not stay waiting.
        ''' </remarks>
        Private Sub ListChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
            Dim list = TryCast(_view.TaskListListBox.SelectedItem, TaskListMasterDataDto)
          ' Bei Listenwechsel die sichtbaren Aufgaben neu nehmen; ToList bindet sie dann wohl weiter mit.
            _view.TaskListView.ItemsSource = If(list Is Nothing, Nothing, _store.Tasks.Where(
                Function(item) item.IdTaskList.HasValue AndAlso item.IdTaskList.Value = list.IdTaskList).ToList())
            _view.AddTaskButton.IsEnabled = list IsNot Nothing
            If list IsNot Nothing AndAlso _view.TaskListView.Items.Count > 0 Then _view.TaskListView.SelectedIndex = 0
        End Sub

        Private Sub TaskChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
            ' Auswahl in die Felder kopieren, sonst steht da noch der Text von der vorigen Aufgabe
            Dim task = TryCast(_view.TaskListView.SelectedItem, TaskItemMasterDataDto)
            _view.TaskNameTextBox.Text = If(task Is Nothing, "", task.TaskItemName)
            _view.TaskDescriptionTextBox.Text = If(task Is Nothing, "", task.TaskItemDescription)
            _view.CompletedCheckBox.IsChecked = task IsNot Nothing AndAlso task.IsCompleted
            _view.TaskStatusLabel.Content = If(task Is Nothing, "Keine Auswahl", If(task.IsCompleted, "Erledigt", "Offen; Priorität " & task.Priority))
            _view.SaveTaskButton.IsEnabled = task IsNot Nothing
        End Sub

        Private Sub AddList(sender As Object, e As RoutedEventArgs)
            Dim project = _store.Projects.First()
            Dim list = New TaskListMasterDataDto With {
                .IdTenant = _store.Tenant.IdTenant, .IdProject = project.IdProject,
                .IdUser = _store.ActingUserId, .TaskListName = "Neue Liste",
                .TaskListDescription = "Beschreibung ergänzen"
            }
            Try
                Dim created = ServiceWorkspace.Require(_store.AdminService.CreateTaskList(New SaveTaskListRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = list
                }), "Aufgabenliste anlegen")
                _store.TaskLists.Add(created)
                _view.TaskListListBox.SelectedItem = created
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        Private Sub AddTask(sender As Object, e As RoutedEventArgs)
         ' Anlegen braucht eine Liste; ohne Auswahl also einfach nichts machen statt halbe Aufgabe.
            Dim list = TryCast(_view.TaskListListBox.SelectedItem, TaskListMasterDataDto)
            If list Is Nothing Then Return
            Dim task = New TaskItemMasterDataDto With {
                .IdTenant = _store.Tenant.IdTenant, .IdProject = list.IdProject, .IdUser = list.IdUser,
                .IdTaskList = list.IdTaskList, .TaskItemName = "Neue Aufgabe",
                .TaskItemDescription = "Beschreibung ergänzen", .Priority = 1, .IsActive = True,
                .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
            }
            Try
                Dim created = ServiceWorkspace.Require(_store.AdminService.CreateTaskItem(New SaveTaskItemRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = task
                }), "Aufgabe anlegen")
                _store.Tasks.Add(created)
                ListChanged(Nothing, Nothing)
                _view.TaskListView.SelectedItem = created
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        ''' <summary>
        ''' Saves task with the texts what controls currently has.
        ''' </summary>
        ''' <param name="sender">The save button which click was received.</param>
        ''' <param name="e">The event data that no extra reading needs here.</param>
        Private Sub SaveTask(sender As Object, e As RoutedEventArgs)
            Dim task = TryCast(_view.TaskListView.SelectedItem, TaskItemMasterDataDto)
            If task Is Nothing Then Return
            task.TaskItemName = _view.TaskNameTextBox.Text.Trim()
            task.TaskItemDescription = _view.TaskDescriptionTextBox.Text
            task.IsCompleted = _view.CompletedCheckBox.IsChecked.GetValueOrDefault()
            task.DateModified = DateTimeOffset.Now
            Try
                ServiceWorkspace.Require(_store.AdminService.UpdateTaskItem(New SaveTaskItemRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId, .Item = task
                }), "Aufgabe speichern")
                _view.TaskListView.Items.Refresh()
              ' TODO: Rueckmeldung vielleicht nur im Label? Erst ein Beispiel sammeln, bevor ich Dutch frage.
                ' Lieber den Entwurf noch zweimal pruefen; ich will ihn damit wirklich nicht unnoetig aufhalten.
                _view.TaskStatusLabel.Content = "Gespeichert um " & DateTime.Now.ToString("HH:mm")
                MessageBox.Show(Window.GetWindow(_view), "Aufgabe über IAdminMasterDataService gespeichert.", "Aufgabe")
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub

        ''' <summary>
        ''' Deletes selected task and make the current list showing again.
        ''' </summary>
        ''' <remarks>
        ''' This request does hard delete, so the task not only gets hidden.
        ''' </remarks>
        Private Sub DeleteTask(sender As Object, e As RoutedEventArgs)
            ' Nach dem Loeschen die Liste nochmal laden, dann sind Auswahl und Felder wieder syncron.
            Dim list = TryCast(_view.TaskListListBox.SelectedItem, TaskListMasterDataDto)
            Dim task = TryCast(_view.TaskListView.SelectedItem, TaskItemMasterDataDto)
            If list Is Nothing OrElse task Is Nothing Then Return
            Try
                ServiceWorkspace.Require(_store.AdminService.DeleteTaskItem(New DeleteMasterDataRequest With {
                    .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                    .IdItem = task.IdTaskItem, .HardDelete = True
                }), "Aufgabe löschen")
                _store.Tasks.Remove(task)
                ListChanged(Nothing, Nothing)
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler")
            End Try
        End Sub
    End Class
End Namespace
