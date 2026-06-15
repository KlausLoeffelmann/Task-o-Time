using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class TenantDto
    {
        public Guid IdTenant { get; set; }

        public string TenantName { get; set; }

        public string TenantIdentifier { get; set; }

        public string Description { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }
    }
}
