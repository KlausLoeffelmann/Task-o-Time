Imports System.Collections
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.ComponentModel

Namespace ActiveDevelop.TimeTrackingServices
    ''' <summary>
    '''  bewaart tijdregels in een eigen gesorteerde lijst en meldt wijzigingen aan afnemers.
    '''  implementeert zelf de lijst- en collectiecontracten; dit is geen afgeleide van
    '''  <see cref="System.Collections.ObjectModel.ObservableCollection(Of TimeItemType)"/>.
    ''' </summary>
    ''' <remarks>
    '''  de volgorde volgt <see cref="ITimeItem(Of IndexType).EventTime"/>, niet de invoegvolgorde.
    '''  sorteren en herladen kunnen hetzelfde collectieobject blijven gebruiken.  de koppelingen
    '''  tussen naburige regels en de bijbehorende meldingen worden hier afzonderlijk onderhouden.
    ''' </remarks>
    Public Class TimeItemsBase(Of IndexType As {Structure, IComparable(Of IndexType)}, TimeItemType As {Class, ITimeItem(Of IndexType), INotifyPropertyChanged, New})
        Implements IEnumerable(Of TimeItemType)
        Implements ICollection(Of TimeItemType)
        Implements IList(Of TimeItemType)
        Implements IList
        Implements INotifyCollectionChanged
        Implements INotifyPropertyChanged

        Private Const EventTimePropertyName As String = "EventTime"
        Private ReadOnly _countPropertyChangedEventArgs As New PropertyChangedEventArgs(NameOf(Count))
        Private ReadOnly _itemPropertyChangedEventArgs As New PropertyChangedEventArgs("Item[]")
        Private ReadOnly _sortedList As New List(Of TimeItemType)()
        Private ReadOnly _comparer As New TimeItemByEventTimeComparer(Of IndexType, TimeItemType)()

        Public Event CollectionChanged As NotifyCollectionChangedEventHandler Implements INotifyCollectionChanged.CollectionChanged
        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Private Sub AddRangeSuspended(range As IEnumerable(Of TimeItemType))
            For Each timeItem In range
                Add(timeItem)
            Next
        End Sub

        Public Sub Add(eventTime As DateTime, newID As IndexType)
            Dim timeItem As New TimeItemType() With {
                .IDTimeItem = newID,
                .EventTime = New DateTimeOffset(eventTime),
                .DateCreated = DateTimeOffset.Now,
                .LastChanged = DateTimeOffset.Now
            }

            Add(timeItem)
        End Sub

        ''' <summary>
        '''  vergelijkt alleen met de gekoppelde buren en geeft een richting, geen invoegindex.
        ''' </summary>
        ''' <remarks>
        '''  vergelijkingen met ontbrekende regels of tijdstippen worden overgeslagen.  nul bewijst dus niet
        '''  dat een tijdstip uniek is; die controle staat los van deze voorspelling.
        ''' </remarks>
        Public Function PredictNewPosition(item As TimeItemType) As Integer
            If item IsNot Nothing AndAlso item.PreviousItem IsNot Nothing AndAlso item.PreviousItem.EventTime.HasValue AndAlso item.EventTime.HasValue AndAlso item.PreviousItem.EventTime.Value > item.EventTime.Value Then
                Return -1
            End If

            If item IsNot Nothing AndAlso item.NextItem IsNot Nothing AndAlso item.NextItem.EventTime.HasValue AndAlso item.EventTime.HasValue AndAlso item.NextItem.EventTime.Value < item.EventTime.Value Then
                Return 1
            End If

            Return 0
        End Function

        ''' <summary>
        '''  voegt een tijdregel volgens de tijdvolgorde in en geeft de invoegpositie terug.
        ''' </summary>
        ''' <remarks>
        '''  een ontbrekend object wordt geweigerd.  een object zonder tijdstip mag wel vooraan staan.
        '''  gelijke tijdstippen worden geweigerd, ook als beide tijdstippen ontbreken.
        '''  na invoegen volgen meldingen voor <see cref="Count"/> en <see cref="Item(Integer)"/>.
        '''  de collectiemelding bevat daarna het toegevoegde object en zijn positie.
        ''' </remarks>
        Public Function AddWithPositionInfo(item As TimeItemType) As Integer
            If item Is Nothing Then
                Throw New ArgumentNullException(NameOf(item))
            End If

            If _sortedList.Count = 0 Then
                item.ItemState = TimeItemState.Added
                _sortedList.Add(item)
                UpdateItem(item, 0)
                WirePropertyChangeEvent(item)
                OnPropertyChanged(_countPropertyChangedEventArgs)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, 0))
                Return 0
            End If

            Dim index = _sortedList.BinarySearch(item, _comparer)

            If index >= 0 Then
                ThrowSamePointInTimeException(item)
            End If

            Dim insertionIndex = -index - 1
            InsertWithUpdate(item, insertionIndex)
            Return insertionIndex
        End Function

        Public Sub Add(item As TimeItemType) Implements ICollection(Of TimeItemType).Add
            AddWithPositionInfo(item)
        End Sub

        Private Shared Sub ThrowSamePointInTimeException(item As TimeItemType)
            Throw New ArgumentException("The same point in time can't be recorded twice for one EventSource: " & item.ToString())
        End Sub

        Private Sub InsertWithUpdate(item As TimeItemType, index As Integer)
            item.ItemState = TimeItemState.Added

            If index = _sortedList.Count Then
                _sortedList.Add(item)
            Else
                _sortedList.Insert(index, item)
            End If

            WirePropertyChangeEvent(item)
            UpdateItem(item, index)
            ' de teller beschrijft de omvang; de indexermelding maakt gewijzigde posities zichtbaar.
            ' de collectiemelding draagt daarnaast het toegevoegde object en zijn positie.
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index))
        End Sub

        Private Function UpdateItem(item As TimeItemType, index As Integer) As TimeItemType
            Dim previous = If(index > 0, _sortedList(index - 1), Nothing)
            Dim [next] = If(index < _sortedList.Count - 1, _sortedList(index + 1), Nothing)

            item.PreviousItem = Nothing
            item.NextItem = Nothing
            item.DurationToPrevious = Nothing
            item.DurationToNext = Nothing

            LinkItems(previous, item)
            LinkItems(item, [next])

            Return item
        End Function

        ''' <summary>
        '''  legt beide richtingen van een buurrelatie vast met dezelfde berekende tijdsafstand.
        ''' </summary>
        ''' <remarks>
        '''  ook aan een uiteinde wordt de aanwezige buur bijgewerkt.  daar ontbreken zowel
        '''  de verwijzing naar buiten als de duur; een ontbrekende duur is niet hetzelfde als nul.
        ''' </remarks>
        Private Shared Sub LinkItems(previous As TimeItemType, [next] As TimeItemType)
            Dim duration = CalculateDuration(previous, [next])

            If previous IsNot Nothing Then
                previous.NextItem = [next]
                previous.DurationToNext = duration
            End If

            If [next] IsNot Nothing Then
                [next].PreviousItem = previous
                [next].DurationToPrevious = duration
            End If
        End Sub

        ''' <summary>
        '''  berekent het tijdsverschil, of geen waarde als een buur of een tijdstip ontbreekt.
        ''' </summary>
        ''' <remarks>
        '''  deze collectieberekening leest geen actievlaggen.  zij beschrijft de afstand tussen
        '''  twee tijdstippen, niet zelfstandig de betekenis van een boeking of onderbreking.
        ''' </remarks>
        Private Shared Function CalculateDuration(previous As TimeItemType, [next] As TimeItemType) As TimeSpan?
            If previous Is Nothing OrElse [next] Is Nothing OrElse Not previous.EventTime.HasValue OrElse Not [next].EventTime.HasValue Then
                Return Nothing
            End If

            Return [next].EventTime.Value - previous.EventTime.Value
        End Function

        ''' <summary>
        '''  controleert tijdstipconflicten voordat een bestaande regel wordt verplaatst of vervangen.
        ''' </summary>
        ''' <param name="oldValue">
        '''  de te vervangen regel; zonder deze waarde wordt het bestaande object opnieuw geplaatst.
        ''' </param>
        ''' <remarks>
        '''  vervanging op dezelfde positie meldt een vervanging en alleen de indexerwijziging.
        '''  bij een andere positie wordt eerst verwijderd en daarna toegevoegd, met beide tellermeldingen.
        ''' </remarks>
        Private Sub SetItemInternal(item As TimeItemType, Optional oldValue As TimeItemType = Nothing)
            If item Is Nothing Then
                Throw New ArgumentNullException(NameOf(item))
            End If

            Dim oldItem = If(oldValue, item)
            Dim oldIndex = _sortedList.IndexOf(oldItem)

            If oldIndex < 0 Then
                Throw New InvalidOperationException("The time item is not part of this collection.")
            End If

            CheckForSameEventTime(item, oldItem)

            If oldValue Is Nothing Then
                RepositionItem(item, oldIndex)
                Return
            End If

            item.PreviousItem = oldItem.PreviousItem
            item.NextItem = oldItem.NextItem

            If PredictNewPosition(item) = 0 Then
                UnwirePropertyChangeEvent(oldItem)
                oldItem.DurationToNext = Nothing
                oldItem.DurationToPrevious = Nothing
                _sortedList(oldIndex) = item
                WirePropertyChangeEvent(item)
                UpdateItem(item, oldIndex)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, oldIndex))
                Return
            End If

            RemoveAtCore(oldIndex)
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, oldIndex))
            AddWithPositionInfo(item)
        End Sub

        ''' <summary>
        '''  sluit de oude buurrelatie en koppelt hetzelfde object opnieuw op zijn gesorteerde positie.
        ''' </summary>
        ''' <remarks>
        '''  de omvang verandert niet, dus <see cref="Count"/> wordt niet gemeld.  de indexer wel;
        '''  een verplaatsingsmelding volgt alleen wanneer de uiteindelijke index anders is.
        ''' </remarks>
        Private Sub RepositionItem(item As TimeItemType, oldIndex As Integer)
            Dim previous = If(oldIndex > 0, _sortedList(oldIndex - 1), Nothing)
            Dim [next] = If(oldIndex < _sortedList.Count - 1, _sortedList(oldIndex + 1), Nothing)

            LinkItems(previous, [next])
            _sortedList.RemoveAt(oldIndex)

            Dim index = _sortedList.BinarySearch(item, _comparer)
            Dim newIndex = If(index < 0, -index - 1, index)
            _sortedList.Insert(newIndex, item)
            UpdateItem(item, newIndex)
            OnPropertyChanged(_itemPropertyChangedEventArgs)

            If newIndex <> oldIndex Then
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex))
            End If
        End Sub

        Private Sub CheckForSameEventTime(item As TimeItemType, ignoredItem As TimeItemType)
            For Each existingItem In _sortedList
                If Object.ReferenceEquals(existingItem, ignoredItem) Then
                    Continue For
                End If

                If _comparer.Compare(existingItem, item) = 0 Then
                    ThrowSamePointInTimeException(item)
                End If
            Next
        End Sub

        Public Function Remove(item As TimeItemType) As Boolean Implements ICollection(Of TimeItemType).Remove
            Dim index = _sortedList.BinarySearch(item, _comparer)

            If index < 0 Then
                Return False
            End If

            RemoveAtCore(index)
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index))
            Return True
        End Function

        Protected Overridable Sub OnNotifyCollectionChanged(e As NotifyCollectionChangedEventArgs)
            RaiseEvent CollectionChanged(Me, e)
        End Sub

        Protected Overridable Sub OnPropertyChanged(e As PropertyChangedEventArgs)
            RaiseEvent PropertyChanged(Me, e)
        End Sub

        Private Sub WirePropertyChangeEvent(item As TimeItemType)
            AddHandler item.PropertyChanged, AddressOf PropertyChangeEventHandlerProc
        End Sub

        Private Sub UnwirePropertyChangeEvent(item As TimeItemType)
            RemoveHandler item.PropertyChanged, AddressOf PropertyChangeEventHandlerProc
        End Sub

        ''' <summary>
        '''  herordent alleen bij de melding voor het tijdstip, niet bij meldingen voor buren of duren.
        ''' </summary>
        ''' <remarks>
        '''  de tijdens het herkoppelen ontstane meldingen starten daardoor geen nieuwe sortering.
        ''' </remarks>
        Private Sub PropertyChangeEventHandlerProc(sender As Object, e As PropertyChangedEventArgs)
            If sender IsNot Nothing AndAlso e.PropertyName = EventTimePropertyName Then
                SetItemInternal(DirectCast(sender, TimeItemType))
            End If
        End Sub

        Public Function GetEnumerator() As IEnumerator(Of TimeItemType) Implements IEnumerable(Of TimeItemType).GetEnumerator
            Return _sortedList.GetEnumerator()
        End Function

        Private Function GetUntypedEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return _sortedList.GetEnumerator()
        End Function

        Public ReadOnly Property Count As Integer Implements ICollection(Of TimeItemType).Count, ICollection.Count
            Get
                Return _sortedList.Count
            End Get
        End Property

        ''' <summary>
        '''  verwijdert alle abonnementen op eigenschapswijzigingen en leegt de interne lijst.
        ''' </summary>
        ''' <remarks>
        '''  meldt teller, indexer en een volledige verversing, ook als de lijst al leeg was.
        '''  de oude objecten worden hier niet onderling losgekoppeld.  de collectie zelf blijft bestaan.
        ''' </remarks>
        Public Sub Clear() Implements ICollection(Of TimeItemType).Clear, IList.Clear
            For Each timeItem In _sortedList
                UnwirePropertyChangeEvent(timeItem)
            Next

            _sortedList.Clear()
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset))
        End Sub

        ''' <summary>
        '''  zoekt op tijdstipvergelijking, niet op objectidentiteit; een ontbrekend object geeft onwaar.
        ''' </summary>
        Public Function Contains(item As TimeItemType) As Boolean Implements ICollection(Of TimeItemType).Contains
            Return item IsNot Nothing AndAlso _sortedList.BinarySearch(item, _comparer) >= 0
        End Function

        Public Sub CopyTo(array As TimeItemType(), arrayIndex As Integer) Implements ICollection(Of TimeItemType).CopyTo
            _sortedList.CopyTo(array, arrayIndex)
        End Sub

        Public Function IndexOf(item As TimeItemType) As Integer Implements IList(Of TimeItemType).IndexOf
            ' een ontbrekend object krijgt min één.  een andere misser behoudt het negatieve zoekresultaat.
            If item Is Nothing Then
                Return -1
            End If

            Return _sortedList.BinarySearch(item, _comparer)
        End Function

        Public Function IndexOf(dateOfTimeItem As DateTimeOffset) As Integer
            Dim tmpTimeItem As New TimeItemType() With {.EventTime = dateOfTimeItem}
            Return _sortedList.BinarySearch(tmpTimeItem, _comparer)
        End Function

        Public Sub Insert(index As Integer, item As TimeItemType) Implements IList(Of TimeItemType).Insert
            Throw New NotImplementedException("Inserting TimeItems at a certain position does not apply, since the order is determined by the TimeItem's UtcEventTime value. ")
        End Sub

        Public Sub RemoveAt(index As Integer) Implements IList(Of TimeItemType).RemoveAt, IList.RemoveAt
            Dim item = _sortedList(index)
            RemoveAtCore(index)
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index))
        End Sub

        ''' <summary>
        '''  verbindt de overblijvende buren en verwijdert het abonnement van de vertrekkende regel.
        ''' </summary>
        ''' <remarks>
        '''  wist ook de buurverwijzingen en duren van die regel.  de aanroeper verzorgt de meldingen
        '''  over de gewijzigde collectie; deze stap past alleen de inhoud en koppelingen aan.
        ''' </remarks>
        Private Sub RemoveAtCore(index As Integer)
            Dim removedItem = _sortedList(index)
            Dim previous = If(index > 0, _sortedList(index - 1), Nothing)
            Dim [next] = If(index < _sortedList.Count - 1, _sortedList(index + 1), Nothing)

            LinkItems(previous, [next])
            UnwirePropertyChangeEvent(removedItem)
            removedItem.PreviousItem = Nothing
            removedItem.NextItem = Nothing
            removedItem.DurationToPrevious = Nothing
            removedItem.DurationToNext = Nothing
            _sortedList.RemoveAt(index)
        End Sub

        Public Function Add(value As Object) As Integer Implements IList.Add
            If value Is Nothing Then
                Throw New NullReferenceException("Value for time item cannot be null.")
            End If

            If Not TypeOf value Is TimeItemType Then
                Throw New ArgumentException("Value must be a time item.", NameOf(value))
            End If

            Return AddWithPositionInfo(DirectCast(value, TimeItemType))
        End Function

        Public Function Contains(value As Object) As Boolean Implements IList.Contains
            Return TypeOf value Is TimeItemType AndAlso Contains(DirectCast(value, TimeItemType))
        End Function

        Public Function IndexOf(value As Object) As Integer Implements IList.IndexOf
            If Not TypeOf value Is TimeItemType Then
                Return -1
            End If

            Return IndexOf(DirectCast(value, TimeItemType))
        End Function

        Public Sub Insert(index As Integer, value As Object) Implements IList.Insert
            If value Is Nothing Then
                Throw New NullReferenceException("Value for time item cannot be null.")
            End If

            If Not TypeOf value Is TimeItemType Then
                Throw New ArgumentException("Value must be a time item.", NameOf(value))
            End If

            Insert(index, DirectCast(value, TimeItemType))
        End Sub

        Public Sub Remove(value As Object) Implements IList.Remove
            If TypeOf value Is TimeItemType Then
                Remove(DirectCast(value, TimeItemType))
            End If
        End Sub

        Public Sub CopyTo(array As Array, index As Integer) Implements ICollection.CopyTo
            DirectCast(_sortedList, ICollection).CopyTo(array, index)
        End Sub

        Public ReadOnly Property IsReadOnly As Boolean Implements ICollection(Of TimeItemType).IsReadOnly, IList.IsReadOnly
            Get
                Return False
            End Get
        End Property

        Public ReadOnly Property IsFixedSize As Boolean Implements IList.IsFixedSize
            Get
                Return False
            End Get
        End Property

        Public ReadOnly Property IsSynchronized As Boolean Implements ICollection.IsSynchronized
            Get
                Return False
            End Get
        End Property

        Public ReadOnly Property SyncRoot As Object Implements ICollection.SyncRoot
            Get
                Return DirectCast(_sortedList, ICollection).SyncRoot
            End Get
        End Property

        Default Public Property Item(index As Integer) As TimeItemType Implements IList(Of TimeItemType).Item
            Get
                Return _sortedList(index)
            End Get
            Set(value As TimeItemType)
                If value Is Nothing Then
                    Throw New NullReferenceException("Value for time item cannot be null.")
                End If

                Dim oldValue = _sortedList(index)
                SetItemInternal(value, oldValue)
            End Set
        End Property

        ''' <summary>
        '''  zoekt een exact tijdstip en geeft geen object terug wanneer dat tijdstip niet voorkomt.
        ''' </summary>
        Default Public ReadOnly Property Item(dateOfTimeItem As DateTimeOffset) As TimeItemType
            Get
                Dim tmpTimeItem As New TimeItemType() With {.EventTime = dateOfTimeItem}
                Dim index = _sortedList.BinarySearch(tmpTimeItem, _comparer)

                If index >= 0 Then
                    Return _sortedList(index)
                End If

                Return Nothing
            End Get
        End Property

        Private Property UntypedItem(index As Integer) As Object Implements IList.Item
            Get
                Return Item(index)
            End Get
            Set(value As Object)
                If value Is Nothing Then
                    Throw New NullReferenceException("Value for time item cannot be null.")
                End If

                If Not TypeOf value Is TimeItemType Then
                    Throw New ArgumentException("Value must be a time item.", NameOf(value))
                End If

                Item(index) = DirectCast(value, TimeItemType)
            End Set
        End Property
    End Class
End Namespace
