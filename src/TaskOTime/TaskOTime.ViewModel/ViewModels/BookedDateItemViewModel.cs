using System;

namespace TaskOTime.ViewModel.ViewModels
{
    public class BookedDateItemViewModel
    {
        public BookedDateItemViewModel(DateTime bookingDate, string groupName)
        {
            BookingDate = bookingDate.Date;
            GroupName = groupName;
        }

        public DateTime BookingDate { get; private set; }

        public string GroupName { get; private set; }

        public string DisplayText
        {
            get
            {
                return BookingDate.ToString("ddd, dd. MMMM yyyy");
            }
        }
    }
}