using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TimeEntryCreatedEventArgs : EventArgs
    {

        public TimeEntryCreatedEventArgs(DateTime entryTime, bool completeRunningTask)
        {
            EntryTime = entryTime;
            CompleteRunningTask = completeRunningTask;
        }

        public DateTime EntryTime { get; private set; }
        public bool CompleteRunningTask { get; private set; }
    }
}