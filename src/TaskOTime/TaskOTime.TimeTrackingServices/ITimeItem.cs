using System;

namespace ActiveDevelop.TimeTrackingServices
{
    /// <summary>
    /// Represents a time item with nullable time values and mutable references to neighboring items.
    /// </summary>
    /// <remarks>
    /// This contract does not require sorting or change notifications. A missing duration remains
    /// distinct from a measured duration of zero; implementations determine how durations are calculated.
    /// </remarks>
    public interface ITimeItem<IndexType> where IndexType : struct, IComparable<IndexType>
    {
        IndexType IDTimeItem { get; set; }
        bool? IsStartAction { get; set; }
        bool? IsEndAction { get; set; }
        DateTimeOffset? EventTime { get; set; }
        TimeSpan? DurationToNext { get; set; }
        TimeSpan? DurationToPrevious { get; set; }
        ITimeItem<IndexType> PreviousItem { get; set; }
        ITimeItem<IndexType> NextItem { get; set; }
        ITimeItem<IndexType> ProcessEndpointItem { get; set; }
        TimeItemState ItemState { get; set; }
        DateTimeOffset? LastChanged { get; set; }
        DateTimeOffset? DateCreated { get; set; }
    }
}