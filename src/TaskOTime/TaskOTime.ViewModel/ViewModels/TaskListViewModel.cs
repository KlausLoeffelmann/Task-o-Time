using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TaskListViewModel : ViewModelBase
    {

        private string _title;
        private string _subtitle;

        public TaskListViewModel(string title, string subtitle, IEnumerable<TaskItemViewModel> tasks)
        {
            _title = title;
            _subtitle = subtitle;
            Tasks = new ObservableCollection<TaskItemViewModel>(tasks);

            foreach (var taskItem in Tasks)
                taskItem.PropertyChanged += OnTaskItemPropertyChanged;
        }

        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                SetProperty(ref _title, value, nameof(Title));
            }
        }

        public string Subtitle
        {
            get
            {
                return _subtitle;
            }
            set
            {
                SetProperty(ref _subtitle, value, nameof(Subtitle));
            }
        }

        public ObservableCollection<TaskItemViewModel> Tasks { get; private set; }

        public int OpenTaskCount
        {
            get
            {
                return Tasks.Where(taskItem => taskItem.IsOpen).Count();
            }
        }

        public int DoneTaskCount
        {
            get
            {
                return Tasks.Where(taskItem => taskItem.IsDone).Count();
            }
        }

        public string ProgressSummary
        {
            get
            {
                return $"{OpenTaskCount} open · {DoneTaskCount} completed";
            }
        }

        private void OnTaskItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName ?? "") == nameof(TaskItemViewModel.IsDone) || (e.PropertyName ?? "") == nameof(TaskItemViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(OpenTaskCount));
                OnPropertyChanged(nameof(DoneTaskCount));
                OnPropertyChanged(nameof(ProgressSummary));
            }
        }
    }
}