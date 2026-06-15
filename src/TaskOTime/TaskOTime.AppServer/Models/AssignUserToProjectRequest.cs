using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class AssignUserToProjectRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdProject { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdAssignedByUser { get; set; }

        public int AssignmentRole { get; set; }

        public bool CanBookTime { get; set; } = true;

        public bool CanManageTasks { get; set; }

        public bool CanManageProject { get; set; }

        public int? DisplayOrder { get; set; }
    }
}
