Namespace ActiveDevelop.TimeTrackingServices
    Public Interface ITimeItem(Of IndexType As {Structure, IComparable(Of IndexType)})
        Property IDTimeItem As IndexType
        Property IsStartAction As Boolean?
        Property IsEndAction As Boolean?
        Property EventTime As DateTimeOffset?
        Property DurationToNext As TimeSpan?
        Property DurationToPrevious As TimeSpan?
        Property PreviousItem As ITimeItem(Of IndexType)
        Property NextItem As ITimeItem(Of IndexType)
        Property ProcessEndpointItem As ITimeItem(Of IndexType)
        Property ItemState As TimeItemState
        Property LastChanged As DateTimeOffset?
        Property DateCreated As DateTimeOffset?
    End Interface
End Namespace
