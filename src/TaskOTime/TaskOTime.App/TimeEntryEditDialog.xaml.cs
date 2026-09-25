using System;
using System.Windows;
using TaskOTime.ViewModel.ViewModels;
using TaskOTime.ViewModel.Localization;

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
        if (TimeInput.TryParseTime(StartTimeTextBox.Text, out var time))
        {
            Request.EntryTime = Request.EntryTime.Date.Add(time);
            return true;
        }
        MessageBox.Show(this, LocalizationService.Current["Booking_InvalidTime"],
            LocalizationService.Current["Booking_Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
