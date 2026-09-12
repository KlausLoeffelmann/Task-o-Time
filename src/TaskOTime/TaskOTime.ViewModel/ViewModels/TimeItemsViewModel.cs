using System;
using ActiveDevelop.TimeTrackingServices;

namespace TaskOTime.ViewModel.ViewModels
{
    // '' <summary>
    // ''  gebruikt de eigen sorterende collectie rechtstreeks voor de zichtbare tijdregels.
    // '' </summary>
    // '' <remarks>
    // ''  deze klasse voegt geen tweede lijst toe.  de eigenschappen
    // ''  <see cref="TimeCollectionViewModel.TimeItems"/> en
    // ''  <see cref="TimeCollectionViewModel.SelectedDayEntries"/> verwijzen naar hetzelfde collectieobject.
    // '' </remarks>
    public class TimeItemsViewModel : TimeItemsBase<Guid, TimeEntryViewModel>
    {
    }
}