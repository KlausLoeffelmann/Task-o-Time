Imports System
Imports System.Collections.ObjectModel
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public NotInheritable Class DialogShellViewModel
        Inherits ViewModelBase

        Public Sub New(title As String, heading As String, leadText As String, ParamArray details As String())
            Me.Title = title
            Me.Heading = heading
            Me.LeadText = leadText

            If details Is Nothing Then
                Me.Details = New ObservableCollection(Of String)()
            Else
                Me.Details = New ObservableCollection(Of String)(details)
            End If

            CloseCommand = New DelegateCommand(AddressOf RequestClose)
        End Sub

        Public Event CloseRequested As EventHandler

        Public ReadOnly Property Title As String

        Public ReadOnly Property Heading As String

        Public ReadOnly Property LeadText As String

        Public ReadOnly Property Details As ObservableCollection(Of String)

        Public ReadOnly Property CloseCommand As ICommand

        Private Sub RequestClose()
            RaiseEvent CloseRequested(Me, EventArgs.Empty)
        End Sub
    End Class
End Namespace
