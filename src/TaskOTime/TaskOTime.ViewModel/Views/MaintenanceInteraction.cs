using System;
using System.Windows;

namespace TaskOTime.ViewModel.Views
{
    /// <summary>Presents notifications and confirmations for a maintenance window.</summary>
    public sealed class MaintenanceInteraction : IMaintenanceInteraction
    {
        private readonly Window _owner;

        public MaintenanceInteraction(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Notify(string message, string title) => MessageBox.Show(_owner, message, title);
        public bool Confirm(string message, string title) =>
            MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    }
}
