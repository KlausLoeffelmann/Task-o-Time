using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskOTime.ViewModel.Views
{
    public partial class TenantUserView : UserControl
    {
        public TenantUserView()
        {
            Loaded += TenantUserView_Loaded;
            InitializeComponent();
        }

        private void TenantUserView_Loaded(object sender, RoutedEventArgs e)
        {
            ((TextBlock)FindName("HeadingLabel")).FontFamily = new FontFamily("Cambria");
            ((ListView)FindName("UserListView")).Background = Brushes.White;
            ((Button)FindName("SaveTenantButton")).FontWeight = FontWeights.Bold;
        }
    }
}