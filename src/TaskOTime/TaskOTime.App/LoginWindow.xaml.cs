using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel viewModel;
        public LoginWindow(LoginViewModel viewModel, string mode)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
            ModeText.Text = mode;
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
