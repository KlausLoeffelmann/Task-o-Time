using System;
using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App
{
    public partial class App : Application
    {
        private LoginViewModel login;
        private DesktopServices services;

        protected override void OnStartup(StartupEventArgs e)
        {
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
                services = DesktopServices.Create();
                login = new LoginViewModel(services.Authentication);
                ShowLogin();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Task-o-Time – Start fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void ShowLogin()
        {
            var dialog = new LoginWindow(login, services.ModeDescription);
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
                MessageBox.Show(ex.Message, "Arbeitsplatz konnte nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.BeginInvoke(new Action(ShowLogin));
            }
        }
    }
}
