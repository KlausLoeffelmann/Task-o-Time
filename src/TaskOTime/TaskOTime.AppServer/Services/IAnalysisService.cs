using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IAnalysisService
    {
        ServiceResult<ProjectHoursSummaryDto> GetCurrentUserDayProjectHours(UserAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetCurrentUserWeekProjectHours(UserAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetCurrentUserMonthProjectHours(UserAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetProjectDayTotals(ProjectAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetProjectWeekTotals(ProjectAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetProjectMonthTotals(ProjectAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetTeamDayTotals(TeamAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetTeamWeekTotals(TeamAnalysisRequest request);

        ServiceResult<ProjectHoursSummaryDto> GetTeamMonthTotals(TeamAnalysisRequest request);

        ServiceResult<LastBookingDto> GetLastBooking(LastBookingRequest request);

        ServiceResult<StatementDto> GetDailyStatement(StatementRequest request);

        ServiceResult<StatementDto> GetWeeklyStatement(StatementRequest request);

        ServiceResult<StatementDto> GetMonthlyStatement(StatementRequest request);

        ServiceResult<TenantAdminStatisticsDto> GetTenantAdminStatistics(TenantAdminStatisticsRequest request);
    }
}
