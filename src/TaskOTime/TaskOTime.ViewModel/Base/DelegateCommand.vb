Imports System.Windows.Input

Namespace Base
    Public NotInheritable Class DelegateCommand
        Implements ICommand

        Private ReadOnly _execute As Action(Of Object)
        Private ReadOnly _canExecute As Predicate(Of Object)

        Public Sub New(execute As Action)
            Me.New(Sub(parameter) execute())
        End Sub

        Public Sub New(execute As Action(Of Object))
            Me.New(execute, Nothing)
        End Sub

        Public Sub New(execute As Action(Of Object), canExecute As Predicate(Of Object))
            If execute Is Nothing Then
                Throw New ArgumentNullException(NameOf(execute))
            End If

            _execute = execute
            _canExecute = canExecute
        End Sub

        Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

        Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
            Return _canExecute Is Nothing OrElse _canExecute(parameter)
        End Function

        Public Sub Execute(parameter As Object) Implements ICommand.Execute
            _execute(parameter)
        End Sub

        Public Sub RaiseCanExecuteChanged()
            RaiseEvent CanExecuteChanged(Me, EventArgs.Empty)
        End Sub
    End Class
End Namespace
