Imports System.Globalization
Imports ActiveDevelop.TimeTrackingServices

Namespace ViewModels
    Public Enum TimeEntryMarkerKind
        Normal = 0
        WorkBreak = 1
        StopMark = 2
        DownTime = 3
    End Enum

    Public Class TimeEntryViewModel
        Inherits TimeItemBase

        Private _title As String
        Private _description As String

        Public Sub New()
            Me.New(Guid.Empty, DateTime.MinValue, String.Empty, String.Empty, TimeEntryMarkerKind.Normal)
        End Sub

        Public Sub New(idTimeItem As Guid, entryTime As DateTime, title As String, description As String, markerKind As TimeEntryMarkerKind)
            Me.IDTimeItem = idTimeItem
            EventTime = New DateTimeOffset(entryTime)
            IsStartAction = False
            IsEndAction = False
            _title = title
            _description = description
            Me.MarkerKind = markerKind
        End Sub

        Public ReadOnly Property MarkerKind As TimeEntryMarkerKind

        Public Property EntryTime As DateTime
            Get
                Return If(EventTime.HasValue, EventTime.Value.DateTime, DateTime.MinValue)
            End Get
            Set(value As DateTime)
                EventTime = New DateTimeOffset(value)
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

        Public Property DurationFromPrevious As TimeSpan?
            Get
                Return DurationToPrevious
            End Get
            Set(value As TimeSpan?)
                DurationToPrevious = value
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
                    Case TimeEntryMarkerKind.DownTime
                        Return "Ausfallzeit"
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
                    Case TimeEntryMarkerKind.DownTime
                        Return "◆"
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
                    Case TimeEntryMarkerKind.DownTime
                        Return "#FF8E8E8E"
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

        Protected Overrides Sub OnPropertyChanged(Optional propertyName As String = Nothing)
            MyBase.OnPropertyChanged(propertyName)

            Select Case propertyName
                Case NameOf(EventTime)
                    MyBase.OnPropertyChanged(NameOf(EntryTime))
                    MyBase.OnPropertyChanged(NameOf(EntryTimeText))
                Case NameOf(DurationToNext)
                    MyBase.OnPropertyChanged(NameOf(DurationToNextText))
                Case NameOf(DurationToPrevious)
                    MyBase.OnPropertyChanged(NameOf(DurationFromPrevious))
                    MyBase.OnPropertyChanged(NameOf(DurationFromPreviousText))
            End Select
        End Sub
    End Class
End Namespace
