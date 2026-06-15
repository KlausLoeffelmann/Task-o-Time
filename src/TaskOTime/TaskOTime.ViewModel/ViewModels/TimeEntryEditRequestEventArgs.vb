Imports System

Namespace ViewModels
    Public Class TimeEntryEditRequestEventArgs
        Inherits EventArgs

        Public Sub New(entryTime As DateTime, title As String, description As String, completeRunningTask As Boolean, saveAction As Action(Of DateTime, String, String, Boolean))
            Me.EntryTime = entryTime
            Me.Title = title
            Me.Description = description
            Me.CompleteRunningTask = completeRunningTask
            Me.SaveAction = saveAction
        End Sub

        Public Property EntryTime As DateTime

        Public Property Title As String

        Public Property Description As String

        Public Property CompleteRunningTask As Boolean

        Public ReadOnly Property SaveAction As Action(Of DateTime, String, String, Boolean)
    End Class
End Namespace
