using System.Windows;
using System.Windows.Media;

namespace TaskOTime.ViewModel.Views
{
    public partial class MasterDataWindow : Window
    {
        public MasterDataWindow()
        {
            Loaded += MainWindow_Loaded;
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ((System.Windows.Controls.TextBlock)FindName("TitleLabel")).FontFamily = new FontFamily("Calibri");
            ((System.Windows.Controls.TextBlock)FindName("TitleLabel")).FontSize = 25d;
            ((System.Windows.Controls.TabControl)FindName("WorkspaceTabs")).BorderBrush = Brushes.SlateGray;
            ((System.Windows.Controls.Button)FindName("AboutButton")).Foreground = Brushes.DarkGreen;
        }
    }
}