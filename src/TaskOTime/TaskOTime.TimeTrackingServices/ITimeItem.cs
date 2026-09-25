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
        /// <summary>
        /// Gets or sets the item's identifier, independently of its timestamp or position in a collection.
        /// </summary>
        IndexType IDTimeItem { get; set; }
        /// <summary>
        /// Gets or sets the start-action flag; null leaves the flag unspecified rather than setting it to false.
        /// </summary>
        bool? IsStartAction { get; set; }
        /// <summary>
        /// Gets or sets the end-action flag; implementations determine how an unspecified flag affects duration calculation.
        /// </summary>
        bool? IsEndAction { get; set; }
        /// <summary>
        /// Gets or sets the event timestamp and offset, or null when the event time is not known.
        /// </summary>
        DateTimeOffset? EventTime { get; set; }
        /// <summary>
        /// Gets or sets the duration toward the next item; null means no duration is available, not zero elapsed time.
        /// </summary>
        TimeSpan? DurationToNext { get; set; }
        /// <summary>
        /// Gets or sets the duration from the previous item, retaining a distinction between missing and zero durations.
        /// </summary>
        TimeSpan? DurationToPrevious { get; set; }
        /// <summary>
        /// Gets or sets the preceding item reference; reciprocal links must be maintained by the implementation or owner.
        /// </summary>
        ITimeItem<IndexType> PreviousItem { get; set; }
        /// <summary>
        /// Gets or sets the following item reference, which may be absent at the end of a sequence.
        /// </summary>
        ITimeItem<IndexType> NextItem { get; set; }
        /// <summary>
        /// Gets or sets a process endpoint reference distinct from the immediate neighbors; this contract does not infer it.
        /// </summary>
        ITimeItem<IndexType> ProcessEndpointItem { get; set; }
        /// <summary>
        /// Gets or sets change-tracking state without prescribing persistence or deletion behavior.
        /// </summary>
        TimeItemState ItemState { get; set; }
        /// <summary>
        /// Gets or sets the recorded modification time, or null when no modification timestamp is available.
        /// </summary>
        DateTimeOffset? LastChanged { get; set; }
        /// <summary>
        /// Gets or sets the recorded creation time; implementations or callers are responsible for assigning it.
        /// </summary>
        DateTimeOffset? DateCreated { get; set; }
    }
}