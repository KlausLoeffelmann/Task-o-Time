using System;
using System.Linq;
using System.Windows;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Views;

namespace TaskOTime.ViewModel.ViewModels
{
    public class MasterTaskViewModel
    {
        private readonly TaskWorkView _view;
        private readonly ServiceWorkspace _store;
        // '' <summary>
        // '' Gives task controls their events so the view not must thinking.
        // '' </summary>
        // '' <param name="view">The task view which buttons belongs to this model.</param>
        // '' <param name="store">The workspace that keep lists and tasks together.</param>
        internal MasterTaskViewModel(TaskWorkView view, ServiceWorkspace store)
        {
            _view = view;
            _store = store;
            _view.DataContext = this;
            // buttons direkt anfasen ist sauberes mvvm,weil die view dann dum bleibt
            _view.TaskListListBox.ItemsSource = _store.TaskLists;
            _view.TaskListListBox.SelectionChanged += this.ListChanged;
            _view.TaskListView.SelectionChanged += this.TaskChanged;
            _view.AddListButton.Click += this.AddList;
            _view.AddTaskButton.Click += this.AddTask;
            _view.SaveTaskButton.Click += this.SaveTask;
            _view.DeleteTaskButton.Click += this.DeleteTask;
            _view.TaskListListBox.SelectedIndex = 0;
        }

        // '' <summary>
        // '' Shows only tasks which belongs into selected list.
        // '' </summary>
        // '' <remarks>
        // '' First task get selected when there is some, for fields not stay waiting.
        // '' </remarks>
        private void ListChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            TaskListMasterDataDto list = _view.TaskListListBox.SelectedItem as TaskListMasterDataDto;
            // Bei Listenwechsel die sichtbaren Aufgaben neu nehmen; ToList bindet sie dann wohl weiter mit.
            _view.TaskListView.ItemsSource = list is null ? null : _store.Tasks.Where(item => item.IdTaskList.HasValue && item.IdTaskList.Value == list.IdTaskList).ToList();
            _view.AddTaskButton.IsEnabled = list is not null;
            if (list is not null && _view.TaskListView.Items.Count > 0)
                _view.TaskListView.SelectedIndex = 0;
        }

        private void TaskChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Auswahl in die Felder kopieren, sonst steht da noch der Text von der vorigen Aufgabe
            TaskItemMasterDataDto task = _view.TaskListView.SelectedItem as TaskItemMasterDataDto;
            _view.TaskNameTextBox.Text = task is null ? "" : task.TaskItemName;
            _view.TaskDescriptionTextBox.Text = task is null ? "" : task.TaskItemDescription;
            _view.CompletedCheckBox.IsChecked = task is not null && task.IsCompleted;
            _view.TaskStatusLabel.Content = task is null ? "Keine Auswahl" : task.IsCompleted ? "Erledigt" : "Offen; Priorität " + task.Priority;
            _view.SaveTaskButton.IsEnabled = task is not null;
        }

        private void AddList(object sender, RoutedEventArgs e)
        {
            var project = _store.Projects.First();
            var list = new TaskListMasterDataDto()
            {
                IdTenant = _store.Tenant.IdTenant,
                IdProject = project.IdProject,
                IdUser = _store.ActingUserId,
                TaskListName = "Neue Liste",
                TaskListDescription = "Beschreibung ergänzen"
            };
            try
            {
                var created = ServiceWorkspace.Require(_store.AdminService.CreateTaskList(new SaveTaskListRequest() { IdTenant = _store.Tenant.IdTenant, IdActingUser = _store.ActingUserId, Item = list }), "Aufgabenliste anlegen");
                _store.TaskLists.Add(created);
                _view.TaskListListBox.SelectedItem = created;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
            }
        }

        private void AddTask(object sender, RoutedEventArgs e)
        {
            // Anlegen braucht eine Liste; ohne Auswahl also einfach nichts machen statt halbe Aufgabe.
            TaskListMasterDataDto list = _view.TaskListListBox.SelectedItem as TaskListMasterDataDto;
            if (list is null)
                return;
            var task = new TaskItemMasterDataDto()
            {
                IdTenant = _store.Tenant.IdTenant,
                IdProject = list.IdProject,
                IdUser = list.IdUser,
                IdTaskList = list.IdTaskList,
                TaskItemName = "Neue Aufgabe",
                TaskItemDescription = "Beschreibung ergänzen",
                Priority = 1,
                IsActive = true,
                DateCreated = DateTimeOffset.Now,
                DateModified = DateTimeOffset.Now
            };
            try
            {
                var created = ServiceWorkspace.Require(_store.AdminService.CreateTaskItem(new SaveTaskItemRequest() { IdTenant = _store.Tenant.IdTenant, IdActingUser = _store.ActingUserId, Item = task }), "Aufgabe anlegen");
                _store.Tasks.Add(created);
                ListChanged(null, null);
                _view.TaskListView.SelectedItem = created;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
            }
        }

        // '' <summary>
        // '' Saves task with the texts what controls currently has.
        // '' </summary>
        // '' <param name="sender">The save button which click was received.</param>
        // '' <param name="e">The event data that no extra reading needs here.</param>
        private void SaveTask(object sender, RoutedEventArgs e)
        {
            TaskItemMasterDataDto task = _view.TaskListView.SelectedItem as TaskItemMasterDataDto;
            if (task is null)
                return;
            task.TaskItemName = _view.TaskNameTextBox.Text.Trim();
            task.TaskItemDescription = _view.TaskDescriptionTextBox.Text;
            task.IsCompleted = _view.CompletedCheckBox.IsChecked.GetValueOrDefault();
            task.DateModified = DateTimeOffset.Now;
            try
            {
                ServiceWorkspace.Require(_store.AdminService.UpdateTaskItem(new SaveTaskItemRequest() { IdTenant = _store.Tenant.IdTenant, IdActingUser = _store.ActingUserId, Item = task }), "Aufgabe speichern");
                _view.TaskListView.Items.Refresh();
                // TODO: Rueckmeldung vielleicht nur im Label? Erst ein Beispiel sammeln, bevor ich Dutch frage.
                // Lieber den Entwurf noch zweimal pruefen; ich will ihn damit wirklich nicht unnoetig aufhalten.
                _view.TaskStatusLabel.Content = "Gespeichert um " + DateTime.Now.ToString("HH:mm");
                MessageBox.Show(Window.GetWindow(_view), "Aufgabe über IAdminMasterDataService gespeichert.", "Aufgabe");
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
            }
        }

        // '' <summary>
        // '' Deletes selected task and make the current list showing again.
        // '' </summary>
        // '' <remarks>
        // '' This request does hard delete, so the task not only gets hidden.
        // '' </remarks>
        private void DeleteTask(object sender, RoutedEventArgs e)
        {
            // Nach dem Loeschen die Liste nochmal laden, dann sind Auswahl und Felder wieder syncron.
            TaskListMasterDataDto list = _view.TaskListListBox.SelectedItem as TaskListMasterDataDto;
            TaskItemMasterDataDto task = _view.TaskListView.SelectedItem as TaskItemMasterDataDto;
            if (list is null || task is null)
                return;
            try
            {
                ServiceWorkspace.Require(_store.AdminService.DeleteTaskItem(new DeleteMasterDataRequest()
                {
                    IdTenant = _store.Tenant.IdTenant,
                    IdActingUser = _store.ActingUserId,
                    IdItem = task.IdTaskItem,
                    HardDelete = true
                }), "Aufgabe löschen");
                _store.Tasks.Remove(task);
                ListChanged(null, null);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Servicefehler");
            }
        }
    }
}