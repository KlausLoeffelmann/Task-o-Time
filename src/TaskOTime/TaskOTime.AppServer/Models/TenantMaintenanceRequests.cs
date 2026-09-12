using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class GetTenantRequest
    {
        public Guid IdTenant { get; set; }
        public Guid IdActingUser { get; set; }
    }

    /// <summary>Updates only the tenant fields exposed by the maintenance editor.</summary>
    public sealed class UpdateTenantRequest
    {
        public Guid IdTenant { get; set; }
        public Guid IdActingUser { get; set; }
        public string TenantName { get; set; }
        public bool IsActive { get; set; }
    }
}
