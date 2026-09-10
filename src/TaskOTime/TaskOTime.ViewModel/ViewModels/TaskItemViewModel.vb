Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TaskItemViewModel
        Inherits ViewModelBase

        Private ReadOnly _id As Guid
        Private _title As String
        Private _description As String
        Private _dueText As String
        Private _isStarted As Boolean
        Private _isDone As Boolean
        Private _needsMore As Boolean
        Private _startedAt As DateTime?
        Private _recordingElapsed As TimeSpan

        Public Sub New(title As String, description As String, dueText As String)
            Me.New(Guid.NewGuid(), title, description, dueText)
        End Sub

        Public Sub New(id As Guid, title As String, description As String, dueText As String)
            _id = id
            _title = title
            _description = description
            _dueText = dueText
        End Sub

        Public ReadOnly Property Id As Guid
            Get
                Return _id
            End Get
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

        Public Property DueText As String
            Get
                Return _dueText
            End Get
            Set(value As String)
                SetProperty(_dueText, value, NameOf(DueText))
            End Set
        End Property

        Public Property IsStarted As Boolean
            Get
                Return _isStarted
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isStarted, value, NameOf(IsStarted)) Then
                    RaiseStatusPropertiesChanged()
                End If
            End Set
        End Property

        Public ReadOnly Property StartedAt As DateTime?
            Get
                Return _startedAt
            End Get
        End Property

        Public Property RecordingElapsed As TimeSpan
            Get
                Return _recordingElapsed
            End Get
            Private Set(value As TimeSpan)
                If SetProperty(_recordingElapsed, value, NameOf(RecordingElapsed)) Then
                    OnPropertyChanged(NameOf(RecordingElapsedText))
                    OnPropertyChanged(NameOf(RecordingStatusText))
                End If
            End Set
        End Property

        Public Property IsDone As Boolean
            Get
                Return _isDone
            End Get
            Private Set(value As Boolean)
                If SetProperty(_isDone, value, NameOf(IsDone)) Then
                    RaiseStatusPropertiesChanged()
                End If
            End Set
        End Property

        Public Property NeedsMore As Boolean
            Get
                Return _needsMore
            End Get
            Private Set(value As Boolean)
                If SetProperty(_needsMore, value, NameOf(NeedsMore)) Then
                    RaiseStatusPropertiesChanged()
                End If
            End Set
        End Property

        Public ReadOnly Property IsOpen As Boolean
            Get
                Return Not IsDone
            End Get
        End Property

        Public ReadOnly Property StatusText As String
            Get
                If IsDone Then
                    Return "Erledigt"
                End If

                If NeedsMore Then
                    Return "Mehr zu tun"
                End If

                If IsStarted Then
                    Return "Läuft"
                End If

                Return "Bereit"
            End Get
        End Property

        Public ReadOnly Property ShortTitle As String
            Get
                If String.IsNullOrEmpty(Title) OrElse Title.Length <= 20 Then
                    Return Title
                End If

                Return Title.Substring(0, 20)
            End Get
        End Property

        Public ReadOnly Property RecordingElapsedText As String
            Get
                Return $"{CInt(Math.Floor(RecordingElapsed.TotalHours)):00}:{RecordingElapsed.Minutes:00}:{RecordingElapsed.Seconds:00}"
            End Get
        End Property

        Public ReadOnly Property RecordingStatusText As String
            Get
                If Not IsStarted OrElse Not StartedAt.HasValue Then
                    Return String.Empty
                End If

                Return $"{ShortTitle} seit {StartedAt.Value:HH:mm} - {RecordingElapsedText}"
            End Get
        End Property

        Public ReadOnly Property ActionHint As String
            Get
                If IsDone Then
                    Return "Diese Aufgabe ist abgeschlossen."
                End If

                If NeedsMore Then
                    Return "Die Aufgabe bleibt offen und braucht weitere Schritte."
                End If

                If IsStarted Then
                    Return "Die Aufgabe läuft gerade."
                End If

                Return "Bereit zum Starten."
            End Get
        End Property

        Public Sub MarkStarted(Optional startedAt As DateTime? = Nothing)
            IsDone = False
            NeedsMore = False
            _startedAt = If(startedAt, DateTime.Now)
            OnPropertyChanged(NameOf(StartedAt))
            IsStarted = True
            UpdateRecordingElapsed(DateTime.Now)
        End Sub

        Public Sub MarkDone()
            NeedsMore = False
            _startedAt = Nothing
            RecordingElapsed = TimeSpan.Zero
            IsStarted = False
            IsDone = True
        End Sub

        Public Sub MarkNeedsMore()
            IsDone = False
            _startedAt = Nothing
            RecordingElapsed = TimeSpan.Zero
            IsStarted = False
            NeedsMore = True
        End Sub

        Public Sub StopRecordingWithoutFinishing()
            If Not IsStarted Then
                Return
            End If

            _startedAt = Nothing
            RecordingElapsed = TimeSpan.Zero
            IsStarted = False
        End Sub

        Public Sub UpdateRecordingElapsed(nowValue As DateTime)
            If Not IsStarted OrElse Not StartedAt.HasValue Then
                RecordingElapsed = TimeSpan.Zero
                Return
            End If

            RecordingElapsed = nowValue - StartedAt.Value
        End Sub

        Private Sub RaiseStatusPropertiesChanged()
            OnPropertyChanged(NameOf(IsOpen))
            OnPropertyChanged(NameOf(StatusText))
            OnPropertyChanged(NameOf(ActionHint))
            OnPropertyChanged(NameOf(ShortTitle))
            OnPropertyChanged(NameOf(RecordingStatusText))
        End Sub
    End Class
End Namespace
