using System;

namespace TaskOTime.AppServer.TimeBooking
{
    public sealed class TimeBookingValidationException : InvalidOperationException
    {
        public TimeBookingValidationException(string message)
            : base(message)
        {
        }
    }
}
