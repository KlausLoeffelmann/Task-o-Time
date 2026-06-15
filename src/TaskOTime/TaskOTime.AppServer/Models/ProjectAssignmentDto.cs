using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class ProjectAssignmentDto
    {
        public Guid IdProjectUserAssignment { get; set; }

        public Guid IdProject { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdAssignedByUser { get; set; }

        public int AssignmentRole { get; set; }

        public bool CanBookTime { get; set; }

        public bool CanManageTasks { get; set; }

        public bool CanManageProject { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public DateTimeOffset DateAssigned { get; set; }

        public DateTimeOffset? DateRemoved { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }
    }
}
