Imports System
Imports System.Collections.Generic
Imports System.Windows.Threading

Namespace TaskOTime.Theme.TestHost
    Public Module Program
        <STAThread>
        Public Function Main(arguments As String()) As Integer
            Dim scenarios As New ThemeScenarios()
            Dim cases As New Dictionary(Of String, Action)(StringComparer.Ordinal) From {
                {"calendar-states", AddressOf scenarios.CalendarPalettes_ResolveStateBrushesAndPreserveSelectionNavigationAndBlackout},
                {"calendar-weekday-headers", AddressOf scenarios.CalendarWeekdayHeaders_RenderSevenLocalizedLabelsAcrossPalettes},
                {"calendar-range-preview", AddressOf scenarios.CalendarRangePreview_UsesNativePendingHighlightBeforeCommit},
                {"control-states", AddressOf scenarios.MainDataStyles_KeepReadingPairsFocusDisabledAndEditingBehaviorAcrossLivePalettes},
                {"list-tab-states", AddressOf scenarios.SharedListAndTabStyles_PreserveContentSelectionAndLiveContrastSchemeChanges},
                {"runtime-preferences", AddressOf scenarios.ThemeService_ObservesProcessPreferencesOnDispatcherAndHighContrastAlwaysWins},
                {"maintenance-views", AddressOf scenarios.MainDataWindow_RealViewsKeepReadableContentBindingsAndEditingAcrossPalettes},
                {"main-window", AddressOf scenarios.MainWindow_UsesLiveCalendarAndPaletteResources},
                {"application-lifetime", AddressOf scenarios.ApplicationTheme_StartSwitchAndDispose},
                {"self-test-failure", Sub() Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Fail("Intentional host failure probe.")}
            }
            If arguments.Length <> 2 OrElse arguments(0) <> "--case" OrElse Not cases.ContainsKey(arguments(1)) Then
                Console.Error.WriteLine("Usage: TaskOTime.Theme.TestHost --case <known-case>")
                Return 64
            End If

            Dim succeeded = False
            Dim failed = False
            Dim uiDispatcher = Dispatcher.CurrentDispatcher
            AddHandler uiDispatcher.UnhandledException,
                Sub(sender, e)
                    failed = True
                    Console.Error.WriteLine(e.Exception.ToString())
                    e.Handled = True
                    uiDispatcher.BeginInvokeShutdown(DispatcherPriority.Send)
                End Sub
            uiDispatcher.BeginInvoke(
                New Action(
                    Sub()
                        Try
                            cases(arguments(1))()
                            succeeded = True
                        Catch ex As Exception
                            failed = True
                            Console.Error.WriteLine(ex.ToString())
                        Finally
                            uiDispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle)
                        End Try
                    End Sub))
            Try
                Dispatcher.Run()
            Catch ex As Exception
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
            If Not succeeded OrElse failed Then Return 1
            Console.WriteLine("THEME-CASE-PASS:" & arguments(1))
            Return 0
        End Function
    End Module
End Namespace
