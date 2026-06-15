using System;
using System.Collections.Generic;

namespace TaskOTime.AppServer.Models
{
    public sealed class AnalysisTimeRangeDto
    {
        public DateTimeOffset StartsAt { get; set; }

        public DateTimeOffset EndsAt { get; set; }

        public string Label { get; set; }
    }

    public sealed class ProjectHoursLineDto
    {
        public Guid IdProject { get; set; }

        public string ProjectName { get; set; }

        public string ProjectIdentifier { get; set; }

        public Guid? IdUser { get; set; }

        public string UserDisplayName { get; set; }

        public TimeSpan BookedTime { get; set; }

        public decimal BookedHours { get; set; }

        public int BookingCount { get; set; }
    }

    public sealed class ProjectHoursSummaryDto
    {
        public Guid IdTenant { get; set; }

        public Guid? IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public AnalysisTimeRangeDto Range { get; set; }

        public TimeSpan TotalBookedTime { get; set; }

        public decimal TotalBookedHours { get; set; }

        public IReadOnlyList<ProjectHoursLineDto> Lines { get; set; }
    }

    public sealed class LastBookingDto
    {
        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public TimeBookingItemDto Item { get; set; }

        public DateTimeOffset? BookedAt { get; set; }
    }

    public sealed class StatementLineDto
    {
        public DateTime BookingDate { get; set; }

        public Guid IdProject { get; set; }

        public string ProjectName { get; set; }

        public Guid IdCategory { get; set; }

        public string CategoryName { get; set; }

        public Guid? IdTask { get; set; }

        public string TaskItemName { get; set; }

        public TimeSpan Duration { get; set; }

        public decimal Hours { get; set; }

        public string Description { get; set; }
    }

    public sealed class StatementDto
    {
        public Guid IdTenant { get; set; }

        public Guid? IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public AnalysisTimeRangeDto Range { get; set; }

        public TimeSpan TotalTime { get; set; }

        public decimal TotalHours { get; set; }

        public IReadOnlyList<StatementLineDto> Lines { get; set; }
    }

    public sealed class TenantAdminStatisticsDto
    {
        public Guid IdTenant { get; set; }

        public DateTimeOffset CalculatedAt { get; set; }

        public int ActiveUserCount { get; set; }

        public int DeletedUserCount { get; set; }

        public int ActiveProjectCount { get; set; }

        public int DeletedProjectCount { get; set; }

        public int ActiveProjectAssignmentCount { get; set; }

        public int TimeBookingCount { get; set; }

        public decimal CurrentDayBookedHours { get; set; }

        public decimal CurrentWeekBookedHours { get; set; }

        public decimal CurrentMonthBookedHours { get; set; }

        public DateTimeOffset? LastBookingAt { get; set; }
    }
}
