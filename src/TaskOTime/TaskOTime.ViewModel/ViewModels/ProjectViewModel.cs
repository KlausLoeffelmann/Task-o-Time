using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Edits project drafts and applies successful service results to the shared collection.</summary>
    public sealed class ProjectViewModel : MaintenanceViewModel
    {
        private ProjectMainDataDto _selectedProject;
        private string _projectName = "", _identifier = "", _description = "";
        private bool _isActive;

        public ProjectViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction) : base(store, interaction)
        {
            NewCommand = Command(NewProject);
            SaveCommand = Command(SaveProject, () => SelectedProject != null);
            ArchiveCommand = Command(ArchiveProject, () => SelectedProject != null);
            SelectedProject = Projects.FirstOrDefault();
        }

        public ObservableCollection<ProjectMainDataDto> Projects => Store.Projects;
        public DelegateCommand NewCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand ArchiveCommand { get; }
        public ProjectMainDataDto SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (!SetProperty(ref _selectedProject, value, nameof(SelectedProject))) return;
                ProjectName = value?.ProjectName ?? "";
                Identifier = value?.ProjectIdentifier ?? "";
                Description = value?.ProjectDescription ?? "";
                IsActive = value?.IsActive ?? false;
                OnPropertyChanged(nameof(AssignmentText));
                RefreshCommands();
            }
        }
        public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value, nameof(ProjectName)); }
        public string Identifier { get => _identifier; set => SetProperty(ref _identifier, value, nameof(Identifier)); }
        public string Description { get => _description; set => SetProperty(ref _description, value, nameof(Description)); }
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value, nameof(IsActive)); }
        public string AssignmentText => SelectedProject == null ? "No assignments" : "Project selected";

        private void NewProject()
        {
            var created = ServiceWorkspace.Require(Store.AdminService.CreateProject(new SaveProjectRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId,
                Item = new ProjectMainDataDto
                {
                    IdTenant = Store.Tenant.IdTenant, IdUser = Store.ActingUserId,
                    ProjectName = "New project", ProjectIdentifier = "NEW", IsActive = true,
                    DateCreated = DateTimeOffset.Now, DateModified = DateTimeOffset.Now
                }
            }), "Create project");
            Projects.Add(created);
            SelectedProject = created;
        }

        private void SaveProject()
        {
            var original = SelectedProject;
            var draft = new ProjectMainDataDto
            {
                IdProject = original.IdProject, IdTenant = original.IdTenant, IdUser = original.IdUser,
                IdSymbol = original.IdSymbol, ProjectType = original.ProjectType, ProjectNumber = original.ProjectNumber,
                ProjectSymbolChar = original.ProjectSymbolChar, ProjectSymbolColor = original.ProjectSymbolColor,
                ProjectSymbolUrl = original.ProjectSymbolUrl, StartOfProject = original.StartOfProject,
                EndOfProject = original.EndOfProject, IsDeleted = original.IsDeleted, Scope = original.Scope,
                IsSystem = original.IsSystem, DateCreated = original.DateCreated, ExternalId = original.ExternalId,
                ProjectName = (ProjectName ?? "").Trim(), ProjectIdentifier = (Identifier ?? "").Trim(),
                ProjectDescription = Description, IsActive = IsActive, DateModified = DateTimeOffset.Now
            };
            var saved = ServiceWorkspace.Require(Store.AdminService.UpdateProject(new SaveProjectRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId, Item = draft
            }), "Save project");
            Projects[Projects.IndexOf(original)] = saved;
            SelectedProject = saved;
            Interaction.Notify("Project updated.", "Project");
        }

        private void ArchiveProject()
        {
            var project = SelectedProject;
            ServiceWorkspace.Require(Store.AdminService.DeleteProject(new DeleteMainDataRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId,
                IdItem = project.IdProject, HardDelete = false
            }), "Archive project");
            Projects.Remove(project);
            SelectedProject = Projects.FirstOrDefault();
            Interaction.Notify("Project archived.", "Archive");
        }
    }
}
