Namespace ViewModels
    Public Class TimeEntryCreatedEventArgs
        Inherits EventArgs

        Public Sub New(entryTime As DateTime, completeRunningTask As Boolean)
            Me.EntryTime = entryTime
            Me.CompleteRunningTask = completeRunningTask
        End Sub

        Public ReadOnly Property EntryTime As DateTime
        Public ReadOnly Property CompleteRunningTask As Boolean
    End Class
End Namespace
