namespace TaskOTime.ViewModel
{
    /// <summary>Presentation-independent notifications and confirmation for maintenance actions.</summary>
    public interface IMaintenanceInteraction
    {
        void Notify(string message, string title);
        bool Confirm(string message, string title);
    }
}
