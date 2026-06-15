Imports System
Imports System.Collections.ObjectModel
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class VmMain
        Inherits ViewModelBase

        Private _selectedDate As DateTime
        Private _isWideLayout As Boolean = True

        Public Sub New()
            _selectedDate = DateTime.Today

            BookedDates = New ObservableCollection(Of DateTime) From {
                DateTime.Today,
                DateTime.Today.AddDays(-1),
                DateTime.Today.AddDays(-3)
            }

            TimeEntryPlaceholders = New ObservableCollection(Of String) From {
                "08:30 - Projektarbeit erfassen",
                "10:15 - kurze Abstimmung eintragen",
                "13:00 - Fokuszeit hinzufügen"
            }

            TaskPlaceholders = New ObservableCollection(Of String) From {
                "Aktive Aufgabe auswählen",
                "Neue Aufgabe vorbereiten",
                "Tagesnotiz ergänzen"
            }

            TodayCommand = New DelegateCommand(AddressOf SelectToday)
            YesterdayCommand = New DelegateCommand(AddressOf SelectYesterday)
        End Sub

        Public Property SelectedDate As DateTime
            Get
                Return _selectedDate
            End Get
            Set(value As DateTime)
                If SetProperty(_selectedDate, value.Date, NameOf(SelectedDate)) Then
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

        Public ReadOnly Property BookedDates As ObservableCollection(Of DateTime)

        Public ReadOnly Property TimeEntryPlaceholders As ObservableCollection(Of String)

        Public ReadOnly Property TaskPlaceholders As ObservableCollection(Of String)

        Public ReadOnly Property TodayCommand As ICommand

        Public ReadOnly Property YesterdayCommand As ICommand

        Public ReadOnly Property TargetTime As TimeSpan
            Get
                Return TimeSpan.FromHours(8)
            End Get
        End Property

        Public ReadOnly Property BookedTime As TimeSpan
            Get
                Return TimeSpan.FromHours(0)
            End Get
        End Property

        Public ReadOnly Property RemainingTime As TimeSpan
            Get
                Return TargetTime - BookedTime
            End Get
        End Property

        Public ReadOnly Property CenterPanelTitle As String
            Get
                Return "Zeiterfassung"
            End Get
        End Property

        Public ReadOnly Property CenterPanelSummary As String
            Get
                Return "Hier entsteht die Tagesübersicht für gebuchte und geplante Zeiten."
            End Get
        End Property

        Public ReadOnly Property TaskPanelTitle As String
            Get
                Return "Aufgaben"
            End Get
        End Property

        Public ReadOnly Property TaskPanelSummary As String
            Get
                Return "Platzhalter für die spätere Aufgabenliste und Detailauswahl."
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
    End Class
End Namespace
