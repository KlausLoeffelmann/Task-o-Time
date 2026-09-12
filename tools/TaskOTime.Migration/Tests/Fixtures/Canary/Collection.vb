Imports System.Collections
Imports System.Collections.Generic
Imports System.Collections.Specialized

' Keep this comment verbatim: sortering is bewust.
Public Class IdentityCollection
    Implements IList, IReadOnlyList(Of String), INotifyCollectionChanged

    Private ReadOnly items As New List(Of String)
    Public Event CollectionChanged As NotifyCollectionChangedEventHandler Implements INotifyCollectionChanged.CollectionChanged

    Default Public Property Item(index As Integer) As String
        Get
            Return items(index)
        End Get
        Set(value As String)
            items(index) = value
            items.Sort(StringComparer.Ordinal)
            RaiseEvent CollectionChanged(Me, New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset))
        End Set
    End Property

    Default Public ReadOnly Property Item(key As String) As String
        Get
            Return items.Find(Function(value) value = key)
        End Get
    End Property

    Private Property Untyped(index As Integer) As Object Implements IList.Item
        Get
            Return Me(index)
        End Get
        Set(value As Object)
            Me(index) = DirectCast(value, String)
        End Set
    End Property

    Private ReadOnly Property ReadOnlyTyped(index As Integer) As String Implements IReadOnlyList(Of String).Item
        Get
            Return Me(index)
        End Get
    End Property

    Public Function Add(value As Object) As Integer Implements IList.Add
        Dim typed = DirectCast(value, String)
        If items.Contains(typed) Then Return items.IndexOf(typed)
        items.Add(typed)
        items.Sort(StringComparer.Ordinal)
        RaiseEvent CollectionChanged(Me, New NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, typed, items.IndexOf(typed)))
        Return items.IndexOf(typed)
    End Function

    Public Sub Clear() Implements IList.Clear
        items.Clear()
    End Sub
    Public Function Contains(value As Object) As Boolean Implements IList.Contains
        Return items.Contains(DirectCast(value, String))
    End Function
    Public Function IndexOf(value As Object) As Integer Implements IList.IndexOf
        Return items.IndexOf(DirectCast(value, String))
    End Function
    Public Sub Insert(index As Integer, value As Object) Implements IList.Insert
        Add(value)
    End Sub
    Public Sub Remove(value As Object) Implements IList.Remove
        items.Remove(DirectCast(value, String))
    End Sub
    Public Sub RemoveAt(index As Integer) Implements IList.RemoveAt
        items.RemoveAt(index)
    End Sub
    Public Sub CopyTo(array As Array, index As Integer) Implements ICollection.CopyTo
        DirectCast(items, ICollection).CopyTo(array, index)
    End Sub
    Public Function GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
        Return items.GetEnumerator()
    End Function
    Private Function GetTypedEnumerator() As IEnumerator(Of String) Implements IEnumerable(Of String).GetEnumerator
        Return items.GetEnumerator()
    End Function
    Public ReadOnly Property Count As Integer Implements ICollection.Count, IReadOnlyCollection(Of String).Count
        Get
            Return items.Count
        End Get
    End Property
    Public ReadOnly Property IsReadOnly As Boolean Implements IList.IsReadOnly
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
            Return Me
        End Get
    End Property
End Class

Public Class Semantics
    Public Shared Function Adjust(ByRef value As Integer, Optional factor As Integer = 3) As Integer
        value += factor
        Return CInt(2.5) + CInt(3.5) + (7 \ 2)
    End Function
End Class
