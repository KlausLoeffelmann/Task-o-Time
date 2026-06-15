using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class TimeBookingAccessContextDto
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdBookingUser { get; set; }

        public bool RequireProjectAssignment { get; set; } = true;

        public bool RequireBookingPermission { get; set; } = true;

        public bool RejectCrossTenantProjects { get; set; } = true;
    }

    public sealed class GetBookingDayRequest
    {
        public TimeBookingAccessContextDto AccessContext { get; set; }

        public DateTime BookingDate { get; set; }

        public bool IncludeDeletedItems { get; set; }
    }

    public sealed class SaveTimeBookingRequest
    {
        public TimeBookingAccessContextDto AccessContext { get; set; }

        public TimeBookingItemDto Item { get; set; }
    }

    public sealed class DeleteTimeBookingRequest
    {
        public TimeBookingAccessContextDto AccessContext { get; set; }

        public Guid IdTimeItem { get; set; }

        public DateTime BookingDate { get; set; }
    }

    public sealed class InsertSystemTimeMarkerRequest
    {
        public TimeBookingAccessContextDto AccessContext { get; set; }

        public DateTimeOffset MarkerTime { get; set; }
    }

    public sealed class RecentTimeTemplatesRequest
    {
        public TimeBookingAccessContextDto AccessContext { get; set; }

        public DateTimeOffset? Since { get; set; }

        public int MaximumTemplateCount { get; set; }
    }
}
