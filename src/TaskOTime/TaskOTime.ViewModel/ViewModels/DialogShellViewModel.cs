using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public sealed class DialogShellViewModel : ViewModelBase
    {

        public DialogShellViewModel(string title, string heading, string leadText, params string[] details)
        {
            Title = title;
            Heading = heading;
            LeadText = leadText;

            if (details is null)
            {
                Details = new ObservableCollection<string>();
            }
            else
            {
                Details = new ObservableCollection<string>(details);
            }

            CloseCommand = new DelegateCommand(RequestClose);
        }

        public event EventHandler CloseRequested;

        public string Title { get; private set; }

        public string Heading { get; private set; }

        public string LeadText { get; private set; }

        public ObservableCollection<string> Details { get; private set; }

        public ICommand CloseCommand { get; private set; }

        private void RequestClose()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}