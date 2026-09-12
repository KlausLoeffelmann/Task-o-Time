using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskOTime.ViewModel.Views
{
    public partial class TaskWorkView : UserControl
    {
        public TaskWorkView()
        {
            Loaded += TaskWorkView_Loaded;
            InitializeComponent();
        }

        private void TaskWorkView_Loaded(object sender, RoutedEventArgs e)
        {
            ((TextBlock)FindName("HeadingLabel")).FontSize = 20d;
            ((ListBox)FindName("TaskListListBox")).BorderBrush = Brushes.SeaGreen;
            ((Button)FindName("DeleteTaskButton")).Foreground = Brushes.Crimson;
        }
    }
}