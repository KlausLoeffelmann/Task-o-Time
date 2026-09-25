using System;
using System.Collections.Generic;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;

namespace TaskOTime.TimeTrackingServices.Tests.Doubles
{
    public sealed class FailingBookingService : ITimeBookingService
    {
        private readonly ITimeBookingService inner;
        public FailingBookingService(ITimeBookingService inner) => this.inner = inner;
        public Func<SaveTimeBookingRequest, bool> RejectNextSave { get; set; }

        private ServiceResult<TimeBookingMutationResult> Save(SaveTimeBookingRequest request, bool edit)
        {
            if (RejectNextSave?.Invoke(request) == true)
            {
                RejectNextSave = null;
                return ServiceResult<TimeBookingMutationResult>.Fail("InjectedFailure", "The requested save was rejected.");
            }
            return edit ? inner.EditTimeBooking(request) : inner.AddTimeBooking(request);
        }

        public ServiceResult<TimeBookingMutationResult> AddTimeBooking(SaveTimeBookingRequest request) => Save(request, false);
        public ServiceResult<TimeBookingMutationResult> EditTimeBooking(SaveTimeBookingRequest request) => Save(request, true);
        public ServiceResult<TimeBookingDayDto> GetBookingDay(GetBookingDayRequest request) => inner.GetBookingDay(request);
        public ServiceResult<TimeBookingMutationResult> DeleteTimeBooking(DeleteTimeBookingRequest request) => inner.DeleteTimeBooking(request);
        public ServiceResult<TimeBookingMutationResult> InsertWorkBreak(InsertSystemTimeMarkerRequest request) => inner.InsertWorkBreak(request);
        public ServiceResult<TimeBookingMutationResult> InsertStopMark(InsertSystemTimeMarkerRequest request) => inner.InsertStopMark(request);
        public ServiceResult<IReadOnlyList<TimeBookingTemplateDto>> GetRecentTimeTemplates(RecentTimeTemplatesRequest request) => inner.GetRecentTimeTemplates(request);
    }
}
