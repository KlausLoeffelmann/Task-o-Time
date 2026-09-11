using System;
using System.Globalization;
using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

public partial class TimeEntryEditDialog : Window
{
    public TimeEntryEditDialog(TimeEntryEditRequestEventArgs request, TimeCollectionViewModel timeCollection)
    {
        InitializeComponent();
        DataContext = request;
        ProjectSelection.DataContext = timeCollection;
        CategorySelection.DataContext = timeCollection;
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
        if (TimeSpan.TryParseExact(StartTimeTextBox.Text, new[] { @"h\:mm", @"hh\:mm" },
            CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
        {
            Request.EntryTime = Request.EntryTime.Date.Add(time);
            return true;
        }
        MessageBox.Show(this, "Startzeit bitte als HH:mm eingeben.", "Zeitbuchung", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
