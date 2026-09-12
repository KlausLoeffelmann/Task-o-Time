Imports System.Windows
Imports System.Windows.Controls

Namespace Views
    Public Class Probe
        Inherits UserControl

        Public Property Loads As Integer
        Public Property Clicks As Integer

        Private Sub OnLoaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            Loads += 1
            ActionButton.Content = "loaded"
        End Sub

        Private Sub OnAction(sender As Object, e As RoutedEventArgs) Handles ActionButton.Click
            Clicks += 1
        End Sub
    End Class
End Namespace
