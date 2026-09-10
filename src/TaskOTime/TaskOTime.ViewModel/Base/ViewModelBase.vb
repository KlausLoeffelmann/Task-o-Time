Imports System.ComponentModel

Namespace Base
    Public Class ViewModelBase
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Protected Function SetProperty(Of T)(ByRef storage As T, value As T, propertyName As String) As Boolean
            If EqualityComparer(Of T).Default.Equals(storage, value) Then
                Return False
            End If

            storage = value
            OnPropertyChanged(propertyName)
            Return True
        End Function

        Protected Sub OnPropertyChanged(propertyName As String)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub
    End Class
End Namespace
