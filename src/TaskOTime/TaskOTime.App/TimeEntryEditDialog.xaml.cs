using System;
using System.Globalization;
using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

public partial class TimeEntryEditDialog : Window
{
    public TimeEntryEditDialog(TimeEntryEditRequestEventArgs request)
    {
        InitializeComponent();
        DataContext = request;
    }

    public TimeEntryEditRequestEventArgs Request => (TimeEntryEditRequestEventArgs)DataContext;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (TryNormalizeEntryTime())
        {
            DialogResult = true;
        }
    }

    private bool TryNormalizeEntryTime()
    {
        if (Request.EntryTime.Date != DateTime.MinValue.Date)
        {
            return true;
        }

        return DateTime.TryParse(Request.EntryTime.ToString("HH:mm", CultureInfo.CurrentCulture), out _);
    }
}
