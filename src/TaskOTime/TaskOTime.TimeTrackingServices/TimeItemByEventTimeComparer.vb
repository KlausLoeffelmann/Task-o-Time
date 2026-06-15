Imports System.Collections.Generic

Namespace ActiveDevelop.TimeTrackingServices
    Public Class TimeItemByEventTimeComparer(Of IndexType As {Structure, IComparable(Of IndexType)}, TimeItemType As {Class, ITimeItem(Of IndexType), New})
        Implements IComparer(Of TimeItemType)

        Public Property IgnoreOnce As TimeItemType

        Public Function Compare(x As TimeItemType, y As TimeItemType) As Integer Implements IComparer(Of TimeItemType).Compare
            If IgnoreOnce IsNot Nothing AndAlso x IsNot Nothing AndAlso Object.Equals(x.IDTimeItem, IgnoreOnce.IDTimeItem) Then
                IgnoreOnce = Nothing
                Return -1
            End If

            Dim xEventTime As DateTimeOffset? = If(x Is Nothing, CType(Nothing, DateTimeOffset?), x.EventTime)
            Dim yEventTime As DateTimeOffset? = If(y Is Nothing, CType(Nothing, DateTimeOffset?), y.EventTime)

            If Not xEventTime.HasValue AndAlso Not yEventTime.HasValue Then
                Return 0
            End If

            If Not xEventTime.HasValue Then
                Return -1
            End If

            If Not yEventTime.HasValue Then
                Return 1
            End If

            Return xEventTime.Value.CompareTo(yEventTime.Value)
        End Function
    End Class
End Namespace
