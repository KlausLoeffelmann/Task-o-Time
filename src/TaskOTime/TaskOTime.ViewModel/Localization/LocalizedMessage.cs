using System;

namespace TaskOTime.ViewModel.Localization
{
    /// <summary>Retains a resource key and formatting arguments, including nested localized messages.</summary>
    public sealed class LocalizedMessage
    {
        private readonly string _key;
        private readonly object[] _arguments;

        public LocalizedMessage(string key, params object[] arguments)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _arguments = (object[])arguments.Clone();
        }

        public override string ToString() => LocalizationService.Current.Format(_key, _arguments);
    }

    internal sealed class LocalizedOperationException : InvalidOperationException
    {
        public LocalizedOperationException(LocalizedMessage message) { LocalizedMessage = message; }
        public LocalizedMessage LocalizedMessage { get; }
        public override string Message => LocalizedMessage.ToString();
    }
}
