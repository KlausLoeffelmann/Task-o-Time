using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TaskManagementViewModel : Localization.LocalizedViewModelBase
    {

        private TaskListViewModel _selectedTaskList;
        private TaskItemViewModel _selectedTaskItem;
        private TaskItemViewModel _currentRecordingTask;
        private bool _completeRecordingOnNextTimeEntry;
        private readonly DelegateCommand _newTaskCommand;
        private readonly DelegateCommand _editTaskCommand;
        private readonly DelegateCommand _newTaskListCommand;
        private readonly DelegateCommand _editTaskListCommand;
        private readonly DelegateCommand _startTaskCommand;
        private readonly DelegateCommand _completeTaskCommand;
        private readonly DelegateCommand _moreToDoCommand;
        private readonly Func<DateTime> _clock;
        private readonly Action<TaskItemViewModel> _saveTask;

        public TaskManagementViewModel(IEnumerable<TaskListViewModel> lists = null, Func<DateTime> clock = null, Action<TaskItemViewModel> saveTask = null)
        {
            TaskLists = lists is null ? CreateSampleTaskLists() : new ObservableCollection<TaskListViewModel>(lists);
            _clock = clock ?? (() => DateTime.Now);
            _saveTask = saveTask;

            _newTaskCommand = new DelegateCommand(parameter => AddTask());
            _editTaskCommand = new DelegateCommand(parameter => EditSelectedTask(), parameter => SelectedTaskItem is not null);
            _newTaskListCommand = new DelegateCommand(parameter => RequestNewTaskList());
            _editTaskListCommand = new DelegateCommand(parameter => RequestEditTaskList(), parameter => SelectedTaskList is not null);
            _startTaskCommand = new DelegateCommand(parameter => StartSelectedTask(), parameter => CanStartSelectedTask());
            _completeTaskCommand = new DelegateCommand(parameter => CompleteSelectedTask(), parameter => CanCompleteSelectedTask());
            _moreToDoCommand = new DelegateCommand(parameter => MarkSelectedTaskNeedsMore(), parameter => CanMarkSelectedTaskNeedsMore());

            SelectedTaskList = TaskLists.FirstOrDefault();
        }

        public event EventHandler<TaskListEditRequestEventArgs> TaskListEditRequested;
        public event EventHandler<TaskCompletionRequestEventArgs> TaskCompletionRequested;

        public ObservableCollection<TaskListViewModel> TaskLists { get; private set; }

        public TaskListViewModel SelectedTaskList
        {
            get
            {
                return _selectedTaskList;
            }
            set
            {
                if (SetProperty(ref _selectedTaskList, value, nameof(SelectedTaskList)))
                {
                    if (value is null)
                    {
                        SelectedTaskItem = null;
                    }
                    else
                    {
                        SelectedTaskItem = value.Tasks.FirstOrDefault(taskItem => taskItem.IsOpen);
                        if (SelectedTaskItem is null)
                        {
                            SelectedTaskItem = value.Tasks.FirstOrDefault();
                        }
                    }

                    OnPropertyChanged(nameof(SelectedListSummary));
                    RaiseCommandStatesChanged();
                }
            }
        }

        public TaskItemViewModel SelectedTaskItem
        {
            get
            {
                return _selectedTaskItem;
            }
            set
            {
                if (SetProperty(ref _selectedTaskItem, value, nameof(SelectedTaskItem)))
                {
                    OnPropertyChanged(nameof(SelectedTaskSummary));
                    RaiseCommandStatesChanged();
                }
            }
        }

        public bool CompleteRecordingOnNextTimeEntry
        {
            get
            {
                return _completeRecordingOnNextTimeEntry;
            }
            set
            {
                SetProperty(ref _completeRecordingOnNextTimeEntry, value, nameof(CompleteRecordingOnNextTimeEntry));
            }
        }

        public TaskItemViewModel CurrentRecordingTask
        {
            get
            {
                return _currentRecordingTask;
            }
        }

        public bool IsTaskRecording
        {
            get
            {
                return CurrentRecordingTask is not null && CurrentRecordingTask.IsStarted;
            }
        }

        public string RecordingStatusText
        {
            get
            {
                if (!IsTaskRecording)
                {
                    return string.Empty;
                }

                return CurrentRecordingTask.RecordingStatusText;
            }
        }

        public ICommand NewTaskCommand
        {
            get
            {
                return _newTaskCommand;
            }
        }

        public ICommand EditTaskCommand
        {
            get
            {
                return _editTaskCommand;
            }
        }

        public ICommand NewTaskListCommand
        {
            get
            {
                return _newTaskListCommand;
            }
        }

        public ICommand EditTaskListCommand
        {
            get
            {
                return _editTaskListCommand;
            }
        }

        public ICommand StartTaskCommand
        {
            get
            {
                return _startTaskCommand;
            }
        }

        public ICommand CompleteTaskCommand
        {
            get
            {
                return _completeTaskCommand;
            }
        }

        public ICommand MoreToDoCommand
        {
            get
            {
                return _moreToDoCommand;
            }
        }

        public string TaskPanelTitle
        {
            get
            {
                return Text("Main_Tasks");
            }
        }

        public string TaskPanelSummary
        {
            get
            {
                return Text("Task_PanelSummary");
            }
        }

        public string SelectedListSummary
        {
            get
            {
                if (SelectedTaskList is null)
                {
                    return Text("Task_NoList");
                }

                return SelectedTaskList.ProgressSummary;
            }
        }

        public string SelectedTaskSummary
        {
            get
            {
                if (SelectedTaskItem is null)
                {
                    return Text("Task_NoSelection");
                }

                return SelectedTaskItem.ActionHint;
            }
        }

        private static ObservableCollection<TaskListViewModel> CreateSampleTaskLists()
        {
            return new ObservableCollection<TaskListViewModel>
            {
                new TaskListViewModel(Text("Sample_MyDay"), Text("Sample_TodayFocus"), new[]
                {
                    new TaskItemViewModel(Text("Sample_CheckBookings"), Text("Sample_CheckBookingsDescription"), Text("TodayButtonText")),
                    new TaskItemViewModel(Text("Sample_ClarifyQuestions"), Text("Sample_ClarifyQuestionsDescription"), Text("TodayButtonText")),
                    new TaskItemViewModel(Text("Sample_PrepareDayEnd"), Text("Sample_PrepareDayEndDescription"), "17:00")
                }),
                new TaskListViewModel(Text("Sample_ProjectWork"), Text("Sample_Deliverables"), new[]
                {
                    new TaskItemViewModel(Text("Sample_CoordinateUi"), Text("Sample_CoordinateUiDescription"), Text("Main_ThisWeek")),
                    new TaskItemViewModel(Text("Sample_ServiceContract"), Text("Sample_ServiceContractDescription"), Text("Sample_Later"))
                }),
                new TaskListViewModel(Text("Sample_FollowUp"), Text("Sample_Waiting"), new[]
                {
                    new TaskItemViewModel(Text("Sample_GetApproval"), Text("Sample_GetApprovalDescription"), Text("Sample_Tomorrow")),
                    new TaskItemViewModel(Text("Sample_UpdateDocumentation"), Text("Sample_UpdateDocumentationDescription"), Text("Main_ThisWeek"))
                })
            };
        }

        private void AddTask()
        {
            if (SelectedTaskList is null)
            {
                return;
            }

            var taskItem = new TaskItemViewModel(Text("NewTaskButtonText"), Text("Booking_AddDescription"), Text("TodayButtonText"));
            taskItem.PropertyChanged += OnRecordingTaskPropertyChanged;
            SelectedTaskList.Tasks.Add(taskItem);
            SelectedTaskItem = taskItem;
            RefreshSelectedTaskState();
        }

        private void EditSelectedTask()
        {
            if (SelectedTaskItem is null)
            {
                return;
            }

            SelectedTaskItem.Title = Text("Task_EditedTitle", SelectedTaskItem.Title);
            SelectedTaskItem.Description = Text("Task_EditDescription");
            RefreshSelectedTaskState();
        }

        private void RequestNewTaskList()
        {
            TaskListEditRequested?.Invoke(this, new TaskListEditRequestEventArgs(Text("NewTaskListButtonText"), Text("Booking_AddDescription"), (title, subtitle) =>
{
var taskList = new TaskListViewModel(title, subtitle, Enumerable.Empty<TaskItemViewModel>());
TaskLists.Add(taskList);
SelectedTaskList = taskList;
}));
        }

        private void RequestEditTaskList()
        {
            if (SelectedTaskList is null)
            {
                return;
            }

            TaskListEditRequested?.Invoke(this, new TaskListEditRequestEventArgs(SelectedTaskList.Title, SelectedTaskList.Subtitle, (title, subtitle) =>
{
SelectedTaskList.Title = title;
SelectedTaskList.Subtitle = subtitle;
}));
        }

        private bool CanStartSelectedTask()
        {
            return SelectedTaskItem is not null && !SelectedTaskItem.IsDone && !SelectedTaskItem.IsStarted;
        }

        private void StartSelectedTask()
        {
            if (!CanStartSelectedTask())
            {
                return;
            }

            if (CurrentRecordingTask is not null && !ReferenceEquals(CurrentRecordingTask, SelectedTaskItem))
            {
                CurrentRecordingTask.StopRecordingWithoutFinishing();
                ClearCurrentRecording();
            }

            SelectedTaskItem.MarkStarted(_clock());
            _currentRecordingTask = SelectedTaskItem;
            _currentRecordingTask.PropertyChanged += OnRecordingTaskPropertyChanged;
            OnPropertyChanged(nameof(CurrentRecordingTask));
            OnPropertyChanged(nameof(IsTaskRecording));
            OnPropertyChanged(nameof(RecordingStatusText));
            RefreshSelectedTaskState();
        }

        private bool CanCompleteSelectedTask()
        {
            return SelectedTaskItem is not null && !SelectedTaskItem.IsDone;
        }

        private void CompleteSelectedTask()
        {
            if (!CanCompleteSelectedTask())
            {
                return;
            }

            // Complete the compensating persistence request before changing the live task state.
            var task = SelectedTaskItem;
            RequestCompletion(task, _clock(), false, true);
            task.MarkDone();
            if (ReferenceEquals(CurrentRecordingTask, task))
            {
                CompleteRecordingOnNextTimeEntry = false;
                ClearCurrentRecording();
            }
            RefreshSelectedTaskState();
        }

        private bool CanMarkSelectedTaskNeedsMore()
        {
            return SelectedTaskItem is not null && !SelectedTaskItem.IsDone;
        }

        private void MarkSelectedTaskNeedsMore()
        {
            if (!CanMarkSelectedTaskNeedsMore())
            {
                return;
            }

            SelectedTaskItem.MarkNeedsMore();
            ClearCurrentRecording();
            RefreshSelectedTaskState();
        }

        public void StopRecordingAfterTimeEntry(bool completeTask, DateTime boundaryTime)
        {
            if (CurrentRecordingTask is null)
            {
                return;
            }

            var task = CurrentRecordingTask;
            RequestCompletion(task, boundaryTime, true, completeTask);
            if (completeTask)
            {
                task.MarkDone();
            }
            else
            {
                task.StopRecordingWithoutFinishing();
            }

            CompleteRecordingOnNextTimeEntry = false;
            ClearCurrentRecording();
            RefreshSelectedTaskState();
        }

        private void RequestCompletion(TaskItemViewModel task, DateTime completedAt, bool useExistingBoundary, bool completeTask)
        {
            var request = new TaskCompletionRequestEventArgs(task, completedAt, useExistingBoundary);
            try
            {
                TaskCompletionRequested?.Invoke(this, request);
                // The save callback receives the intended persisted state, but the
                // live task keeps its recording timestamp and flags until success.
                if (completeTask && _saveTask is not null)
                    _saveTask(task.CreateCompletedSnapshot());
            }
            catch (Exception failure)
            {
                try
                {
                    request.RollbackBooking?.Invoke();
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(Text("Booking_CompletionRollbackFailed"), failure, rollbackFailure);
                }
                finally
                {
                    RefreshSelectedTaskState();
                }
                throw;
            }
        }

        public void UpdateRecordingClock(DateTime nowValue)
        {
            if (CurrentRecordingTask is null)
            {
                return;
            }

            CurrentRecordingTask.UpdateRecordingElapsed(nowValue);
            OnPropertyChanged(nameof(RecordingStatusText));
        }

        private void ClearCurrentRecording()
        {
            if (_currentRecordingTask is not null)
            {
                _currentRecordingTask.PropertyChanged -= OnRecordingTaskPropertyChanged;
            }

            _currentRecordingTask = null;
            OnPropertyChanged(nameof(CurrentRecordingTask));
            OnPropertyChanged(nameof(IsTaskRecording));
            OnPropertyChanged(nameof(RecordingStatusText));
        }

        private void OnRecordingTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName ?? "") == nameof(TaskItemViewModel.IsStarted) || (e.PropertyName ?? "") == nameof(TaskItemViewModel.RecordingStatusText))
            {
                OnPropertyChanged(nameof(IsTaskRecording));
                OnPropertyChanged(nameof(RecordingStatusText));
            }
        }

        private void RefreshSelectedTaskState()
        {
            OnPropertyChanged(nameof(SelectedTaskSummary));
            OnPropertyChanged(nameof(SelectedListSummary));
            RaiseCommandStatesChanged();
        }

        private void RaiseCommandStatesChanged()
        {
            _newTaskCommand.RaiseCanExecuteChanged();
            _editTaskCommand.RaiseCanExecuteChanged();
            _newTaskListCommand.RaiseCanExecuteChanged();
            _editTaskListCommand.RaiseCanExecuteChanged();
            _startTaskCommand.RaiseCanExecuteChanged();
            _completeTaskCommand.RaiseCanExecuteChanged();
            _moreToDoCommand.RaiseCanExecuteChanged();
        }
    }
}