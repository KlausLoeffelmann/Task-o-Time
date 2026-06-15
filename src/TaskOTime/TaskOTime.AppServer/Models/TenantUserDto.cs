using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class TenantUserDto
    {
        public Guid IdUser { get; set; }

        public Guid IdTenant { get; set; }

        public string UserIdent { get; set; }

        public string FirstName { get; set; }

        public string MiddleName { get; set; }

        public string LastName { get; set; }

        public string EMail { get; set; }

        public bool IsAdmin { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public bool MustChangePassword { get; set; }

        public DateTimeOffset? PasswordChangedAt { get; set; }

        public DateTimeOffset? PreliminaryPasswordExpiresAt { get; set; }

        public int FailedLoginCount { get; set; }

        public DateTimeOffset? LockoutUntil { get; set; }

        public DateTimeOffset LastLogin { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public DateTimeOffset? DateDeactivated { get; set; }

        public DateTimeOffset? DateDeleted { get; set; }
    }
}
