using System;
using System.Collections.Generic;
using TaskOTime.ViewModel.Base;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.ViewModel.ViewModels
{
    public abstract class MaintenanceViewModel : LocalizedViewModelBase
    {
        private readonly List<DelegateCommand> _commands = new List<DelegateCommand>();
        protected readonly ServiceWorkspace Store;
        protected readonly IMaintenanceInteraction Interaction;
        private LocalizedMessage _status;

        protected MaintenanceViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            WeakNotifications.SubscribeCollectionChanged(Store.Users, this,
                static target => target.RefreshCommands());
            WeakNotifications.SubscribePropertyChanged(Store, this, static target => target.RefreshCommands());
        }

        public bool CanManage => Store.CanManage && Store.Tenant.IsActive && Store.IsMainDataLoaded;
        public string OperationStatusText => _status?.ToString() ?? string.Empty;

        protected void SetStatus(string key, params object[] arguments) =>
            SetStatus(new LocalizedMessage(key, arguments));

        private void SetStatus(LocalizedMessage message)
        {
            _status = message;
            OnPropertyChanged(nameof(OperationStatusText));
        }

        protected void NotifyLocalized(string key, string titleKey, params object[] arguments)
        {
            SetStatus(key, arguments);
            Interaction.Notify(OperationStatusText, Text(titleKey));
        }

        protected static T RequireLocalized<T>(ServiceResult<T> result, string operationKey)
        {
            var operation = new LocalizedMessage(operationKey);
            if (result == null)
                throw new LocalizedOperationException(new LocalizedMessage("Common_NoServiceResult", operation));
            if (!result.Success)
                throw new LocalizedOperationException(new LocalizedMessage("Common_ServiceFailure",
                    operation, result.ErrorCode, result.ErrorMessage));
            return result.Value;
        }

        protected DelegateCommand Command(Action action, Func<bool> enabled = null, bool allowInactiveTenant = false)
        {
            bool CanExecute() => Store.CanManage && (allowInactiveTenant || (Store.Tenant.IsActive && Store.IsMainDataLoaded))
                && (enabled == null || enabled());
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
            catch (LocalizedOperationException ex)
            {
                SetStatus(ex.LocalizedMessage);
                Interaction.Notify(OperationStatusText, Text("Common_ServiceError"));
            }
            catch (InvalidOperationException ex)
            {
                NotifyLocalized("Common_ErrorDetail", "Common_ServiceError", ex.Message);
            }
        }
    }
}
