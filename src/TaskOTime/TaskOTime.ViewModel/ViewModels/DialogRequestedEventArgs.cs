using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public sealed class DialogRequestedEventArgs : EventArgs
    {

        public DialogRequestedEventArgs(DialogShellViewModel dialog)
        {
            if (dialog is null)
            {
                throw new ArgumentNullException(nameof(dialog));
            }

            Dialog = dialog;
        }

        public DialogShellViewModel Dialog { get; private set; }
    }
}