using System;

namespace ActiveDevelop.TimeTrackingServices
{
    public interface IActionTarget<ActionTargetIDType> : IComparable<IActionTarget<ActionTargetIDType>> where ActionTargetIDType : struct, IComparable<ActionTargetIDType>
    {

        ActionTargetIDType Id { get; set; }
    }
}