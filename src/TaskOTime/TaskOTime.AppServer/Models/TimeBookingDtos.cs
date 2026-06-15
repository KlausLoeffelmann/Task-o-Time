using System;
using System.Collections.Generic;
using TaskOTime.AppServer.TimeBooking;

namespace TaskOTime.AppServer.Models
{
    public sealed class TimeBookingItemDto
    {
        public Guid IdTimeItem { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid IdProject { get; set; }

        public Guid? IdTask { get; set; }

        public Guid IdCategory { get; set; }

        public string ShortTitle { get; set; }

        public string Description { get; set; }

        public DateTimeOffset? EventTime { get; set; }

        public DateTime? BookingDate { get; set; }

        public string EventInfo { get; set; }

        public int EventTypeInfo { get; set; }

        public SystemTimeMarkerKind MarkerKind { get; set; }

        public TimeSpan? DurationToNext { get; set; }

        public TimeSpan? DurationToPrevious { get; set; }

        public Guid? IdParentItem { get; set; }

        public bool IsItemCompleted { get; set; }

        public bool IsItemDeleted { get; set; }

        public bool IsStartAction { get; set; }

        public bool IsEndAction { get; set; }

        public decimal? Value { get; set; }

        public int Priority { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public string ExternalId { get; set; }
    }

    public sealed class TimeBookingDayDto
    {
        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public DateTime BookingDate { get; set; }

        public IReadOnlyList<TimeBookingItemDto> Items { get; set; }

        public TimeSpan TotalBookedTime { get; set; }

        public TimeSpan WorkBreakTime { get; set; }

        public DateTimeOffset? FirstBookingAt { get; set; }

        public DateTimeOffset? LastBookingAt { get; set; }
    }

    public sealed class TimeBookingMutationResult
    {
        public TimeBookingDayDto BookingDay { get; set; }

        public TimeBookingItemDto AffectedItem { get; set; }

        public IReadOnlyList<TimeBookingItemDto> RemovedItems { get; set; }
    }

    public sealed class TimeBookingTemplateDto
    {
        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid IdProject { get; set; }

        public string ProjectName { get; set; }

        public Guid IdCategory { get; set; }

        public string CategoryName { get; set; }

        public Guid? IdTask { get; set; }

        public string TaskItemName { get; set; }

        public string ShortTitle { get; set; }

        public string Description { get; set; }

        public DateTimeOffset LastUsedAt { get; set; }

        public int UsageCount { get; set; }
    }
}
