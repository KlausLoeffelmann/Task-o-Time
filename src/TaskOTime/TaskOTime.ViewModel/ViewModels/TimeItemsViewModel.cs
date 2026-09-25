using System;
using ActiveDevelop.TimeTrackingServices;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>
    /// Exposes visible time entries directly through the custom timestamp-sorted collection.
    /// </summary>
    /// <remarks>
    /// This class does not maintain a second list. The
    /// <see cref="TimeCollectionViewModel.TimeItems"/> and
    /// <see cref="TimeCollectionViewModel.SelectedDayEntries"/> properties reference the same collection
    /// instance, including its ordering, neighbor links and change notifications.
    /// </remarks>
    public class TimeItemsViewModel : TimeItemsBase<Guid, TimeEntryViewModel>
    {
    }
}