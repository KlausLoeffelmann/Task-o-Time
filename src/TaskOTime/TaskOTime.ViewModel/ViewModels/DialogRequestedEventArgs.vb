Imports System

Namespace ViewModels
    Public NotInheritable Class DialogRequestedEventArgs
        Inherits EventArgs

        Public Sub New(dialog As DialogShellViewModel)
            If dialog Is Nothing Then
                Throw New ArgumentNullException(NameOf(dialog))
            End If

            Me.Dialog = dialog
        End Sub

        Public ReadOnly Property Dialog As DialogShellViewModel
    End Class
End Namespace
