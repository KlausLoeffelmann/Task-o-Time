using System;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.TimeBooking
{
    public sealed class TimeBookingOptions
    {
        public int TimeEventType { get; set; } = (int)TimeBookingEventType.Time;

        public Guid WorkBreakCategoryId { get; set; } = LegacySystemTimeMarkerIds.WorkBreak;

        public Guid StopMarkCategoryId { get; set; } = LegacySystemTimeMarkerIds.StopMark;

        public Guid? WorkBreakProjectId { get; set; }

        public TimeSpan BookingDateThreshold { get; set; } = TimeSpan.FromHours(12);

        public bool IgnoreDeletedItems { get; set; } = true;

        public SystemTimeMarkerKind GetMarkerKind(TimeItem timeItem)
        {
            if (timeItem == null)
            {
                return SystemTimeMarkerKind.Normal;
            }

            if (timeItem.IdCategory == WorkBreakCategoryId)
            {
                return SystemTimeMarkerKind.WorkBreak;
            }

            if (timeItem.IdCategory == StopMarkCategoryId)
            {
                return SystemTimeMarkerKind.StopMark;
            }

            return SystemTimeMarkerKind.Normal;
        }
    }
}
