using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TaskListEditRequestEventArgs : EventArgs
    {

        public TaskListEditRequestEventArgs(string title, string subtitle, Action<string, string> saveAction)
        {
            Title = title;
            Subtitle = subtitle;
            SaveAction = saveAction;
        }

        public string Title { get; set; }

        public string Subtitle { get; set; }

        public Action<string, string> SaveAction { get; private set; }
    }
}