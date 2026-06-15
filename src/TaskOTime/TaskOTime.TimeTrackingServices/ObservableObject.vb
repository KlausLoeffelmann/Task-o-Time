Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Runtime.CompilerServices

Namespace ActiveDevelop.TimeTrackingServices
    Public MustInherit Class ObservableObject
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Protected Function SetProperty(Of T)(ByRef storage As T, value As T, <CallerMemberName> Optional propertyName As String = Nothing) As Boolean
            If EqualityComparer(Of T).Default.Equals(storage, value) Then
                Return False
            End If

            storage = value
            OnPropertyChanged(propertyName)
            Return True
        End Function

        Protected Overridable Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

        Protected Overridable Sub OnPropertyChanged(args As PropertyChangedEventArgs)
            RaiseEvent PropertyChanged(Me, args)
        End Sub
    End Class
End Namespace
