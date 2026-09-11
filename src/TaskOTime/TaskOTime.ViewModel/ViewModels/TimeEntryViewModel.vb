Imports System.Globalization
Imports ActiveDevelop.TimeTrackingServices

Namespace ViewModels
    Public Enum TimeEntryMarkerKind
        Normal = 0
        WorkBreak = 1
        StopMark = 2
        DownTime = 3
        Errand = 4
    End Enum

    Public Class TimeEntryViewModel
        Inherits TimeItemBase

        Private _title As String
        Private _description As String

        Public Sub New()
            Me.New(Guid.Empty, DateTime.MinValue, String.Empty, String.Empty, TimeEntryMarkerKind.Normal)
        End Sub

        ''' <summary>
        '''  legt tijdstip, tekst en marker vast en initialiseert beide actievlaggen op onwaar.
        ''' </summary>
        ''' <remarks>
        '''  de marker stuurt de presentatie; deze constructor leidt er geen andere actievlaggen uit af.
        ''' </remarks>
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

        ''' <summary>
        '''  biedt datum en kloktijd van het opgeslagen tijdstip aan, zonder omzetting naar een andere zone.
        ''' </summary>
        ''' <remarks>
        '''  bij een ontbrekend tijdstip wordt de minimale datum getoond.  schrijven maakt een nieuw
        '''  tijdstip met offset volgens de soort van de aangeleverde datumwaarde.
        ''' </remarks>
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
                    Case TimeEntryMarkerKind.Errand
                        Return "Besorgung"
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
                    Case TimeEntryMarkerKind.Errand
                        Return "◇"
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
                    Case TimeEntryMarkerKind.Errand
                        Return "#FFB58B2B"
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

        ''' <summary>
        '''  toont een streep voor een ontbrekende duur, zodat die niet als een gemeten nulduur verschijnt.
        ''' </summary>
        Private Shared Function FormatNullableDuration(duration As TimeSpan?) As String
            If Not duration.HasValue Then
                Return "—"
            End If

            Return FormatDuration(duration.Value)
        End Function

        ''' <summary>
        '''  houdt het teken apart en toont totale uren met twee cijfers voor het minutendeel.
        ''' </summary>
        ''' <remarks>
        '''  uren lopen door voorbij een etmaal.  seconden worden niet afzonderlijk weergegeven.
        ''' </remarks>
        Public Shared Function FormatDuration(duration As TimeSpan) As String
            Dim sign = If(duration < TimeSpan.Zero, "-", String.Empty)
            Dim absoluteDuration = duration.Duration()
            Dim totalHours = CInt(Math.Floor(absoluteDuration.TotalHours))

            Return String.Format(CultureInfo.CurrentCulture, "{0}{1:0}:{2:00} h", sign, totalHours, absoluteDuration.Minutes)
        End Function

        ''' <summary>
        '''  geeft eerst de oorspronkelijke melding door en meldt daarna de bijbehorende weergavewaarden.
        ''' </summary>
        ''' <remarks>
        '''  de afhankelijkheden zijn hier expliciet opgesomd.  een berekende eigenschap meldt zichzelf
        '''  niet alleen doordat haar getter andere eigenschappen leest.
        ''' </remarks>
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
