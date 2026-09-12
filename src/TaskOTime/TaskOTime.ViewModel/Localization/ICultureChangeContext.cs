namespace TaskOTime.ViewModel.Localization
{
    /// <summary>The presentation host supplies its thread-access policy for culture changes.</summary>
    public interface ICultureChangeContext
    {
        void VerifyAccess();
    }
}
