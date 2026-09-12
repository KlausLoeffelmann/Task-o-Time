using System;

namespace ActiveDevelop.TimeTrackingServices
{
    public interface IEventSource<EventSourceIDType> : IComparable<IEventSource<EventSourceIDType>> where EventSourceIDType : struct, IComparable<EventSourceIDType>
    {

        EventSourceIDType Id { get; set; }
    }
}