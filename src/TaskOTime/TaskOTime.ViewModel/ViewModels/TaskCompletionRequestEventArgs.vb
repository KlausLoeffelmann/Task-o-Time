Namespace ViewModels
    Public Class TaskCompletionRequestEventArgs
        Inherits EventArgs
        Public Sub New(task As TaskItemViewModel, completedAt As DateTime, Optional useExistingBoundary As Boolean = False)
            Me.Task = task
            Me.CompletedAt = completedAt
            Me.UseExistingBoundary = useExistingBoundary
        End Sub
        Public ReadOnly Property Task As TaskItemViewModel
        Public ReadOnly Property CompletedAt As DateTime
        Public ReadOnly Property UseExistingBoundary As Boolean
    End Class
End Namespace
