using System.Collections.Generic;
using System.Linq;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.TimeBooking
{
    public sealed class TimeBookingChangeSet
    {
        public TimeBookingChangeSet(IEnumerable<TimeItem> timelineItems, IEnumerable<TimeItem> removedItems, TimeItem affectedItem)
        {
            TimelineItems = (timelineItems ?? Enumerable.Empty<TimeItem>()).ToList();
            RemovedItems = (removedItems ?? Enumerable.Empty<TimeItem>()).ToList();
            AffectedItem = affectedItem;
        }

        public IReadOnlyList<TimeItem> TimelineItems { get; }

        public IReadOnlyList<TimeItem> RemovedItems { get; }

        public TimeItem AffectedItem { get; }

        public bool HasRemovedItems
        {
            get { return RemovedItems.Count > 0; }
        }
    }
}
