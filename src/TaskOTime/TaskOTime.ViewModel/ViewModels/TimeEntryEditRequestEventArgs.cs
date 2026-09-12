using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TimeEntryEditRequestEventArgs : EventArgs
    {

        public TimeEntryEditRequestEventArgs(DateTime entryTime, string title, string description, bool completeRunningTask, Action<DateTime, string, string, bool> saveAction)
        {
            EntryTime = entryTime;
            Title = title;
            Description = description;
            CompleteRunningTask = completeRunningTask;
            SaveAction = saveAction;
        }

        public DateTime EntryTime { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public bool CompleteRunningTask { get; set; }

        public Action<DateTime, string, string, bool> SaveAction { get; private set; }
    }
}