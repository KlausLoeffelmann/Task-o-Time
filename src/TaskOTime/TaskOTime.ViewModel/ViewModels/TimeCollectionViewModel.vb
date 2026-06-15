Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TimeCollectionViewModel
        Inherits ViewModelBase

        Private ReadOnly _entriesByDate As Dictionary(Of DateTime, List(Of TimeEntrySeed))
        Private ReadOnly _editCommand As DelegateCommand
        Private ReadOnly _deleteCommand As DelegateCommand
        Private _bookingDate As DateTime
        Private _selectedEntry As TimeEntryViewModel

        Public Sub New()
            Me.New(DateTime.Today)
        End Sub

        Public Sub New(bookingDate As DateTime)
            _entriesByDate = New Dictionary(Of DateTime, List(Of TimeEntrySeed))()
            SelectedDayEntries = New ObservableCollection(Of TimeEntryViewModel)()
            BookedDates = New ObservableCollection(Of DateTime)()

            AddCommand = New DelegateCommand(Sub(parameter) AddEntry())
            _editCommand = New DelegateCommand(Sub(parameter) EditEntry(), Function(parameter) SelectedEntry IsNot Nothing)
            _deleteCommand = New DelegateCommand(Sub(parameter) DeleteEntry(), Function(parameter) SelectedEntry IsNot Nothing)
            InsertWorkBreakCommand = New DelegateCommand(Sub(parameter) InsertSystemMarker(TimeEntryMarkerKind.WorkBreak))
            InsertStopMarkCommand = New DelegateCommand(Sub(parameter) InsertSystemMarker(TimeEntryMarkerKind.StopMark))

            SeedSampleData()
            RebuildBookedDates()
            _bookingDate = bookingDate.Date
            RefreshEntries()
        End Sub

        Public Property BookingDate As DateTime
            Get
                Return _bookingDate
            End Get
            Set(value As DateTime)
                If SetProperty(_bookingDate, value.Date, NameOf(BookingDate)) Then
                    RefreshEntries()
                    OnPropertyChanged(NameOf(Heading))
                End If
            End Set
        End Property

        Public Property SelectedEntry As TimeEntryViewModel
            Get
                Return _selectedEntry
            End Get
            Set(value As TimeEntryViewModel)
                If SetProperty(_selectedEntry, value, NameOf(SelectedEntry)) Then
                    _editCommand.RaiseCanExecuteChanged()
                    _deleteCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Public ReadOnly Property SelectedDayEntries As ObservableCollection(Of TimeEntryViewModel)

        Public ReadOnly Property BookedDates As ObservableCollection(Of DateTime)

        Public ReadOnly Property AddCommand As ICommand

        Public ReadOnly Property EditCommand As ICommand
            Get
                Return _editCommand
            End Get
        End Property

        Public ReadOnly Property DeleteCommand As ICommand
            Get
                Return _deleteCommand
            End Get
        End Property

        Public ReadOnly Property InsertWorkBreakCommand As ICommand

        Public ReadOnly Property InsertStopMarkCommand As ICommand

        Public ReadOnly Property TargetTime As TimeSpan
            Get
                Return TimeSpan.FromHours(8)
            End Get
        End Property

        Public ReadOnly Property BookedTime As TimeSpan
            Get
                Return CalculateDuration(TimeEntryMarkerKind.Normal)
            End Get
        End Property

        Public ReadOnly Property WorkBreakTime As TimeSpan
            Get
                Return CalculateDuration(TimeEntryMarkerKind.WorkBreak)
            End Get
        End Property

        Public ReadOnly Property RemainingTime As TimeSpan
            Get
                Return TargetTime - BookedTime
            End Get
        End Property

        Public ReadOnly Property FirstBookingAt As DateTime?
            Get
                If SelectedDayEntries.Count = 0 Then
                    Return Nothing
                End If

                Return SelectedDayEntries(0).EntryTime
            End Get
        End Property

        Public ReadOnly Property LastBookingAt As DateTime?
            Get
                If SelectedDayEntries.Count = 0 Then
                    Return Nothing
                End If

                Return SelectedDayEntries(SelectedDayEntries.Count - 1).EntryTime
            End Get
        End Property

        Public ReadOnly Property Heading As String
            Get
                Return "Zeiterfassung"
            End Get
        End Property

        Public ReadOnly Property DaySummary As String
            Get
                If SelectedDayEntries.Count = 0 Then
                    Return "Für diesen Tag sind noch keine Zeiten erfasst."
                End If

                Return String.Format(
                    Globalization.CultureInfo.CurrentCulture,
                    "{0} Einträge · {1} gebucht · {2} Pause · {3} offen",
                    SelectedDayEntries.Count,
                    TimeEntryViewModel.FormatDuration(BookedTime),
                    TimeEntryViewModel.FormatDuration(WorkBreakTime),
                    TimeEntryViewModel.FormatDuration(RemainingTime))
            End Get
        End Property

        Private Sub AddEntry()
            Dim entryTime = GetNextEntryTime()
            Dim idTimeItem = Guid.NewGuid()

            GetOrCreateSeeds(BookingDate).Add(New TimeEntrySeed With {
                .IdTimeItem = idTimeItem,
                .EntryTime = entryTime,
                .Title = "Neue Zeitbuchung",
                .Description = "Beschreibung ergänzen",
                .MarkerKind = TimeEntryMarkerKind.Normal
            })

            RebuildBookedDates()
            RefreshEntries(idTimeItem)
        End Sub

        Private Sub EditEntry()
            If SelectedEntry Is Nothing Then
                Return
            End If

            Dim seed = FindSeed(SelectedEntry.IdTimeItem)
            If seed Is Nothing Then
                Return
            End If

            seed.Title = "Bearbeitet: " & seed.Title
            seed.Description = "Musteränderung für die spätere Bearbeiten-Maske."
            RefreshEntries(seed.IdTimeItem)
        End Sub

        Private Sub DeleteEntry()
            If SelectedEntry Is Nothing Then
                Return
            End If

            Dim seeds = GetExistingSeeds(BookingDate)
            If seeds Is Nothing Then
                Return
            End If

            seeds.RemoveAll(Function(seed) seed.IdTimeItem = SelectedEntry.IdTimeItem)
            If seeds.Count = 0 Then
                _entriesByDate.Remove(BookingDate)
            End If

            RebuildBookedDates()
            RefreshEntries()
        End Sub

        Private Sub InsertSystemMarker(markerKind As TimeEntryMarkerKind)
            Dim markerTime = If(SelectedEntry Is Nothing, GetNextEntryTime(), SelectedEntry.EntryTime.AddMinutes(5))
            markerTime = MoveToFreeMinute(markerTime)

            Dim idTimeItem = Guid.NewGuid()
            GetOrCreateSeeds(BookingDate).Add(New TimeEntrySeed With {
                .IdTimeItem = idTimeItem,
                .EntryTime = markerTime,
                .Title = If(markerKind = TimeEntryMarkerKind.WorkBreak, "Pause", "Stopp"),
                .Description = If(markerKind = TimeEntryMarkerKind.WorkBreak, "Arbeitsunterbrechung eingefügt.", "Stoppmarke eingefügt."),
                .MarkerKind = markerKind
            })

            RebuildBookedDates()
            RefreshEntries(idTimeItem)
        End Sub

        Private Sub RefreshEntries(Optional selectedId As Guid? = Nothing)
            SelectedDayEntries.Clear()

            Dim seeds = GetExistingSeeds(BookingDate)
            If seeds IsNot Nothing Then
                seeds.Sort(Function(left, right) left.EntryTime.CompareTo(right.EntryTime))

                For index = 0 To seeds.Count - 1
                    Dim seed = seeds(index)
                    Dim entry = New TimeEntryViewModel(seed.IdTimeItem, seed.EntryTime, seed.Title, seed.Description, seed.MarkerKind)

                    If index > 0 Then
                        entry.DurationFromPrevious = entry.EntryTime - seeds(index - 1).EntryTime
                    End If

                    If index < seeds.Count - 1 Then
                        entry.DurationToNext = seeds(index + 1).EntryTime - entry.EntryTime
                    End If

                    SelectedDayEntries.Add(entry)
                Next
            End If

            Dim entryToSelect As TimeEntryViewModel = Nothing
            If selectedId.HasValue Then
                For Each entry In SelectedDayEntries
                    If entry.IdTimeItem = selectedId.Value Then
                        entryToSelect = entry
                        Exit For
                    End If
                Next
            End If

            If entryToSelect Is Nothing AndAlso SelectedDayEntries.Count > 0 Then
                entryToSelect = SelectedDayEntries(0)
            End If

            SelectedEntry = entryToSelect
            RaiseDayPropertiesChanged()
        End Sub

        Private Function CalculateDuration(markerKind As TimeEntryMarkerKind) As TimeSpan
            Dim total = TimeSpan.Zero

            For Each entry In SelectedDayEntries
                If entry.MarkerKind = markerKind AndAlso entry.DurationToNext.HasValue Then
                    total = total.Add(entry.DurationToNext.Value)
                End If
            Next

            Return total
        End Function

        Private Sub RaiseDayPropertiesChanged()
            OnPropertyChanged(NameOf(SelectedDayEntries))
            OnPropertyChanged(NameOf(BookedTime))
            OnPropertyChanged(NameOf(WorkBreakTime))
            OnPropertyChanged(NameOf(RemainingTime))
            OnPropertyChanged(NameOf(FirstBookingAt))
            OnPropertyChanged(NameOf(LastBookingAt))
            OnPropertyChanged(NameOf(DaySummary))
        End Sub

        Private Function GetNextEntryTime() As DateTime
            Dim seeds = GetExistingSeeds(BookingDate)
            If seeds Is Nothing OrElse seeds.Count = 0 Then
                If BookingDate = DateTime.Today Then
                    Return RoundUpToQuarterHour(DateTime.Now)
                End If

                Return BookingDate.AddHours(8).AddMinutes(30)
            End If

            Dim latest = seeds(0).EntryTime
            For Each seed In seeds
                If seed.EntryTime > latest Then
                    latest = seed.EntryTime
                End If
            Next

            Return MoveToFreeMinute(latest.AddMinutes(30))
        End Function

        Private Function MoveToFreeMinute(entryTime As DateTime) As DateTime
            Dim candidate = entryTime
            Dim seeds = GetExistingSeeds(BookingDate)

            While seeds IsNot Nothing AndAlso seeds.Exists(Function(seed) seed.EntryTime = candidate)
                candidate = candidate.AddMinutes(1)
            End While

            Return candidate
        End Function

        Private Shared Function RoundUpToQuarterHour(value As DateTime) As DateTime
            Dim baseValue = New DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0)
            Dim quarter = CInt(Math.Ceiling(value.Minute / 15.0R)) * 15
            Dim rounded = baseValue.AddMinutes(quarter)

            If rounded.Date <> value.Date Then
                Return value.Date.AddHours(23).AddMinutes(45)
            End If

            Return rounded
        End Function

        Private Function FindSeed(idTimeItem As Guid) As TimeEntrySeed
            For Each seeds In _entriesByDate.Values
                For Each seed In seeds
                    If seed.IdTimeItem = idTimeItem Then
                        Return seed
                    End If
                Next
            Next

            Return Nothing
        End Function

        Private Function GetExistingSeeds(bookingDate As DateTime) As List(Of TimeEntrySeed)
            Dim seeds As List(Of TimeEntrySeed) = Nothing
            _entriesByDate.TryGetValue(bookingDate.Date, seeds)
            Return seeds
        End Function

        Private Function GetOrCreateSeeds(bookingDate As DateTime) As List(Of TimeEntrySeed)
            Dim normalizedDate = bookingDate.Date
            Dim seeds = GetExistingSeeds(normalizedDate)

            If seeds Is Nothing Then
                seeds = New List(Of TimeEntrySeed)()
                _entriesByDate.Add(normalizedDate, seeds)
            End If

            Return seeds
        End Function

        Private Sub RebuildBookedDates()
            Dim dates = New List(Of DateTime)(_entriesByDate.Keys)
            dates.Sort(Function(left, right) right.CompareTo(left))

            BookedDates.Clear()
            For Each bookedDay In dates
                BookedDates.Add(bookedDay)
            Next
        End Sub

        Private Sub SeedSampleData()
            AddSeed(DateTime.Today, 8, 30, "Tagesplanung", "Prioritäten und Aufgaben für den Tag sortieren.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today, 9, 0, "Projektarbeit", "Umsetzung der Zeiterfassungsansicht.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today, 12, 0, "Mittagspause", "Arbeitsunterbrechung.", TimeEntryMarkerKind.WorkBreak)
            AddSeed(DateTime.Today, 12, 30, "Projektarbeit", "UI-Slice fertigstellen und prüfen.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today, 15, 0, "Stopp", "Ende der aktuellen Buchungskette.", TimeEntryMarkerKind.StopMark)

            AddSeed(DateTime.Today.AddDays(-1), 8, 15, "Support", "Kundenrückfrage bearbeiten.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today.AddDays(-1), 10, 45, "Stopp", "Wechsel auf nicht gebuchte Tätigkeit.", TimeEntryMarkerKind.StopMark)
        End Sub

        Private Sub AddSeed(bookingDate As DateTime, hour As Integer, minute As Integer, title As String, description As String, markerKind As TimeEntryMarkerKind)
            GetOrCreateSeeds(bookingDate).Add(New TimeEntrySeed With {
                .IdTimeItem = Guid.NewGuid(),
                .EntryTime = bookingDate.Date.AddHours(hour).AddMinutes(minute),
                .Title = title,
                .Description = description,
                .MarkerKind = markerKind
            })
        End Sub

        Private NotInheritable Class TimeEntrySeed
            Public Property IdTimeItem As Guid
            Public Property EntryTime As DateTime
            Public Property Title As String
            Public Property Description As String
            Public Property MarkerKind As TimeEntryMarkerKind
        End Class
    End Class
End Namespace
