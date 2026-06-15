namespace TaskOTime.AppServer.Models
{
    public sealed class AuthenticationResult
    {
        public TenantUserDto User { get; set; }

        public bool MustChangePassword { get; set; }
    }
}
