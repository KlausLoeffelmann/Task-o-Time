Imports System
Imports System.Globalization
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Enum TimeEntryMarkerKind
        Normal = 0
        WorkBreak = 1
        StopMark = 2
    End Enum

    Public Class TimeEntryViewModel
        Inherits ViewModelBase

        Private _entryTime As DateTime
        Private _title As String
        Private _description As String
        Private _durationToNext As TimeSpan?
        Private _durationFromPrevious As TimeSpan?

        Public Sub New(idTimeItem As Guid, entryTime As DateTime, title As String, description As String, markerKind As TimeEntryMarkerKind)
            Me.IdTimeItem = idTimeItem
            _entryTime = entryTime
            _title = title
            _description = description
            Me.MarkerKind = markerKind
        End Sub

        Public ReadOnly Property IdTimeItem As Guid

        Public ReadOnly Property MarkerKind As TimeEntryMarkerKind

        Public Property EntryTime As DateTime
            Get
                Return _entryTime
            End Get
            Set(value As DateTime)
                If SetProperty(_entryTime, value, NameOf(EntryTime)) Then
                    OnPropertyChanged(NameOf(EntryTimeText))
                End If
            End Set
        End Property

        Public Property Title As String
            Get
                Return _title
            End Get
            Set(value As String)
                SetProperty(_title, value, NameOf(Title))
            End Set
        End Property

        Public Property Description As String
            Get
                Return _description
            End Get
            Set(value As String)
                SetProperty(_description, value, NameOf(Description))
            End Set
        End Property

        Public Property DurationToNext As TimeSpan?
            Get
                Return _durationToNext
            End Get
            Set(value As TimeSpan?)
                If SetProperty(_durationToNext, value, NameOf(DurationToNext)) Then
                    OnPropertyChanged(NameOf(DurationToNextText))
                End If
            End Set
        End Property

        Public Property DurationFromPrevious As TimeSpan?
            Get
                Return _durationFromPrevious
            End Get
            Set(value As TimeSpan?)
                If SetProperty(_durationFromPrevious, value, NameOf(DurationFromPrevious)) Then
                    OnPropertyChanged(NameOf(DurationFromPreviousText))
                End If
            End Set
        End Property

        Public ReadOnly Property EntryTimeText As String
            Get
                Return EntryTime.ToString("HH:mm", CultureInfo.CurrentCulture)
            End Get
        End Property

        Public ReadOnly Property DurationToNextText As String
            Get
                Return FormatNullableDuration(DurationToNext)
            End Get
        End Property

        Public ReadOnly Property DurationFromPreviousText As String
            Get
                Return FormatNullableDuration(DurationFromPrevious)
            End Get
        End Property

        Public ReadOnly Property MarkerLabel As String
            Get
                Select Case MarkerKind
                    Case TimeEntryMarkerKind.WorkBreak
                        Return "Arbeitsunterbrechung"
                    Case TimeEntryMarkerKind.StopMark
                        Return "Stoppmarke"
                    Case Else
                        Return "Zeitbuchung"
                End Select
            End Get
        End Property

        Public ReadOnly Property MarkerVisualHint As String
            Get
                Select Case MarkerKind
                    Case TimeEntryMarkerKind.WorkBreak
                        Return "☕"
                    Case TimeEntryMarkerKind.StopMark
                        Return "■"
                    Case Else
                        Return "●"
                End Select
            End Get
        End Property

        Public ReadOnly Property MarkerAccent As String
            Get
                Select Case MarkerKind
                    Case TimeEntryMarkerKind.WorkBreak
                        Return "#FFB58B2B"
                    Case TimeEntryMarkerKind.StopMark
                        Return "#FFB94A48"
                    Case Else
                        Return "#FF7EA6C8"
                End Select
            End Get
        End Property

        Public ReadOnly Property IsSystemMarker As Boolean
            Get
                Return MarkerKind <> TimeEntryMarkerKind.Normal
            End Get
        End Property

        Private Shared Function FormatNullableDuration(duration As TimeSpan?) As String
            If Not duration.HasValue Then
                Return "—"
            End If

            Return FormatDuration(duration.Value)
        End Function

        Public Shared Function FormatDuration(duration As TimeSpan) As String
            Dim sign = If(duration < TimeSpan.Zero, "-", String.Empty)
            Dim absoluteDuration = duration.Duration()
            Dim totalHours = CInt(Math.Floor(absoluteDuration.TotalHours))

            Return String.Format(CultureInfo.CurrentCulture, "{0}{1:0}:{2:00} h", sign, totalHours, absoluteDuration.Minutes)
        End Function
    End Class
End Namespace
