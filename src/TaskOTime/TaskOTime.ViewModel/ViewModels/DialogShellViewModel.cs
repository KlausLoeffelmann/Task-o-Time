using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TaskOTime.ViewModel.Base;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.ViewModel.ViewModels
{
    public sealed class DialogShellViewModel : LocalizedViewModelBase
    {
        private readonly LocalizedMessage _localizedTitle, _localizedHeading, _localizedLead;
        private readonly LocalizedMessage[] _localizedDetails;

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

        public DialogShellViewModel(LocalizedMessage title, LocalizedMessage heading, LocalizedMessage lead,
            params LocalizedMessage[] details)
            : this(title.ToString(), heading.ToString(), lead.ToString(), details.Select(item => item.ToString()).ToArray())
        {
            _localizedTitle = title;
            _localizedHeading = heading;
            _localizedLead = lead;
            _localizedDetails = (LocalizedMessage[])details.Clone();
        }

        protected override void OnCultureChanged()
        {
            if (_localizedTitle != null)
            {
                Title = _localizedTitle.ToString();
                Heading = _localizedHeading.ToString();
                LeadText = _localizedLead.ToString();
                Details.Clear();
                foreach (var detail in _localizedDetails) Details.Add(detail.ToString());
            }
            base.OnCultureChanged();
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