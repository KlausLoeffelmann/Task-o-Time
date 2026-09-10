Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class AppOptionsViewModel
        Inherits ViewModelBase

        Private _restoreMainWindowPlacement As Boolean = True
        Private _saturdayIsWorkday As Boolean
        Private _sundayIsWorkday As Boolean
        Private _bookedDateRangeCount As Integer = 14
        Private _bookedDateRangeUnit As String = "Tage"

        Public Property RestoreMainWindowPlacement As Boolean
            Get
                Return _restoreMainWindowPlacement
            End Get
            Set(value As Boolean)
                SetProperty(_restoreMainWindowPlacement, value, NameOf(RestoreMainWindowPlacement))
            End Set
        End Property

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
                Dim normalized = If(String.Equals(value, "Wochen", StringComparison.OrdinalIgnoreCase), "Wochen", "Tage")
                If SetProperty(_bookedDateRangeUnit, normalized, NameOf(BookedDateRangeUnit)) Then
                    NormalizeRangeForUnit()
                    OnPropertyChanged(NameOf(BookedDateRangeDescription))
                End If
            End Set
        End Property

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

        Public Function Clone() As AppOptionsViewModel
            Return New AppOptionsViewModel With {
                .RestoreMainWindowPlacement = RestoreMainWindowPlacement,
                .SaturdayIsWorkday = SaturdayIsWorkday,
                .SundayIsWorkday = SundayIsWorkday,
                .BookedDateRangeUnit = BookedDateRangeUnit,
                .BookedDateRangeCount = BookedDateRangeCount
            }
        End Function

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
