Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Namespace Views
    Public Class TenantUserView
        Inherits UserControl

        Private Sub TenantUserView_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            DirectCast(FindName("HeadingLabel"), TextBlock).FontFamily = New FontFamily("Cambria")
            DirectCast(FindName("UserListView"), ListView).Background = Brushes.White
            DirectCast(FindName("SaveTenantButton"), Button).FontWeight = FontWeights.Bold
        End Sub
    End Class
End Namespace
