using System;
using System.Collections.Generic;

namespace ActiveDevelop.TimeTrackingServices
{
    public class TimeItemByEventTimeComparer<IndexType, TimeItemType> : IComparer<TimeItemType>
where IndexType : struct, IComparable<IndexType>
where TimeItemType : class, ITimeItem<IndexType>, new()
    {

        public TimeItemType IgnoreOnce { get; set; }

        public int Compare(TimeItemType x, TimeItemType y)
        {
            if (IgnoreOnce is not null && x is not null && Equals(x.IDTimeItem, IgnoreOnce.IDTimeItem))
            {
                IgnoreOnce = null;
                return -1;
            }

            var xEventTime = x is null ? default : x.EventTime;
            var yEventTime = y is null ? default : y.EventTime;

            if (!xEventTime.HasValue && !yEventTime.HasValue)
            {
                return 0;
            }

            if (!xEventTime.HasValue)
            {
                return -1;
            }

            if (!yEventTime.HasValue)
            {
                return 1;
            }

            return xEventTime.Value.CompareTo(yEventTime.Value);
        }
    }
}