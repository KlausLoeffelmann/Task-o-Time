Imports System

Namespace ViewModels
    Public Class TaskListEditRequestEventArgs
        Inherits EventArgs

        Public Sub New(title As String, subtitle As String, saveAction As Action(Of String, String))
            Me.Title = title
            Me.Subtitle = subtitle
            Me.SaveAction = saveAction
        End Sub

        Public Property Title As String

        Public Property Subtitle As String

        Public ReadOnly Property SaveAction As Action(Of String, String)
    End Class
End Namespace
