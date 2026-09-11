using System;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.TimeBooking
{
    public sealed class TimeBookingOptions
    {
        public const string DownTimeEventInfo = "DownTime";
        public const string ErrandEventInfo = "Errand";
        public int TimeEventType { get; set; } = (int)TimeBookingEventType.Time;

        public Guid WorkBreakCategoryId { get; set; } = SystemTimeMarkerIds.WorkBreakCategoryId;

        public Guid StopMarkCategoryId { get; set; } = SystemTimeMarkerIds.StopMarkCategoryId;

        public Guid? WorkBreakProjectId { get; set; }

        public TimeSpan BookingDateThreshold { get; set; } = TimeSpan.FromHours(12);

        public bool IgnoreDeletedItems { get; set; } = true;

        public SystemTimeMarkerKind GetMarkerKind(TimeItem timeItem)
        {
            if (timeItem == null)
            {
                return SystemTimeMarkerKind.Normal;
            }

            if (timeItem.EventInfo == ErrandEventInfo)
            {
                return SystemTimeMarkerKind.Errand;
            }

            if (timeItem.EventInfo == DownTimeEventInfo)
            {
                return SystemTimeMarkerKind.DownTime;
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
