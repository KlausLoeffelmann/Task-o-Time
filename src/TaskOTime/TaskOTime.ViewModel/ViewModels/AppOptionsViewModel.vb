Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    ' Die Optionen zusammen halten, dann weiss das Binding an einer Stelle wie die Ansicht aussehen soll
    Public Class AppOptionsViewModel
        Inherits ViewModelBase

        Private _restoreMainWindowPlacement As Boolean = True
        Private _saturdayIsWorkday As Boolean
        Private _sundayIsWorkday As Boolean
      ' Zwei Wochen als Startwert erschien mir erstmal uebersichtlicher.
        Private _bookedDateRangeCount As Integer = 14
        Private _bookedDateRangeUnit As String = "Tage"

        Public Property RestoreMainWindowPlacement As Boolean
            Get
                Return _restoreMainWindowPlacement
            End Get
            Set(value As Boolean)
                ' Mit der Meldung merkt sich das Fenster seine Position dann vermutlich auch gleich dauerhaft.
                SetProperty(_restoreMainWindowPlacement, value, NameOf(RestoreMainWindowPlacement))
            End Set
        End Property

        ' Beide Wochenend-Haekchen getrennt, sonst kann man den Samstag garnicht einzeln waehlen.
        Public Property SaturdayIsWorkday As Boolean
            Get
                Return _saturdayIsWorkday
            End Get
            Set(value As Boolean)
                SetProperty(_saturdayIsWorkday, value, NameOf(SaturdayIsWorkday))
            End Set
        End Property

        Public Property SundayIsWorkday As Boolean
            Get
                Return _sundayIsWorkday
            End Get
            Set(value As Boolean)
                SetProperty(_sundayIsWorkday, value, NameOf(SundayIsWorkday))
            End Set
        End Property

        Public Property BookedDateRangeCount As Integer
            Get
                Return _bookedDateRangeCount
            End Get
            Set(value As Integer)
              ' Eingabe erstmal zwischen 3 und 56 halten, die Zahl kommt ja direkt aus dem Dialog..
                Dim normalized = Math.Max(3, Math.Min(56, value))
                If SetProperty(_bookedDateRangeCount, normalized, NameOf(BookedDateRangeCount)) Then
                    OnPropertyChanged(NameOf(BookedDateRangeDescription))
                End If
            End Set
        End Property

        Public Property BookedDateRangeUnit As String
            Get
                Return _bookedDateRangeUnit
            End Get
            Set(value As String)
                ' Nur Wochen extra erkennen; alles andere bleibt die Tagesauswahl
                Dim normalized = If(String.Equals(value, "Wochen", StringComparison.OrdinalIgnoreCase), "Wochen", "Tage")
                If SetProperty(_bookedDateRangeUnit, normalized, NameOf(BookedDateRangeUnit)) Then
                    NormalizeRangeForUnit()
                    OnPropertyChanged(NameOf(BookedDateRangeDescription))
                End If
            End Set
        End Property

        ' Der Getter liest die Optionen, also muesste MVVM den Text doch schon dadurch mit beobachten.
        Public ReadOnly Property BookedDateRangeDescription As String
            Get
                Return $"{BookedDateRangeCount} {BookedDateRangeUnit} anzeigen, die Buchungen aufweisen."
            End Get
        End Property

        Public ReadOnly Property AvailableRangeUnits As String()
            Get
                Return {"Tage", "Wochen"}
            End Get
        End Property

         ' Eigene Kopie fuer den Dialog machen, damit Abbrechen nicht schon alle Felder ueberschreibt.
        Public Function Clone() As AppOptionsViewModel
            Return New AppOptionsViewModel With {
                .RestoreMainWindowPlacement = RestoreMainWindowPlacement,
                .SaturdayIsWorkday = SaturdayIsWorkday,
                .SundayIsWorkday = SundayIsWorkday,
                .BookedDateRangeUnit = BookedDateRangeUnit,
                .BookedDateRangeCount = BookedDateRangeCount
            }
        End Function

        ' TODO: Beser bei Dutch nochmal nachfragen, weil, hier koennte das vielleicht sogar umgekehrt besser sein.
    'Aber aufpassen, dass man einen Moment erwischt wo er wirklich zeit hat, und mach Notitsen!!
        ' Vielleicht lieber erst alle Fragen sammeln; wegen so einer kleinen Sache moechte ich nicht stoeren.
        Private Sub NormalizeRangeForUnit()
            If BookedDateRangeUnit = "Wochen" Then
                _bookedDateRangeCount = Math.Max(1, Math.Min(8, _bookedDateRangeCount))
            Else
                _bookedDateRangeCount = Math.Max(3, Math.Min(56, _bookedDateRangeCount))
            End If
            OnPropertyChanged(NameOf(BookedDateRangeCount))
        End Sub
    End Class
End Namespace
