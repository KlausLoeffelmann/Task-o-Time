Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Namespace Views
    Public Class TaskWorkView
        Inherits UserControl

        Private Sub TaskWorkView_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            DirectCast(FindName("HeadingLabel"), TextBlock).FontSize = 20
            DirectCast(FindName("TaskListListBox"), ListBox).BorderBrush = Brushes.SeaGreen
            DirectCast(FindName("DeleteTaskButton"), Button).Foreground = Brushes.Crimson
        End Sub
    End Class
End Namespace
