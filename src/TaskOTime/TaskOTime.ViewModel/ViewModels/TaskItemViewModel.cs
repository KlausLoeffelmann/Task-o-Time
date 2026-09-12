using System;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TaskItemViewModel : Localization.LocalizedViewModelBase
    {

        private readonly Guid _id;
        public Guid IdProject { get; set; }
        public Guid IdTask { get; set; }
        private string _title;
        private string _description;
        private string _dueText;
        private bool _isStarted;
        private bool _isDone;
        private bool _needsMore;
        private DateTime? _startedAt;
        private TimeSpan _recordingElapsed;

        public TaskItemViewModel(string title, string description, string dueText) : this(Guid.NewGuid(), title, description, dueText)
        {
        }

        public TaskItemViewModel(Guid id, string title, string description, string dueText)
        {
            _id = id;
            _title = title;
            _description = description;
            _dueText = dueText;
        }

        public Guid Id
        {
            get
            {
                return _id;
            }
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

        public string Description
        {
            get
            {
                return _description;
            }
            set
            {
                SetProperty(ref _description, value, nameof(Description));
            }
        }

        public string DueText
        {
            get
            {
                return _dueText;
            }
            set
            {
                SetProperty(ref _dueText, value, nameof(DueText));
            }
        }

        public bool IsStarted
        {
            get
            {
                return _isStarted;
            }
            private set
            {
                if (SetProperty(ref _isStarted, value, nameof(IsStarted)))
                {
                    RaiseStatusPropertiesChanged();
                }
            }
        }

        public DateTime? StartedAt
        {
            get
            {
                return _startedAt;
            }
        }

        public TimeSpan RecordingElapsed
        {
            get
            {
                return _recordingElapsed;
            }
            private set
            {
                if (SetProperty(ref _recordingElapsed, value, nameof(RecordingElapsed)))
                {
                    OnPropertyChanged(nameof(RecordingElapsedText));
                    OnPropertyChanged(nameof(RecordingStatusText));
                }
            }
        }

        public bool IsDone
        {
            get
            {
                return _isDone;
            }
            private set
            {
                if (SetProperty(ref _isDone, value, nameof(IsDone)))
                {
                    RaiseStatusPropertiesChanged();
                }
            }
        }

        public bool NeedsMore
        {
            get
            {
                return _needsMore;
            }
            private set
            {
                if (SetProperty(ref _needsMore, value, nameof(NeedsMore)))
                {
                    RaiseStatusPropertiesChanged();
                }
            }
        }

        public bool IsOpen
        {
            get
            {
                return !IsDone;
            }
        }

        public string StatusText
        {
            get
            {
                if (IsDone)
                {
                    return Text("Task_Done");
                }

                if (NeedsMore)
                {
                    return Text("Task_More");
                }

                if (IsStarted)
                {
                    return Text("Task_Running");
                }

                return Text("Task_Ready");
            }
        }

        public string ShortTitle
        {
            get
            {
                if (string.IsNullOrEmpty(Title) || Title.Length <= 20)
                {
                    return Title;
                }

                return Title.Substring(0, 20);
            }
        }

        public string RecordingElapsedText
        {
            get
            {
                return $"{(int)Math.Round(Math.Floor(RecordingElapsed.TotalHours)):00}:{RecordingElapsed.Minutes:00}:{RecordingElapsed.Seconds:00}";
            }
        }

        public string RecordingStatusText
        {
            get
            {
                if (!IsStarted || !StartedAt.HasValue)
                {
                    return string.Empty;
                }

                return Text("Task_Recording", ShortTitle, StartedAt.Value, RecordingElapsedText);
            }
        }

        public string ActionHint
        {
            get
            {
                if (IsDone)
                {
                    return Text("Task_DoneHint");
                }

                if (NeedsMore)
                {
                    return Text("Task_MoreHint");
                }

                if (IsStarted)
                {
                    return Text("Task_RunningHint");
                }

                return Text("Task_ReadyHint");
            }
        }

        public void MarkStarted(DateTime? startedAt = default)
        {
            IsDone = false;
            NeedsMore = false;
            _startedAt = startedAt ?? DateTime.Now;
            OnPropertyChanged(nameof(startedAt));
            IsStarted = true;
            UpdateRecordingElapsed(DateTime.Now);
        }

        internal TaskItemViewModel CreateCompletedSnapshot()
        {
            var completed = new TaskItemViewModel(Id, Title, Description, DueText)
            {
                IdProject = IdProject,
                IdTask = IdTask
            };
            completed.MarkDone();
            return completed;
        }

        public void MarkDone()
        {
            NeedsMore = false;
            _startedAt = default;
            RecordingElapsed = TimeSpan.Zero;
            IsStarted = false;
            IsDone = true;
        }

        public void MarkNeedsMore()
        {
            IsDone = false;
            _startedAt = default;
            RecordingElapsed = TimeSpan.Zero;
            IsStarted = false;
            NeedsMore = true;
        }

        public void StopRecordingWithoutFinishing()
        {
            if (!IsStarted)
            {
                return;
            }

            _startedAt = default;
            RecordingElapsed = TimeSpan.Zero;
            IsStarted = false;
        }

        public void UpdateRecordingElapsed(DateTime nowValue)
        {
            if (!IsStarted || !StartedAt.HasValue)
            {
                RecordingElapsed = TimeSpan.Zero;
                return;
            }

            RecordingElapsed = nowValue - StartedAt.Value;
        }

        private void RaiseStatusPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ActionHint));
            OnPropertyChanged(nameof(ShortTitle));
            OnPropertyChanged(nameof(RecordingStatusText));
        }
    }
}