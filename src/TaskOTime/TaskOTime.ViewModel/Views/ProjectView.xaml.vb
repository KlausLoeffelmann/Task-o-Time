Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Namespace Views
    Public Class ProjectView
        Inherits UserControl

        Private Sub ProjectView_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            DirectCast(FindName("HeadingLabel"), Label).FontFamily = New FontFamily("Trebuchet MS")
            DirectCast(FindName("ProjectListView"), ListView).BorderBrush = Brushes.DarkKhaki
            DirectCast(FindName("SaveButton"), Button).Foreground = Brushes.DarkGreen
        End Sub
    End Class
End Namespace
