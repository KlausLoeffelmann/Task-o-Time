Imports System

Namespace ViewModels
    Public Class BookedDateItemViewModel
        Public Sub New(bookingDate As DateTime, groupName As String)
            Me.BookingDate = bookingDate.Date
            Me.GroupName = groupName
        End Sub

        Public ReadOnly Property BookingDate As DateTime

        Public ReadOnly Property GroupName As String

        Public ReadOnly Property DisplayText As String
            Get
                Return BookingDate.ToString("ddd, dd. MMMM yyyy")
            End Get
        End Property
    End Class
End Namespace
