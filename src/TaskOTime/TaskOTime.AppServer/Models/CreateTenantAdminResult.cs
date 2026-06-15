namespace TaskOTime.AppServer.Models
{
    public sealed class CreateTenantAdminResult
    {
        public TenantDto Tenant { get; set; }

        public TenantUserDto AdminUser { get; set; }
    }
}
