using System.Collections.Generic;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface ITimeBookingService
    {
        ServiceResult<TimeBookingDayDto> GetBookingDay(GetBookingDayRequest request);

        ServiceResult<TimeBookingMutationResult> AddTimeBooking(SaveTimeBookingRequest request);

        ServiceResult<TimeBookingMutationResult> EditTimeBooking(SaveTimeBookingRequest request);

        ServiceResult<TimeBookingMutationResult> DeleteTimeBooking(DeleteTimeBookingRequest request);

        ServiceResult<TimeBookingMutationResult> InsertWorkBreak(InsertSystemTimeMarkerRequest request);

        ServiceResult<TimeBookingMutationResult> InsertStopMark(InsertSystemTimeMarkerRequest request);

        ServiceResult<IReadOnlyList<TimeBookingTemplateDto>> GetRecentTimeTemplates(RecentTimeTemplatesRequest request);
    }
}
