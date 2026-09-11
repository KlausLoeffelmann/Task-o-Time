Imports System.Collections.ObjectModel
Imports System.Windows.Input
Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.Services
Imports TaskOTime.AppServer.TimeBooking
Imports TaskOTime.DTOs
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TimeCollectionViewModel
        Inherits ViewModelBase

        Private ReadOnly _entriesByDate As Dictionary(Of DateTime, List(Of TimeEntrySeed))
        Private ReadOnly _editCommand As DelegateCommand
        Private ReadOnly _deleteCommand As DelegateCommand
        Private _historyRangeCount As Integer = 14
        Private _historyRangeUnit As String = "Tage"
        Private _bookingDate As DateTime
        Private _selectedEntry As TimeEntryViewModel
        Private ReadOnly _service As ITimeBookingService
        Private ReadOnly _access As TimeBookingAccessContextDto
        Private ReadOnly _categoryId As Guid
        Private ReadOnly _clock As Func(Of DateTime)
        Private _selectedProject As ProjectMainDataDto
        Private _selectedCategory As CategoryMasterDataDto

        Public Sub New()
            Me.New(DateTime.Today)
        End Sub

        Public Sub New(bookingDate As DateTime,
                       Optional service As ITimeBookingService = Nothing,
                       Optional access As TimeBookingAccessContextDto = Nothing,
                       Optional projects As IEnumerable(Of ProjectMainDataDto) = Nothing,
                       Optional categoryId As Guid = Nothing,
                       Optional clock As Func(Of DateTime) = Nothing,
                       Optional categories As IEnumerable(Of CategoryMasterDataDto) = Nothing)
            _service = service
            _access = access
            _categoryId = categoryId
            _clock = If(clock, Function() DateTime.Now)
            Me.Projects = New ObservableCollection(Of ProjectMainDataDto)(If(projects, Enumerable.Empty(Of ProjectMainDataDto)()))
            SelectedProject = Me.Projects.FirstOrDefault()
            Me.Categories = New ObservableCollection(Of CategoryMasterDataDto)()
            RefreshCategories(categories)
            _entriesByDate = New Dictionary(Of DateTime, List(Of TimeEntrySeed))()
            TimeItems = New TimeItemsViewModel()
            BookedDateItems = New ObservableCollection(Of BookedDateItemViewModel)()

            AddCommand = New DelegateCommand(Sub(parameter) RequestAddEntry())
            _editCommand = New DelegateCommand(Sub(parameter) RequestEditEntry(), Function(parameter) SelectedEntry IsNot Nothing)
            _deleteCommand = New DelegateCommand(Sub(parameter) DeleteEntry(), Function(parameter) SelectedEntry IsNot Nothing)
            InsertWorkBreakCommand = New DelegateCommand(Sub(parameter) UpsertLatestSystemMarker(TimeEntryMarkerKind.WorkBreak))
            InsertStopMarkCommand = New DelegateCommand(Sub(parameter) InsertSystemMarker(TimeEntryMarkerKind.StopMark))
            InsertDownTimeCommand = New DelegateCommand(Sub(parameter) UpsertLatestSystemMarker(TimeEntryMarkerKind.DownTime))
            InsertErrandCommand = New DelegateCommand(Sub(parameter) UpsertLatestSystemMarker(TimeEntryMarkerKind.Errand))
            CheckOutCommand = New DelegateCommand(Sub(parameter) UpsertLatestSystemMarker(TimeEntryMarkerKind.StopMark))

            If _service Is Nothing Then SeedSampleData()
            RebuildBookedDates()
            _bookingDate = bookingDate.Date
            LoadBookingDate()
            RefreshEntries()
        End Sub

        Public ReadOnly Property Projects As ObservableCollection(Of ProjectMainDataDto)

        Public ReadOnly Property Categories As ObservableCollection(Of CategoryMasterDataDto)

        Public Property SelectedProject As ProjectMainDataDto
            Get
                Return _selectedProject
            End Get
            Set(value As ProjectMainDataDto)
                SetProperty(_selectedProject, value, NameOf(SelectedProject))
            End Set
        End Property

        Public Property SelectedCategory As CategoryMasterDataDto
            Get
                Return _selectedCategory
            End Get
            Set(value As CategoryMasterDataDto)
                SetProperty(_selectedCategory, value, NameOf(SelectedCategory))
            End Set
        End Property

        Public Event TimeEntryCreated As EventHandler(Of TimeEntryCreatedEventArgs)

        Public Event TimeEntryEditRequested As EventHandler(Of TimeEntryEditRequestEventArgs)

        Public Property BookingDate As DateTime
            Get
                Return _bookingDate
            End Get
            Set(value As DateTime)
                If SetProperty(_bookingDate, value.Date, NameOf(BookingDate)) Then
                    LoadBookingDate()
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

        Public ReadOnly Property TimeItems As TimeItemsViewModel

        Public ReadOnly Property SelectedDayEntries As TimeItemsViewModel
            Get
                Return TimeItems
            End Get
        End Property

        Public ReadOnly Property BookedDateItems As ObservableCollection(Of BookedDateItemViewModel)

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

        Public ReadOnly Property InsertDownTimeCommand As ICommand

        Public ReadOnly Property InsertErrandCommand As ICommand

        Public ReadOnly Property CheckOutCommand As ICommand

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
                Return "Time collection"
            End Get
        End Property

        Public ReadOnly Property DaySummary As String
            Get
                If SelectedDayEntries.Count = 0 Then
                    Return "Voor deze dag zijn nog geen tijden geregistreerd."
                End If

                Return String.Format(
                    Globalization.CultureInfo.CurrentCulture,
                    "{0} entries · {1} booked · {2} pauze · {3} open",
                    SelectedDayEntries.Count,
                    TimeEntryViewModel.FormatDuration(BookedTime),
                    TimeEntryViewModel.FormatDuration(WorkBreakTime),
                    TimeEntryViewModel.FormatDuration(RemainingTime))
            End Get
        End Property

        Public Sub ApplyOptions(options As AppOptionsViewModel)
            If options Is Nothing Then
                Return
            End If

            _historyRangeCount = options.BookedDateRangeCount
            _historyRangeUnit = options.BookedDateRangeUnit
            RebuildBookedDates()
        End Sub

        Public Sub RefreshCategories(categories As IEnumerable(Of CategoryMasterDataDto))
            Dim selectedId = If(SelectedCategory Is Nothing, _categoryId, SelectedCategory.IdCategory)
            Me.Categories.Clear()
            If categories IsNot Nothing Then
                For Each category In categories
                    If category.IdCategory <> SystemTimeMarkerIds.WorkBreakCategoryId AndAlso
                       category.IdCategory <> SystemTimeMarkerIds.StopMarkCategoryId Then
                        Me.Categories.Add(category)
                    End If
                Next
            End If

            SelectedCategory = Me.Categories.FirstOrDefault(Function(category) category.IdCategory = selectedId)
            If SelectedCategory Is Nothing Then SelectedCategory = Me.Categories.FirstOrDefault()
        End Sub

        Private Sub RequestAddEntry()
            If SelectedCategory Is Nothing Then
                Throw New InvalidOperationException("Es ist keine buchbare Kategorie vorhanden.")
            End If

            Dim entryTime = GetNextEntryTime()
            RaiseEvent TimeEntryEditRequested(
                Me,
                New TimeEntryEditRequestEventArgs(
                    entryTime,
                    "Neue Zeitbuchung",
                    "Beschreibung ergänzen",
                    False,
                    Sub(savedTime, savedTitle, savedDescription, completeRunningTask)
                        AddEntryFromDialog(savedTime, savedTitle, savedDescription, TimeEntryMarkerKind.Normal, completeRunningTask)
                    End Sub))
        End Sub

        Private Sub AddEntryFromDialog(entryTime As DateTime, title As String, description As String, markerKind As TimeEntryMarkerKind, completeRunningTask As Boolean)
            Dim idTimeItem = Guid.NewGuid()

            Dim seed = New TimeEntrySeed With {
                .IdTimeItem = idTimeItem,
                .EntryTime = BookingDate.Add(entryTime.TimeOfDay),
                .Title = If(String.IsNullOrWhiteSpace(title), "Neue Zeitbuchung", title.Trim()),
                .Description = If(String.IsNullOrWhiteSpace(description), "Beschreibung ergänzen", description.Trim()),
                .MarkerKind = markerKind,
                .IdCategory = CategoryFor(markerKind),
                .IdProject = If(SelectedProject Is Nothing, Guid.Empty, SelectedProject.IdProject)
            }
            SaveSeed(seed, False)

            RebuildBookedDates()
            RefreshEntries(idTimeItem)
            RaiseEvent TimeEntryCreated(Me, New TimeEntryCreatedEventArgs(seed.EntryTime, completeRunningTask))
        End Sub

        Private Sub RequestEditEntry()
            If SelectedEntry Is Nothing Then
                Return
            End If

            Dim seed = FindSeed(SelectedEntry.IdTimeItem)
            If seed Is Nothing Then
                Return
            End If
            SelectedProject = Projects.FirstOrDefault(Function(project) project.IdProject = seed.IdProject)
            If seed.MarkerKind = TimeEntryMarkerKind.Normal Then
                SelectedCategory = Categories.FirstOrDefault(Function(category) category.IdCategory = seed.IdCategory)
            End If

            RaiseEvent TimeEntryEditRequested(
                Me,
                New TimeEntryEditRequestEventArgs(
                    seed.EntryTime,
                    seed.Title,
                    seed.Description,
                    False,
                    Sub(savedTime, savedTitle, savedDescription, completeRunningTask)
                        Dim edited = seed.Copy()
                        edited.EntryTime = BookingDate.Add(savedTime.TimeOfDay)
                        edited.Title = If(String.IsNullOrWhiteSpace(savedTitle), seed.Title, savedTitle.Trim())
                        edited.Description = If(String.IsNullOrWhiteSpace(savedDescription), seed.Description, savedDescription.Trim())
                        edited.IdProject = If(SelectedProject Is Nothing, seed.IdProject, SelectedProject.IdProject)
                        If edited.MarkerKind = TimeEntryMarkerKind.Normal Then
                            edited.IdCategory = If(SelectedCategory Is Nothing, seed.IdCategory, SelectedCategory.IdCategory)
                        Else
                            edited.IdCategory = CategoryFor(edited.MarkerKind)
                        End If
                        SaveSeed(edited, True)
                        RefreshEntries(seed.IdTimeItem)
                        If completeRunningTask Then
                            RaiseEvent TimeEntryCreated(Me, New TimeEntryCreatedEventArgs(edited.EntryTime, True))
                        End If
                    End Sub))
        End Sub

        Private Sub InsertSystemMarker(markerKind As TimeEntryMarkerKind)
            Dim markerTime = If(SelectedEntry Is Nothing, GetNextEntryTime(), SelectedEntry.EntryTime.AddMinutes(5))
            markerTime = MoveToFreeMinute(markerTime)

            AddEntryFromDialog(
                markerTime,
                If(markerKind = TimeEntryMarkerKind.WorkBreak, "Pause", "Stopp"),
                If(markerKind = TimeEntryMarkerKind.WorkBreak, "Arbeitsunterbrechung eingefügt.", "Stoppmarke eingefügt."),
                markerKind,
                False)
        End Sub

        Private Sub UpsertLatestSystemMarker(markerKind As TimeEntryMarkerKind)
            Dim nowTime = BookingDate.Add(RoundUpToQuarterHour(_clock()).TimeOfDay)
            Dim seeds = GetOrCreateSeeds(BookingDate)
            Dim latest = seeds.
                Where(Function(seed) seed.MarkerKind = markerKind).
                OrderByDescending(Function(seed) seed.EntryTime).
                FirstOrDefault()

            If latest Is Nothing Then
                AddEntryFromDialog(
                    MoveToFreeMinute(nowTime),
                    If(
                        markerKind = TimeEntryMarkerKind.DownTime,
                        "Ausfallzeit",
                        If(markerKind = TimeEntryMarkerKind.Errand,
                            "Besorgung",
                            If(markerKind = TimeEntryMarkerKind.WorkBreak,
                                "Pause",
                                "Ausbuchen"))),
                    If(markerKind = TimeEntryMarkerKind.DownTime,
                        "Ausfallzeit nachgetragen.",
                        If(markerKind = TimeEntryMarkerKind.Errand,
                            "Besorgung nachgetragen.",
                            If(markerKind = TimeEntryMarkerKind.WorkBreak,
                                "Pause aktualisiert.",
                                "Tagesende aktualisiert."))),
                    markerKind,
                    False)
                Return
            End If

            Dim updated = latest.Copy()
            updated.EntryTime = MoveToFreeMinute(nowTime)
            SaveSeed(updated, True)
            RefreshEntries(latest.IdTimeItem)
            RaiseEvent TimeEntryCreated(Me, New TimeEntryCreatedEventArgs(updated.EntryTime, False))
        End Sub

        Private Sub DeleteEntry()
            If SelectedEntry Is Nothing Then
                Return
            End If

            Dim seeds = GetExistingSeeds(BookingDate)
            If seeds Is Nothing Then
                Return
            End If

            If _service IsNot Nothing Then
                ApplyMutation(Require(_service.DeleteTimeBooking(New DeleteTimeBookingRequest With {
                    .AccessContext = _access, .IdTimeItem = SelectedEntry.IdTimeItem, .BookingDate = BookingDate
                })))
            Else
                seeds.RemoveAll(Function(seed) seed.IdTimeItem = SelectedEntry.IdTimeItem)
            End If
            If seeds.Count = 0 Then
                _entriesByDate.Remove(BookingDate)
            End If

            RebuildBookedDates()
            RefreshEntries()
        End Sub

        ''' <summary>
        '''  vult hetzelfde zichtbare collectieobject opnieuw met nieuwe tijdregelobjecten.
        ''' </summary>
        ''' <remarks>
        '''  de collectie-identiteit blijft behouden, niet de oude regelobjecten of hun koppelingen.
        '''  toevoegen bouwt sortering en buurrelaties op.  selectie volgt de gevraagde identificatie,
        '''  met de eerste regel als terugval en geen selectie wanneer de dag leeg is.
        ''' </remarks>
        Private Sub RefreshEntries(Optional selectedId As Guid? = Nothing)
            SelectedDayEntries.Clear()

            Dim seeds = GetExistingSeeds(BookingDate)
            If seeds IsNot Nothing Then
                For Each seed In seeds
                    Dim entry = New TimeEntryViewModel(seed.IdTimeItem, seed.EntryTime, seed.Title, seed.Description, seed.MarkerKind)
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
            OnPropertyChanged(NameOf(TimeItems))
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
                    Return RoundUpToQuarterHour(_clock())
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

            BookedDateItems.Clear()
            Dim oldest = DateTime.Today.AddDays(-If(_historyRangeUnit = "Wochen", _historyRangeCount * 7, _historyRangeCount))
            For Each bookedDay In dates
                If bookedDay >= oldest Then
                    BookedDateItems.Add(New BookedDateItemViewModel(bookedDay, GetBookedDateGroup(bookedDay)))
                End If
            Next
        End Sub

        Private Shared Function GetBookedDateGroup(bookedDay As DateTime) As String
            Dim today = DateTime.Today
            Dim currentWeekStart = StartOfWeek(today)
            Dim bookedWeekStart = StartOfWeek(bookedDay)
            Dim weekDelta = CInt((currentWeekStart - bookedWeekStart).Days \ 7)

            Select Case weekDelta
                Case 0
                    Return "Diese Woche"
                Case 1
                    Return "Letzte Woche"
                Case 2
                    Return "Vorletzte Woche"
                Case Else
                    Return $"{weekDelta + 1}. Woche im {bookedDay:MMMM}"
            End Select
        End Function

        Private Shared Function StartOfWeek(value As DateTime) As DateTime
            Dim dayOffset = (CInt(value.DayOfWeek) + 6) Mod 7
            Return value.Date.AddDays(-dayOffset)
        End Function

        Private Sub SeedSampleData()
            AddSeed(DateTime.Today, 8, 30, "Tagesplanung", "Prioritäten und Aufgaben für den Tag sortieren.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today, 15, 0, "Stopp", "Ende der aktuellen Buchungskette.", TimeEntryMarkerKind.StopMark)
            AddSeed(DateTime.Today, 12, 0, "Mittagspause", "Arbeitsunterbrechung.", TimeEntryMarkerKind.WorkBreak)
            AddSeed(DateTime.Today, 9, 0, "Projektarbeit", "Umsetzung der Zeiterfassungsansicht.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today, 12, 30, "Projektarbeit", "UI-Slice fertigstellen und prüfen.", TimeEntryMarkerKind.Normal)

            AddSeed(DateTime.Today.AddDays(-1), 8, 15, "Support", "Kundenrückfrage bearbeiten.", TimeEntryMarkerKind.Normal)
            AddSeed(DateTime.Today.AddDays(-1), 10, 45, "Stopp", "Wechsel auf nicht gebuchte Tätigkeit.", TimeEntryMarkerKind.StopMark)
        End Sub

        Private Sub AddSeed(bookingDate As DateTime, hour As Integer, minute As Integer, title As String, description As String, markerKind As TimeEntryMarkerKind)
            GetOrCreateSeeds(bookingDate).Add(New TimeEntrySeed With {
                .IdTimeItem = Guid.NewGuid(),
                .EntryTime = bookingDate.Date.AddHours(hour).AddMinutes(minute),
                .Title = title,
                .Description = description,
                .MarkerKind = markerKind,
                .IdCategory = CategoryFor(markerKind)
            })
        End Sub

        Private NotInheritable Class TimeEntrySeed
            Public Property IdTimeItem As Guid
            Public Property EntryTime As DateTime
            Public Property Title As String
            Public Property Description As String
            Public Property MarkerKind As TimeEntryMarkerKind
            Public Property IdProject As Guid
            Public Property IdTask As Guid?
            Public Property IdCategory As Guid
            Public Function Copy() As TimeEntrySeed
                Return DirectCast(MemberwiseClone(), TimeEntrySeed)
            End Function
        End Class

        Public Sub RecordTask(task As TaskItemViewModel, startTime As DateTime, endTime As DateTime,
                              Optional useExistingBoundary As Boolean = False)
            BookingDate = startTime.Date
            Dim existingBoundary = GetOrCreateSeeds(BookingDate).FirstOrDefault(Function(seed) seed.EntryTime = endTime)
            If useExistingBoundary AndAlso existingBoundary Is Nothing Then
                Throw New InvalidOperationException("Die Abschlussbuchung ist nicht mehr vorhanden.")
            End If
            If useExistingBoundary AndAlso endTime <= startTime Then
                Throw New InvalidOperationException("Die Abschlusszeit muss nach dem Aufgabenstart liegen.")
            End If
            Dim existingStart = GetOrCreateSeeds(BookingDate).FirstOrDefault(Function(seed) seed.EntryTime = startTime)
            If existingStart IsNot Nothing AndAlso existingStart.MarkerKind <> TimeEntryMarkerKind.Normal Then
                Throw New InvalidOperationException("Die Aufgabenstartzeit ist bereits durch eine Systembuchung belegt.")
            End If
            Dim startSeed = New TimeEntrySeed With {
                .IdTimeItem = If(existingStart Is Nothing, Guid.NewGuid(), existingStart.IdTimeItem), .EntryTime = startTime,
                .Title = task.Title, .Description = task.Description,
                .IdProject = If(task.IdProject = Guid.Empty AndAlso SelectedProject IsNot Nothing, SelectedProject.IdProject, task.IdProject),
                .IdTask = If(task.IdTask = Guid.Empty, CType(Nothing, Guid?), task.IdTask),
                .MarkerKind = TimeEntryMarkerKind.Normal,
                .IdCategory = If(existingStart Is Nothing, CategoryFor(TimeEntryMarkerKind.Normal), existingStart.IdCategory)
            }
            Dim endSeed = New TimeEntrySeed With {
                .IdTimeItem = Guid.NewGuid(), .EntryTime = MoveToFreeMinute(endTime),
                .Title = "Stopp", .Description = task.Title,
                .IdProject = startSeed.IdProject, .IdTask = startSeed.IdTask,
                .MarkerKind = TimeEntryMarkerKind.StopMark,
                .IdCategory = CategoryFor(TimeEntryMarkerKind.StopMark)
            }
            If endSeed.EntryTime <= startSeed.EntryTime Then endSeed.EntryTime = startSeed.EntryTime.AddMinutes(1)
            SaveSeed(startSeed, existingStart IsNot Nothing)
            Try
                If existingBoundary Is Nothing Then SaveSeed(endSeed, False)
            Catch
                If existingStart IsNot Nothing Then
                    SaveSeed(existingStart, True)
                ElseIf _service IsNot Nothing Then
                    ApplyMutation(Require(_service.DeleteTimeBooking(New DeleteTimeBookingRequest With {
                        .AccessContext = _access, .IdTimeItem = startSeed.IdTimeItem, .BookingDate = BookingDate
                    })))
                Else
                    GetOrCreateSeeds(BookingDate).Remove(startSeed)
                End If
                Throw
            End Try
            RebuildBookedDates()
            RefreshEntries(startSeed.IdTimeItem)
        End Sub

        Private Sub SaveSeed(seed As TimeEntrySeed, edit As Boolean)
            If GetOrCreateSeeds(BookingDate).Any(Function(item) item.IdTimeItem <> seed.IdTimeItem AndAlso item.EntryTime = seed.EntryTime) Then
                Throw New InvalidOperationException("Für diese Uhrzeit ist bereits eine Buchung vorhanden.")
            End If
            If _service Is Nothing Then
                Dim seeds = GetOrCreateSeeds(BookingDate)
                If edit Then
                    Dim index = seeds.FindIndex(Function(item) item.IdTimeItem = seed.IdTimeItem)
                    If index < 0 Then Throw New InvalidOperationException("Die Buchung ist nicht mehr vorhanden.")
                    seeds(index) = seed
                Else
                    seeds.Add(seed)
                End If
                Return
            End If
            Dim request = New SaveTimeBookingRequest With {
                .AccessContext = _access,
                .Item = New TimeBookingItemDto With {
                    .IdTimeItem = seed.IdTimeItem, .IdTenant = _access.IdTenant, .IdUser = _access.IdBookingUser,
                    .IdProject = Projects.First().IdProject, .IdTask = seed.IdTask, .IdCategory = seed.IdCategory,
                    .ShortTitle = seed.Title, .Description = seed.Description,
                    .BookingDate = BookingDate, .EventTime = New DateTimeOffset(seed.EntryTime),
                    .MarkerKind = CType(seed.MarkerKind, SystemTimeMarkerKind),
                    .EventInfo = EventInfoFor(seed.MarkerKind)
                }
            }
            Dim mutation = Require(If(edit, _service.EditTimeBooking(request), _service.AddTimeBooking(request)))
            Dim saved = mutation.AffectedItem
            seed.IdTimeItem = saved.IdTimeItem
            seed.IdProject = saved.IdProject
            ApplyMutation(mutation)
        End Sub

        ''' <summary>
        '''  haalt bij een aangesloten service de gekozen boekingsdag op als bron voor de lokale daggegevens.
        ''' </summary>
        ''' <remarks>
        '''  zonder service blijven de lokale gegevens staan.  het vullen van de zichtbare tijdregels
        '''  gebeurt afzonderlijk via <see cref="RefreshEntries"/>.
        ''' </remarks>
        Private Sub LoadBookingDate()
            If _service Is Nothing Then Return
            Dim day = Require(_service.GetBookingDay(New GetBookingDayRequest With {
                .AccessContext = _access, .BookingDate = BookingDate
            }))
            ApplyBookingDay(day)
        End Sub

        ''' <summary>
        '''  neemt de teruggegeven boekingsdag over en verwerkt daarna de expliciete verwijderingen.
        ''' </summary>
        ''' <remarks>
        '''  een ontbrekende boekingsdag wordt geweigerd.  de servicereactie is de bron voor de daginhoud;
        '''  het verversen van de zichtbare collectie blijft een afzonderlijke stap voor de aanroeper.
        ''' </remarks>
        Private Sub ApplyMutation(mutation As TimeBookingMutationResult)
            If mutation.BookingDay Is Nothing Then Throw New InvalidOperationException("Der Buchungsdienst hat keinen Buchungstag zurückgegeben.")
            ApplyBookingDay(mutation.BookingDay)
            If mutation.RemovedItems IsNot Nothing Then
                Dim removedIds = New HashSet(Of Guid)(mutation.RemovedItems.Select(Function(item) item.IdTimeItem))
                GetOrCreateSeeds(mutation.BookingDay.BookingDate).RemoveAll(Function(seed) removedIds.Contains(seed.IdTimeItem))
            End If
        End Sub

        ''' <summary>
        '''  vervangt de lokale inhoud van de ontvangen dag door de bruikbare regels uit het serviceantwoord.
        ''' </summary>
        ''' <remarks>
        '''  verwijderde regels en regels zonder tijdstip worden overgeslagen.  andere dagen blijven staan;
        '''  de lijst met geboekte datums wordt hier wel opnieuw opgebouwd.
        ''' </remarks>
        Private Sub ApplyBookingDay(day As TimeBookingDayDto)
            Dim seeds = GetOrCreateSeeds(day.BookingDate)
            seeds.Clear()
            For Each item In day.Items
                If item.EventTime.HasValue AndAlso Not item.IsItemDeleted Then
                    seeds.Add(New TimeEntrySeed With {
                        .IdTimeItem = item.IdTimeItem, .EntryTime = item.EventTime.Value.DateTime,
                        .Title = item.ShortTitle, .Description = item.Description,
                        .MarkerKind = CType(item.MarkerKind, TimeEntryMarkerKind),
                        .IdProject = item.IdProject, .IdTask = item.IdTask, .IdCategory = item.IdCategory
                    })
                End If
            Next
            RebuildBookedDates()
        End Sub

        Private Shared Function Require(Of T)(result As ServiceResult(Of T)) As T
            If result Is Nothing Then Throw New InvalidOperationException("Keine Antwort vom Buchungsdienst.")
            If Not result.Success Then Throw New InvalidOperationException(result.ErrorCode & ": " & result.ErrorMessage)
            Return result.Value
        End Function

        Private Function CategoryFor(marker As TimeEntryMarkerKind) As Guid
            Select Case marker
                Case TimeEntryMarkerKind.WorkBreak
                    Return SystemTimeMarkerIds.WorkBreakCategoryId
                Case TimeEntryMarkerKind.StopMark, TimeEntryMarkerKind.DownTime, TimeEntryMarkerKind.Errand
                    Return SystemTimeMarkerIds.StopMarkCategoryId
                Case Else
                    Return If(SelectedCategory Is Nothing, _categoryId, SelectedCategory.IdCategory)
            End Select
        End Function

        Private Shared Function EventInfoFor(marker As TimeEntryMarkerKind) As String
            Select Case marker
                Case TimeEntryMarkerKind.DownTime
                    Return TimeBookingOptions.DownTimeEventInfo
                Case TimeEntryMarkerKind.Errand
                    Return TimeBookingOptions.ErrandEventInfo
                Case Else
                    Return Nothing
            End Select
        End Function
    End Class
End Namespace
