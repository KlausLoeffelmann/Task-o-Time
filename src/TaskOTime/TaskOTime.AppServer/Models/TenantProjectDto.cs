using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class TenantProjectDto
    {
        public Guid IdProject { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public string ProjectName { get; set; }

        public int ProjectType { get; set; }

        public int? ProjectNumber { get; set; }

        public string ProjectDescription { get; set; }

        public string ProjectIdentifier { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }
    }
}
