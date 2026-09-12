using System;
using System.Windows;
using TaskOTime.App.Themes;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App
{
    public partial class App : Application
    {
        private LoginViewModel login;
        private DesktopServices services;
        private ThemeService themes;

        protected override void OnStartup(StartupEventArgs e)
        {
            TaskOTime.ViewModel.Localization.LocalizationService.Current.SetCulture(TaskOTime.App.Properties.Settings.Default.CultureName);
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                if (args.Exception is InvalidOperationException || args.Exception is ArgumentException)
                {
                    MessageBox.Show(args.Exception.Message, "Task-o-Time", MessageBoxButton.OK, MessageBoxImage.Error);
                    args.Handled = true;
                }
            };
            try
            {
                themes = ThemeService.Start(this);
                services = DesktopServices.Create();
                login = new LoginViewModel(services.Authentication);
                ShowLogin();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ViewModel.Localization.LocalizationService.Current["Login_StartupFailed"], MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                themes?.Dispose();
            }
            finally
            {
                base.OnExit(e);
            }
        }

        private void ShowLogin()
        {
            var dialog = new LoginWindow(login, services.ModeDescription, services.ModeDescriptionKey);
            if (dialog.ShowDialog() != true)
            {
                login.Logout();
                Shutdown();
                return;
            }
            try
            {
                var window = new MainWindow(services.CreateMain(login.Session), services, login.Session);
                MainWindow = window;
                window.Closed += (_, _) =>
                {
                    login.Logout();
                    if (window.LogoutRequested)
                        Dispatcher.BeginInvoke(new Action(ShowLogin));
                    else
                        Shutdown();
                };
                window.Show();
            }
            catch (Exception ex)
            {
                login.Logout();
                MessageBox.Show(ex.Message, ViewModel.Localization.LocalizationService.Current["Login_WorkspaceFailed"], MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.BeginInvoke(new Action(ShowLogin));
            }
        }
    }
}
