Namespace ActiveDevelop.TimeTrackingServices
    Public Interface IEventSource(Of EventSourceIDType As {Structure, IComparable(Of EventSourceIDType)})
        Inherits IComparable(Of IEventSource(Of EventSourceIDType))

        Property Id As EventSourceIDType
    End Interface
End Namespace
