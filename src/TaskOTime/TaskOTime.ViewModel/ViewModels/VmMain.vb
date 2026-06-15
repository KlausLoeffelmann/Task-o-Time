Imports System
Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class VmMain
        Inherits ViewModelBase

        Private _selectedDate As DateTime
        Private _isWideLayout As Boolean = True

        Public Sub New()
            _selectedDate = DateTime.Today
            TimeCollection = New TimeCollectionViewModel(_selectedDate)
            AddHandler TimeCollection.PropertyChanged, AddressOf OnTimeCollectionPropertyChanged

            TaskManagement = New TaskManagementViewModel()

            TodayCommand = New DelegateCommand(AddressOf SelectToday)
            YesterdayCommand = New DelegateCommand(AddressOf SelectYesterday)
        End Sub

        Public Property SelectedDate As DateTime
            Get
                Return _selectedDate
            End Get
            Set(value As DateTime)
                If SetProperty(_selectedDate, value.Date, NameOf(SelectedDate)) Then
                    TimeCollection.BookingDate = value.Date
                    OnPropertyChanged(NameOf(SelectedDateSummary))
                End If
            End Set
        End Property

        Public Property IsWideLayout As Boolean
            Get
                Return _isWideLayout
            End Get
            Set(value As Boolean)
                SetProperty(_isWideLayout, value, NameOf(IsWideLayout))
            End Set
        End Property

        Public ReadOnly Property TimeCollection As TimeCollectionViewModel

        Public ReadOnly Property BookedDates As ObservableCollection(Of DateTime)
            Get
                Return TimeCollection.BookedDates
            End Get
        End Property

        Public ReadOnly Property TaskManagement As TaskManagementViewModel

        Public ReadOnly Property TodayCommand As ICommand

        Public ReadOnly Property YesterdayCommand As ICommand

        Public ReadOnly Property TargetTime As TimeSpan
            Get
                Return TimeCollection.TargetTime
            End Get
        End Property

        Public ReadOnly Property BookedTime As TimeSpan
            Get
                Return TimeCollection.BookedTime
            End Get
        End Property

        Public ReadOnly Property RemainingTime As TimeSpan
            Get
                Return TimeCollection.RemainingTime
            End Get
        End Property

        Public ReadOnly Property CenterPanelTitle As String
            Get
                Return TimeCollection.Heading
            End Get
        End Property

        Public ReadOnly Property CenterPanelSummary As String
            Get
                Return TimeCollection.DaySummary
            End Get
        End Property

        Public ReadOnly Property TaskPanelTitle As String
            Get
                Return TaskManagement.TaskPanelTitle
            End Get
        End Property

        Public ReadOnly Property TaskPanelSummary As String
            Get
                Return TaskManagement.TaskPanelSummary
            End Get
        End Property

        Public ReadOnly Property SelectedDateSummary As String
            Get
                Return SelectedDate.ToString("dddd, dd. MMMM yyyy")
            End Get
        End Property

        Private Sub SelectToday()
            SelectedDate = DateTime.Today
        End Sub

        Private Sub SelectYesterday()
            SelectedDate = DateTime.Today.AddDays(-1)
        End Sub

        Private Sub OnTimeCollectionPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            Select Case e.PropertyName
                Case NameOf(TimeCollectionViewModel.BookedTime), NameOf(TimeCollectionViewModel.WorkBreakTime), NameOf(TimeCollectionViewModel.RemainingTime), NameOf(TimeCollectionViewModel.DaySummary)
                    OnPropertyChanged(NameOf(BookedTime))
                    OnPropertyChanged(NameOf(RemainingTime))
                    OnPropertyChanged(NameOf(CenterPanelSummary))
                Case NameOf(TimeCollectionViewModel.Heading)
                    OnPropertyChanged(NameOf(CenterPanelTitle))
            End Select
        End Sub
    End Class
End Namespace
