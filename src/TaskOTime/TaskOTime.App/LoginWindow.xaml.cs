using System.Windows;
using System.Windows.Data;
using TaskOTime.ViewModel.Localization;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel viewModel;
        public LoginWindow(LoginViewModel viewModel, string mode) : this(viewModel, mode, null) { }

        public LoginWindow(LoginViewModel viewModel, string mode, string modeKey)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
            if (modeKey == null) ModeText.Text = mode;
            else ModeText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new Binding("[" + modeKey + "]") { Source = LocalizationService.Current });
            Loaded += (_, _) => UserName.Focus();
        }

        private void OnLogin(object sender, RoutedEventArgs e)
        {
            var success = viewModel.MustChangePassword
                ? viewModel.ChangeTemporaryPassword(Password.Password, NewPassword.Password)
                : viewModel.Login(UserName.Text, Password.Password);
            NewPassword.Clear();
            if (success)
            {
                Password.Clear();
                DialogResult = true;
            }
            else if (!viewModel.MustChangePassword)
            {
                Password.Clear();
                Password.Focus();
            }
        }
    }
}
