using System;

namespace ActiveDevelop.TimeTrackingServices
{
    // '' <summary>
    // ''  beschrijft een tijdregel met optionele tijdswaarden en bewerkbare buurverwijzingen.
    // '' </summary>
    // '' <remarks>
    // ''  dit contract schrijft geen sortering of wijzigingsmeldingen voor.  een ontbrekende duur
    // ''  blijft onderscheiden van een gemeten duur van nul; de implementatie bepaalt de berekening.
    // '' </remarks>
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