using System;
using System.Collections.Generic;

namespace TaskOTime.AppServer.Models
{
    public sealed class UserAnalysisRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdUser { get; set; }

        public DateTimeOffset ReferenceDate { get; set; }
    }

    public sealed class ProjectAnalysisRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdProject { get; set; }

        public DateTimeOffset ReferenceDate { get; set; }

        public bool IncludeUserBreakdown { get; set; }
    }

    public sealed class TeamAnalysisRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public DateTimeOffset ReferenceDate { get; set; }

        public IReadOnlyList<Guid> IdProjectFilter { get; set; }

        public IReadOnlyList<Guid> IdUserFilter { get; set; }
    }

    public sealed class LastBookingRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid? IdUser { get; set; }

        public Guid? IdProject { get; set; }
    }

    public sealed class StatementRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid? IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public DateTimeOffset ReferenceDate { get; set; }

        public bool IncludeNotes { get; set; }
    }

    public sealed class TenantAdminStatisticsRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public DateTimeOffset ReferenceDate { get; set; }
    }
}
