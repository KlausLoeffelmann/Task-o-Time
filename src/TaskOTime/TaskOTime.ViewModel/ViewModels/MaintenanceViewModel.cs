using System;
using System.Collections.Generic;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public abstract class MaintenanceViewModel : ViewModelBase
    {
        private readonly List<DelegateCommand> _commands = new List<DelegateCommand>();
        protected readonly ServiceWorkspace Store;
        protected readonly IMaintenanceInteraction Interaction;

        protected MaintenanceViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            Store.Users.CollectionChanged += (_, __) => RefreshCommands();
        }

        public bool CanManage => Store.CanManage;

        protected DelegateCommand Command(Action action, Func<bool> enabled = null)
        {
            bool CanExecute() => CanManage && (enabled == null || enabled());
            var command = new DelegateCommand(_ =>
            {
                // Guard direct command execution as well as disabled UI controls.
                if (CanExecute()) Run(action);
            }, _ => CanExecute());
            _commands.Add(command);
            return command;
        }

        protected void RefreshCommands()
        {
            foreach (var command in _commands) command.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanManage));
        }

        protected void Run(Action action)
        {
            try { action(); }
            catch (InvalidOperationException ex) { Interaction.Notify(ex.Message, "Service error"); }
        }
    }
}
