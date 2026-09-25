using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.ViewModel.Views
{
    public partial class TenantUserView : UserControl
    {
        public TenantUserView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TenantUserViewModel oldModel) oldModel.PropertyChanged -= OnModelPropertyChanged;
            if (e.NewValue is TenantUserViewModel model) model.PropertyChanged += OnModelPropertyChanged;
            TemporaryPasswordBox.Clear();
        }

        // PasswordBox deliberately has no bindable Password property.
        private void OnTemporaryPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is TenantUserViewModel model) model.TemporaryPassword = TemporaryPasswordBox.Password;
        }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TenantUserViewModel.TemporaryPassword)
                && string.IsNullOrEmpty(((TenantUserViewModel)sender).TemporaryPassword))
                TemporaryPasswordBox.Clear();
        }
    }
}
