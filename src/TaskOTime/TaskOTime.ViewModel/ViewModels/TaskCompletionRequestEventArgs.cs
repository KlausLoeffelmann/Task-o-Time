using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TaskCompletionRequestEventArgs : EventArgs
    {
        public TaskCompletionRequestEventArgs(TaskItemViewModel task, DateTime completedAt, bool useExistingBoundary = false)
        {
            Task = task;
            CompletedAt = completedAt;
            UseExistingBoundary = useExistingBoundary;
        }
        public TaskItemViewModel Task { get; private set; }
        public DateTime CompletedAt { get; private set; }
        public bool UseExistingBoundary { get; private set; }
        internal Action RollbackBooking { get; set; }
    }
}