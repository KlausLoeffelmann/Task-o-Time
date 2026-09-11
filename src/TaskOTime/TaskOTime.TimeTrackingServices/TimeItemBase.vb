Namespace ActiveDevelop.TimeTrackingServices
    ''' <summary>
    '''  bewaart een tijdregel met wijzigingsmeldingen, optionele tijdstippen en verwijzingen naar buren.
    '''  de regel beheert geen eigen sorteerlijst.  een omringende collectie kan de buurrelaties wijzigen.
    ''' </summary>
    ''' <remarks>
    '''  ontbrekende actievlaggen zijn niet zonder meer gelijk aan onwaar: zij kunnen een berekening
    '''  overslaan.  de collectieberekening van tijdsafstanden staat los van deze vlagafhankelijke logica.
    '''  eigenschapsmeldingen worden meteen doorgegeven en vormen geen gezamenlijke wijzigingstransactie.
    ''' </remarks>
    Public Class TimeItemBase
        Inherits ObservableObject
        Implements ITimeItem(Of Guid)

        Public Event EventTimeChanged As EventHandler
        Public Event ActionTargetInternalChanged As EventHandler

        Private _durationToNext As TimeSpan?
        Private _durationToPrevious As TimeSpan?
        Private _eventTime As DateTimeOffset?
        Private _idTimeItem As Guid
        Private _isStartAction As Boolean?
        Private _isEndAction As Boolean?
        Private _itemState As TimeItemState
        Private _lastChanged As DateTimeOffset?
        Private _isNotTimeItem As Boolean
        Private _processEndpointItem As ITimeItem(Of Guid)
        Private _previousItem As ITimeItem(Of Guid)
        Private _nextItem As ITimeItem(Of Guid)
        Private _dateCreated As DateTimeOffset?

        Public Property IDTimeItem As Guid Implements ITimeItem(Of Guid).IDTimeItem
            Get
                Return _idTimeItem
            End Get
            Set(value As Guid)
                SetProperty(_idTimeItem, value)
            End Set
        End Property

        Public Property DurationToNext As TimeSpan? Implements ITimeItem(Of Guid).DurationToNext
            Get
                Return _durationToNext
            End Get
            Set(value As TimeSpan?)
                SetProperty(_durationToNext, value)
            End Set
        End Property

        Public Property DurationToPrevious As TimeSpan? Implements ITimeItem(Of Guid).DurationToPrevious
            Get
                Return _durationToPrevious
            End Get
            Set(value As TimeSpan?)
                SetProperty(_durationToPrevious, value)
            End Set
        End Property

        ''' <summary>
        '''  meldt een gewijzigd tijdstip voordat de afzonderlijke tijdstipmelding en duurberekening volgen.
        ''' </summary>
        ''' <remarks>
        '''  bij een uitzondering wordt alleen het tijdstipveld teruggezet.  reeds uitgevoerde reacties
        '''  van afnemers worden niet teruggedraaid en er volgt geen extra herstelmelding.
        ''' </remarks>
        Public Property EventTime As DateTimeOffset? Implements ITimeItem(Of Guid).EventTime
            Get
                Return _eventTime
            End Get
            Set(value As DateTimeOffset?)
                Dim oldEventTime = _eventTime

                Try
                    If SetProperty(_eventTime, value) Then
                        OnEventTimeChanged()
                        CalculateDurationToLinkedItems()
                    End If
                Catch
                    _eventTime = oldEventTime
                    Throw
                End Try
            End Set
        End Property

        Protected Overridable Sub OnEventTimeChanged()
            RaiseEvent EventTimeChanged(Me, EventArgs.Empty)
        End Sub

        Protected Overridable Sub OnActionTargetInternalChanged()
            RaiseEvent ActionTargetInternalChanged(Me, EventArgs.Empty)
        End Sub

        Public Property IsStartAction As Boolean? Implements ITimeItem(Of Guid).IsStartAction
            Get
                Return _isStartAction
            End Get
            Set(value As Boolean?)
                SetProperty(_isStartAction, value)
                CalculateDurationToLinkedItems()
            End Set
        End Property

        Public Property IsEndAction As Boolean? Implements ITimeItem(Of Guid).IsEndAction
            Get
                Return _isEndAction
            End Get
            Set(value As Boolean?)
                SetProperty(_isEndAction, value)
                CalculateDurationToLinkedItems()
            End Set
        End Property

        Public Property ItemState As TimeItemState Implements ITimeItem(Of Guid).ItemState
            Get
                Return _itemState
            End Get
            Set(value As TimeItemState)
                SetProperty(_itemState, value)
            End Set
        End Property

        Public Property LastChanged As DateTimeOffset? Implements ITimeItem(Of Guid).LastChanged
            Get
                Return _lastChanged
            End Get
            Set(value As DateTimeOffset?)
                SetProperty(_lastChanged, value)
            End Set
        End Property

        Public Property DateCreated As DateTimeOffset? Implements ITimeItem(Of Guid).DateCreated
            Get
                Return _dateCreated
            End Get
            Set(value As DateTimeOffset?)
                SetProperty(_dateCreated, value)
            End Set
        End Property

        Public Property NextItem As ITimeItem(Of Guid) Implements ITimeItem(Of Guid).NextItem
            Get
                Return _nextItem
            End Get
            Set(value As ITimeItem(Of Guid))
                SetProperty(_nextItem, value)
                CalculateDurationToLinkedItems()
            End Set
        End Property

        Public Property PreviousItem As ITimeItem(Of Guid) Implements ITimeItem(Of Guid).PreviousItem
            Get
                Return _previousItem
            End Get
            Set(value As ITimeItem(Of Guid))
                SetProperty(_previousItem, value)
                CalculateDurationToLinkedItems()
            End Set
        End Property

        Public Property ProcessEndpointItem As ITimeItem(Of Guid) Implements ITimeItem(Of Guid).ProcessEndpointItem
            Get
                Return _processEndpointItem
            End Get
            Set(value As ITimeItem(Of Guid))
                SetProperty(_processEndpointItem, value)
            End Set
        End Property

        Public Property IsNotTimeItem As Boolean
            Get
                Return _isNotTimeItem
            End Get
            Set(value As Boolean)
                SetProperty(_isNotTimeItem, value)
            End Set
        End Property

        ''' <summary>
        '''  berekent per richting alleen wanneer de betreffende buur en de bijbehorende actievlag bestaan.
        ''' </summary>
        ''' <remarks>
        '''  binnen zo'n berekening wist een begin- of eindactie de duur, net als een ontbrekend tijdstip.
        '''  ontbreekt de buur of de benodigde vlag, dan blijft de bestaande duur in die richting staan.
        ''' </remarks>
        Protected Overridable Sub CalculateDurationToLinkedItems()
            If PreviousItem IsNot Nothing AndAlso IsStartAction.HasValue Then
                If IsStartAction.Value OrElse If(IsEndAction, False) OrElse Not EventTime.HasValue OrElse Not PreviousItem.EventTime.HasValue Then
                    DurationToPrevious = Nothing
                Else
                    DurationToPrevious = EventTime.Value - PreviousItem.EventTime.Value
                End If
            End If

            If NextItem IsNot Nothing AndAlso IsEndAction.HasValue Then
                If IsEndAction.Value OrElse If(IsStartAction, False) OrElse Not EventTime.HasValue OrElse Not NextItem.EventTime.HasValue Then
                    DurationToNext = Nothing
                Else
                    DurationToNext = NextItem.EventTime.Value - EventTime.Value
                End If
            End If
        End Sub

        Public Overrides Function ToString() As String
            Dim current = If(EventTime.HasValue, EventTime.Value.ToString("yy-MM-dd HH:mm"), "##-##-## ##:##")
            Dim previous = If(PreviousItem IsNot Nothing AndAlso PreviousItem.EventTime.HasValue, PreviousItem.EventTime.Value.ToString("yy-MM-dd HH:mm"), "##-##-## ##:##")
            Dim [next] = If(NextItem IsNot Nothing AndAlso NextItem.EventTime.HasValue, NextItem.EventTime.Value.ToString("yy-MM-dd HH:mm"), "##-##-## ##:##")

            Return $"--> {current} (<-- {previous} --> {[next]}"
        End Function
    End Class
End Namespace
