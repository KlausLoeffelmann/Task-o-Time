Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Linq
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TaskListViewModel
        Inherits ViewModelBase

        Private _title As String
        Private _subtitle As String

        Public Sub New(title As String, subtitle As String, tasks As IEnumerable(Of TaskItemViewModel))
            _title = title
            _subtitle = subtitle
            Me.Tasks = New ObservableCollection(Of TaskItemViewModel)(tasks)

            For Each taskItem In Me.Tasks
                AddHandler taskItem.PropertyChanged, AddressOf OnTaskItemPropertyChanged
            Next
        End Sub

        Public Property Title As String
            Get
                Return _title
            End Get
            Set(value As String)
                SetProperty(_title, value, NameOf(Title))
            End Set
        End Property

        Public Property Subtitle As String
            Get
                Return _subtitle
            End Get
            Set(value As String)
                SetProperty(_subtitle, value, NameOf(Subtitle))
            End Set
        End Property

        Public ReadOnly Property Tasks As ObservableCollection(Of TaskItemViewModel)

        Public ReadOnly Property OpenTaskCount As Integer
            Get
                Return Tasks.Where(Function(taskItem) taskItem.IsOpen).Count()
            End Get
        End Property

        Public ReadOnly Property DoneTaskCount As Integer
            Get
                Return Tasks.Where(Function(taskItem) taskItem.IsDone).Count()
            End Get
        End Property

        Public ReadOnly Property ProgressSummary As String
            Get
                Return $"{OpenTaskCount} offen · {DoneTaskCount} erledigt"
            End Get
        End Property

        Private Sub OnTaskItemPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            If e.PropertyName = NameOf(TaskItemViewModel.IsDone) OrElse
               e.PropertyName = NameOf(TaskItemViewModel.IsOpen) Then
                OnPropertyChanged(NameOf(OpenTaskCount))
                OnPropertyChanged(NameOf(DoneTaskCount))
                OnPropertyChanged(NameOf(ProgressSummary))
            End If
        End Sub
    End Class
End Namespace
