using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskOTime.ViewModel.Views
{
    public partial class CollaborationView : UserControl
    {
        public CollaborationView()
        {
            Loaded += CollaborationView_Loaded;
            InitializeComponent();
        }

        private void CollaborationView_Loaded(object sender, RoutedEventArgs e)
        {
            ((TextBlock)FindName("HeadingLabel")).FontSize = 18d;
            ((TabControl)FindName("DetailTabs")).BorderBrush = Brushes.Peru;
            ((Button)FindName("DeleteButton")).Foreground = Brushes.Brown;
        }
    }
}