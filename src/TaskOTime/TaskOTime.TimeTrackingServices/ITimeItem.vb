Namespace ActiveDevelop.TimeTrackingServices
    ''' <summary>
    '''  beschrijft een tijdregel met optionele tijdswaarden en bewerkbare buurverwijzingen.
    ''' </summary>
    ''' <remarks>
    '''  dit contract schrijft geen sortering of wijzigingsmeldingen voor.  een ontbrekende duur
    '''  blijft onderscheiden van een gemeten duur van nul; de implementatie bepaalt de berekening.
    ''' </remarks>
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
