using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class CreateTenantAdminRequest
    {
        public string TenantName { get; set; }

        public string TenantIdentifier { get; set; }

        public string Description { get; set; }

        public string UserIdent { get; set; }

        public string FirstName { get; set; }

        public string MiddleName { get; set; }

        public string LastName { get; set; }

        public string EMail { get; set; }

        public string TemporaryPassword { get; set; }

        public DateTimeOffset? PreliminaryPasswordExpiresAt { get; set; }
    }
}
