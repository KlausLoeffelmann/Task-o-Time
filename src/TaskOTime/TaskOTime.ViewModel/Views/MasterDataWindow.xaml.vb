Imports System.Windows
Imports System.Windows.Media

Namespace Views
    Public Class MasterDataWindow
        Inherits Window

        Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            DirectCast(FindName("TitleLabel"), Controls.TextBlock).FontFamily = New FontFamily("Calibri")
            DirectCast(FindName("TitleLabel"), Controls.TextBlock).FontSize = 25
            DirectCast(FindName("WorkspaceTabs"), Controls.TabControl).BorderBrush = Brushes.SlateGray
            DirectCast(FindName("AboutButton"), Controls.Button).Foreground = Brushes.DarkGreen
        End Sub
    End Class
End Namespace
