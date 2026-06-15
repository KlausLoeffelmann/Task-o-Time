Imports System.Collections
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.ComponentModel

Namespace ActiveDevelop.TimeTrackingServices
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

        Public Function PredictNewPosition(item As TimeItemType) As Integer
            If item IsNot Nothing AndAlso item.PreviousItem IsNot Nothing AndAlso item.PreviousItem.EventTime.HasValue AndAlso item.EventTime.HasValue AndAlso item.PreviousItem.EventTime.Value > item.EventTime.Value Then
                Return -1
            End If

            If item IsNot Nothing AndAlso item.NextItem IsNot Nothing AndAlso item.NextItem.EventTime.HasValue AndAlso item.EventTime.HasValue AndAlso item.NextItem.EventTime.Value < item.EventTime.Value Then
                Return 1
            End If

            Return 0
        End Function

        Public Function AddWithPositionInfo(item As TimeItemType) As Integer
            If item Is Nothing Then
                Throw New ArgumentNullException(NameOf(item))
            End If

            If _sortedList.Count = 0 Then
                item.ItemState = TimeItemState.Added
                _sortedList.Add(item)
                UpdateItem(item, 0)
                WirePropertyChangeEvent(item)
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, 0))
                OnPropertyChanged(_countPropertyChangedEventArgs)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
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
            If index = _sortedList.Count Then
                _sortedList.Add(item)
            Else
                _sortedList.Insert(index, item)
            End If

            WirePropertyChangeEvent(item)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index))
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            UpdateItem(item, index)
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

        Private Shared Function CalculateDuration(previous As TimeItemType, [next] As TimeItemType) As TimeSpan?
            If previous Is Nothing OrElse [next] Is Nothing OrElse Not previous.EventTime.HasValue OrElse Not [next].EventTime.HasValue Then
                Return Nothing
            End If

            Return [next].EventTime.Value - previous.EventTime.Value
        End Function

        Private Sub SetItemInternal(item As TimeItemType, Optional oldValue As TimeItemType = Nothing)
            If item Is Nothing Then
                Throw New ArgumentNullException(NameOf(item))
            End If

            Dim oldIndex As Integer
            Dim oldItem As TimeItemType

            CheckForSameEventTime(item)

            If oldValue IsNot Nothing Then
                oldIndex = _sortedList.IndexOf(oldValue)
                If oldIndex < 0 Then
                    Throw New InvalidOperationException("The time item is not part of this collection.")
                End If

                oldItem = _sortedList(oldIndex)
                item.PreviousItem = oldItem.PreviousItem
                item.NextItem = oldItem.NextItem
            Else
                oldIndex = _sortedList.IndexOf(item)
                If oldIndex < 0 Then
                    Throw New InvalidOperationException("The time item is not part of this collection.")
                End If

                oldItem = _sortedList(oldIndex)
            End If

            Dim predictedPosition = PredictNewPosition(item)

            If predictedPosition = 0 Then
                UnwirePropertyChangeEvent(_sortedList(oldIndex))
                _sortedList(oldIndex).DurationToNext = Nothing
                _sortedList(oldIndex).DurationToPrevious = Nothing
                _sortedList(oldIndex) = item
                WirePropertyChangeEvent(_sortedList(oldIndex))
                UpdateItem(item, oldIndex)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, oldIndex))
            Else
                RemoveAtCore(oldIndex)
                OnPropertyChanged(_countPropertyChangedEventArgs)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
                OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, oldIndex))
                AddWithPositionInfo(item)
                OnPropertyChanged(_countPropertyChangedEventArgs)
                OnPropertyChanged(_itemPropertyChangedEventArgs)
            End If
        End Sub

        Private Sub CheckForSameEventTime(item As TimeItemType)
            For Each existingItem In _sortedList
                If Object.Equals(existingItem.IDTimeItem, item.IDTimeItem) Then
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

        Public Sub Clear() Implements ICollection(Of TimeItemType).Clear, IList.Clear
            For Each timeItem In _sortedList
                UnwirePropertyChangeEvent(timeItem)
            Next

            _sortedList.Clear()
            OnPropertyChanged(_countPropertyChangedEventArgs)
            OnPropertyChanged(_itemPropertyChangedEventArgs)
            OnNotifyCollectionChanged(New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset))
        End Sub

        Public Function Contains(item As TimeItemType) As Boolean Implements ICollection(Of TimeItemType).Contains
            Return item IsNot Nothing AndAlso _sortedList.BinarySearch(item, _comparer) >= 0
        End Function

        Public Sub CopyTo(array As TimeItemType(), arrayIndex As Integer) Implements ICollection(Of TimeItemType).CopyTo
            _sortedList.CopyTo(array, arrayIndex)
        End Sub

        Public Function IndexOf(item As TimeItemType) As Integer Implements IList(Of TimeItemType).IndexOf
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
