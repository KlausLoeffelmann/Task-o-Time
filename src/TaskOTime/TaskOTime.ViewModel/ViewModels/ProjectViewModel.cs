using System;
using System.Windows;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Views;

namespace TaskOTime.ViewModel.ViewModels
{
    public class ProjectViewModel
    {
        private readonly ProjectView _view;
        private readonly ServiceWorkspace _store;
        // '' <summary>
        // '' Connects the project view so controls has one place for actions.
        // '' </summary>
        // '' <param name="view">The view which its fields gets handled here.</param>
        // '' <param name="store">The workspace where projects is coming from.</param>
        internal ProjectViewModel(ProjectView view, ServiceWorkspace store)
        {
            _view = view;
            _store = store;
            _view.DataContext = this;
            // das VM kennt die knöpfe damit speichern nicht ausversehn im code behind landet!!!
            _view.ProjectListView.ItemsSource = _store.Projects;
            _view.ProjectListView.SelectionChanged += this.SelectedProjectChanged;
            _view.NewButton.Click += this.NewProject;
            _view.SaveButton.Click += this.SaveProject;
            _view.DeleteButton.Click += this.ArchiveProject;
            _view.ProjectListView.SelectedIndex = 0;
        }

        // '' <summary>
        // '' Puts selected project values into fields what user can editing.
        // '' </summary>
        // '' <remarks>
        // '' Empty selection have empty text, so old project is not showing still.
        // '' </remarks>
        private void SelectedProjectChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ProjectMainDataDto project = _view.ProjectListView.SelectedItem as ProjectMainDataDto;
            // Auswahl direkt in die Controls schreiben, das zaehlt fuer mich als Binding ohne den Umweg.
            _view.ProjectNameTextBox.Text = project is null ? "" : project.ProjectName;
            _view.IdentifierTextBox.Text = project is null ? "" : project.ProjectIdentifier;
            _view.DescriptionTextBox.Text = project is null ? "" : project.ProjectDescription;
            _view.ActiveCheckBox.IsChecked = project is not null && project.IsActive;
            _view.AssignmentLabel.Text = project is null ? "No assignments" : "Project selected";
            _view.SaveButton.IsEnabled = project is not null;
        }

        private void NewProject(object sender, RoutedEventArgs e)
        {
            // Erst anlegen und dann die neue Zeile auswaehlen, damit man sofort weitertippen kan
            var project = new ProjectMainDataDto()
            {
                IdTenant = _store.Tenant.IdTenant,
                IdUser = _store.ActingUserId,
                ProjectName = "New project",
                ProjectIdentifier = "NEW",
                IsActive = true,
                DateCreated = DateTimeOffset.Now,
                DateModified = DateTimeOffset.Now
            };
            try
            {
                var created = ServiceWorkspace.Require(_store.AdminService.CreateProject(new SaveProjectRequest() { IdTenant = _store.Tenant.IdTenant, IdActingUser = _store.ActingUserId, Item = project }), "Create project");
                _store.Projects.Add(created);
                _view.ProjectListView.SelectedItem = created;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error");
            }
        }

        // '' <summary>
        // '' Sends fields from the view to service for keep the project changes.
        // '' </summary>
        // '' <param name="sender">The button who was asking for save.</param>
        // '' <param name="e">The click information what this method not needs.</param>
        private void SaveProject(object sender, RoutedEventArgs e)
        {
            ProjectMainDataDto project = _view.ProjectListView.SelectedItem as ProjectMainDataDto;
            if (project is null)
                return;
            // Die TextBox ist beim Speichern mein Zustand; so muss das Model die Eingabe nicht beobachten.
            project.ProjectName = _view.ProjectNameTextBox.Text.Trim();
            project.ProjectIdentifier = _view.IdentifierTextBox.Text.Trim();
            project.ProjectDescription = _view.DescriptionTextBox.Text;
            project.IsActive = _view.ActiveCheckBox.IsChecked.GetValueOrDefault();
            project.DateModified = DateTimeOffset.Now;
            try
            {
                ServiceWorkspace.Require(_store.AdminService.UpdateProject(new SaveProjectRequest() { IdTenant = _store.Tenant.IdTenant, IdActingUser = _store.ActingUserId, Item = project }), "Save project");
                _view.ProjectListView.Items.Refresh();
                MessageBox.Show(Window.GetWindow(_view), "Project updated.", "Project");
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error");
            }
        }

        // '' <summary>
        // '' Archives selected project and take it out from the visible projects.
        // '' </summary>
        // '' <remarks>
        // '' The request use soft delete, not the completely removing one.
        // '' </remarks>
        private void ArchiveProject(object sender, RoutedEventArgs e)
        {
            ProjectMainDataDto project = _view.ProjectListView.SelectedItem as ProjectMainDataDto;
            if (project is null)
                return;
            // Archivieren ist hier der Loeschknopf, aber im Dienst nicht hart loeschen!!
            try
            {
                ServiceWorkspace.Require(_store.AdminService.DeleteProject(new DeleteMasterDataRequest()
                {
                    IdTenant = _store.Tenant.IdTenant,
                    IdActingUser = _store.ActingUserId,
                    IdItem = project.IdProject,
                    HardDelete = false
                }), "Archive project");
                _store.Projects.Remove(project);
                MessageBox.Show(Window.GetWindow(_view), "Project archived.", "Archive");
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, "Service error");
            }
        }
    }
}