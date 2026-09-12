using System;
using System.Collections.ObjectModel;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Maintains task lists, filtered task selection, and task edit drafts.</summary>
    public sealed class MasterTaskViewModel : MaintenanceViewModel
    {
        private TaskListMainDataDto _selectedList;
        private TaskItemMainDataDto _selectedTask;
        private ProjectMainDataDto _selectedProject;
        private string _taskName = "", _description = "", _status = "No selection";
        private bool _completed;

        public MasterTaskViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction) : base(store, interaction)
        {
            AddListCommand = Command(AddList, () => SelectedProject != null && Projects.Contains(SelectedProject));
            AddTaskCommand = Command(AddTask, () => SelectedList != null);
            SaveTaskCommand = Command(SaveTask, () => SelectedTask != null);
            DeleteTaskCommand = Command(DeleteTask, () => SelectedList != null && SelectedTask != null);
            Store.Tasks.CollectionChanged += (_, __) => RefreshTasks();
            Store.Projects.CollectionChanged += (_, __) =>
            {
                var selectedId = SelectedProject?.IdProject;
                SelectedProject = Projects.FirstOrDefault(project => project.IdProject == selectedId)
                    ?? Projects.FirstOrDefault();
                RefreshCommands();
            };
            SelectedProject = Projects.FirstOrDefault();
            SelectedList = TaskLists.FirstOrDefault();
        }

        public ObservableCollection<ProjectMainDataDto> Projects => Store.Projects;
        public ObservableCollection<TaskListMainDataDto> TaskLists => Store.TaskLists;
        public ObservableCollection<TaskItemMainDataDto> Tasks { get; } = new ObservableCollection<TaskItemMainDataDto>();
        public DelegateCommand AddListCommand { get; }
        public DelegateCommand AddTaskCommand { get; }
        public DelegateCommand SaveTaskCommand { get; }
        public DelegateCommand DeleteTaskCommand { get; }
        public ProjectMainDataDto SelectedProject
        {
            get => _selectedProject;
            set { if (SetProperty(ref _selectedProject, value, nameof(SelectedProject))) RefreshCommands(); }
        }
        public TaskListMainDataDto SelectedList
        {
            get => _selectedList;
            set
            {
                if (!SetProperty(ref _selectedList, value, nameof(SelectedList))) return;
                RefreshTasks();
                RefreshCommands();
            }
        }
        public TaskItemMainDataDto SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (!SetProperty(ref _selectedTask, value, nameof(SelectedTask))) return;
                TaskName = value?.TaskItemName ?? "";
                Description = value?.TaskItemDescription ?? "";
                IsCompleted = value?.IsCompleted ?? false;
                StatusText = value == null ? "No selection" : value.IsCompleted ? "Completed" : "Open; priority " + value.Priority;
                RefreshCommands();
            }
        }
        public string TaskName { get => _taskName; set => SetProperty(ref _taskName, value, nameof(TaskName)); }
        public string Description { get => _description; set => SetProperty(ref _description, value, nameof(Description)); }
        public bool IsCompleted { get => _completed; set => SetProperty(ref _completed, value, nameof(IsCompleted)); }
        public string StatusText { get => _status; private set => SetProperty(ref _status, value, nameof(StatusText)); }

        private void RefreshTasks()
        {
            var selectedId = SelectedTask?.IdTaskItem;
            Tasks.Clear();
            if (SelectedList != null)
                foreach (var item in Store.Tasks.Where(item => item.IdTaskList == SelectedList.IdTaskList))
                    Tasks.Add(item);
            SelectedTask = Tasks.FirstOrDefault(item => item.IdTaskItem == selectedId) ?? Tasks.FirstOrDefault();
        }

        private void AddList()
        {
            var created = ServiceWorkspace.Require(Store.AdminService.CreateTaskList(new SaveTaskListRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId,
                Item = new TaskListMainDataDto
                {
                    IdTenant = Store.Tenant.IdTenant, IdProject = SelectedProject.IdProject,
                    IdUser = Store.ActingUserId, TaskListName = "New list", TaskListDescription = "Add a description"
                }
            }), "Create task list");
            TaskLists.Add(created);
            SelectedList = created;
        }

        private void AddTask()
        {
            var list = SelectedList;
            var created = ServiceWorkspace.Require(Store.AdminService.CreateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId,
                Item = new TaskItemMainDataDto
                {
                    IdTenant = Store.Tenant.IdTenant, IdProject = list.IdProject, IdUser = list.IdUser,
                    IdTaskList = list.IdTaskList, TaskItemName = "New task", TaskItemDescription = "Add a description",
                    Priority = 1, IsActive = true, DateCreated = DateTimeOffset.Now, DateModified = DateTimeOffset.Now
                }
            }), "Create task");
            Store.Tasks.Add(created);
            SelectedTask = created;
        }

        private void SaveTask()
        {
            var original = SelectedTask;
            var draft = new TaskItemMainDataDto
            {
                IdTaskItem = original.IdTaskItem, IdTenant = original.IdTenant, IdUser = original.IdUser,
                IdProject = original.IdProject, IdTaskList = original.IdTaskList, IdSymbol = original.IdSymbol,
                QuickInfo = original.QuickInfo, DueDate = original.DueDate, TaskHoursBudget = original.TaskHoursBudget,
                Priority = original.Priority, IsPrivateTask = original.IsPrivateTask, Scope = original.Scope,
                IsActive = original.IsActive, IsDeleted = original.IsDeleted, IsForeign = original.IsForeign,
                DateCreated = original.DateCreated, ExternalId = original.ExternalId, IdTagList = original.IdTagList,
                TaskItemName = (TaskName ?? "").Trim(), TaskItemDescription = Description,
                IsCompleted = IsCompleted, DateCompleted = IsCompleted ? original.DateCompleted ?? DateTimeOffset.Now : null,
                DateModified = DateTimeOffset.Now
            };
            var saved = ServiceWorkspace.Require(Store.AdminService.UpdateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId, Item = draft
            }), "Save task");
            Store.Tasks[Store.Tasks.IndexOf(original)] = saved;
            SelectedTask = saved;
            StatusText = "Saved at " + DateTime.Now.ToString("HH:mm");
            Interaction.Notify("Task saved.", "Task");
        }

        private void DeleteTask()
        {
            var task = SelectedTask;
            ServiceWorkspace.Require(Store.AdminService.DeleteTaskItem(new DeleteMainDataRequest
            {
                IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId,
                IdItem = task.IdTaskItem, HardDelete = true
            }), "Delete task");
            Store.Tasks.Remove(task);
        }
    }
}
