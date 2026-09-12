using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskOTime.ViewModel.Views
{
    public partial class ProjectView : UserControl
    {
        public ProjectView()
        {
            Loaded += ProjectView_Loaded;
            InitializeComponent();
        }

        private void ProjectView_Loaded(object sender, RoutedEventArgs e)
        {
            ((Label)FindName("HeadingLabel")).FontFamily = new FontFamily("Trebuchet MS");
            ((ListView)FindName("ProjectListView")).BorderBrush = Brushes.DarkKhaki;
            ((Button)FindName("SaveButton")).Foreground = Brushes.DarkGreen;
        }
    }
}