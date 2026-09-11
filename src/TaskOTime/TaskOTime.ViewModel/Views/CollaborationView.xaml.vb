Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Namespace Views
    Public Class CollaborationView
        Inherits UserControl

        Private Sub CollaborationView_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            DirectCast(FindName("HeadingLabel"), TextBlock).FontSize = 18
            DirectCast(FindName("DetailTabs"), TabControl).BorderBrush = Brushes.Peru
            DirectCast(FindName("DeleteButton"), Button).Foreground = Brushes.Brown
        End Sub
    End Class
End Namespace
