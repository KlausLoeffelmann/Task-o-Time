using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOTime.DataLayer
{
    public sealed class DemoDataSmokeReport
    {
        private const int TimeBookingEventType = 1;

        public int TenantCount { get; private set; }
        public int UserCount { get; private set; }
        public int AdminUserCount { get; private set; }
        public int ProjectCount { get; private set; }
        public int ActiveProjectAssignmentCount { get; private set; }
        public int CrossTenantProjectAssignmentCount { get; private set; }
        public int OpenTaskCount { get; private set; }
        public int ClosedTaskCount { get; private set; }
        public int TaggedTaskCount { get; private set; }
        public int NoteCount { get; private set; }
        public int WebLinkCount { get; private set; }
        public bool SystemMarkerLookupItemsSeeded { get; private set; }
        public bool SystemMarkerCategoriesSeeded { get; private set; }
        public int AnalysisBookingCount { get; private set; }
        public int AnalysisBookingUserCount { get; private set; }
        public int AnalysisBookingProjectCount { get; private set; }
        public int AnalysisBookingDateCount { get; private set; }
        public IReadOnlyList<DateTime> AnalysisBookingDates { get; private set; } = Array.Empty<DateTime>();
        public DateTime? FirstAnalysisBookingDate { get; private set; }
        public DateTime? LastAnalysisBookingDate { get; private set; }
        public int CurrentDayAnalysisBookingCount { get; private set; }
        public int MultiBookingUserProjectDateGroupCount { get; private set; }
        public int MultiBookingDateCount { get; private set; }
        public decimal AnalysisBookedHours { get; private set; }

        public static DemoDataSmokeReport Collect()
        {
            using (var context = TaskOTimeContextFactory.Create())
            {
                return Collect(context);
            }
        }

        public static DemoDataSmokeReport Collect(TaskOTimeContext context)
        {
            return Collect(context, DateTime.Today);
        }

        public static DemoDataSmokeReport Collect(TaskOTimeContext context, DateTime referenceDate)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var normalTimeItems = context.TimeItem
                .Where(item =>
                    item.EventTypeInfo == TimeBookingEventType &&
                    item.IdCategory != TaskOTime.DTOs.SystemTimeMarkerIds.WorkBreakCategoryId &&
                    item.IdCategory != TaskOTime.DTOs.SystemTimeMarkerIds.StopMarkCategoryId &&
                    !item.IsItemDeleted &&
                    item.BookingDate.HasValue &&
                    item.DurationTicksToNext.HasValue &&
                    item.DurationTicksToNext.Value > 0)
                .ToList();

            var bookingDates = normalTimeItems
                .Select(item => item.BookingDate.Value.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToArray();
            var multiBookingGroups = normalTimeItems
                .GroupBy(item => new { item.IdUser, item.IdProject, BookingDate = item.BookingDate.Value.Date })
                .Where(group => group.Count() > 1)
                .ToArray();
            var today = referenceDate.Date;
            return new DemoDataSmokeReport
            {
                TenantCount = context.Tenant.Count(tenant => tenant.IsActive && !tenant.IsDeleted),
                UserCount = context.User.Count(user => user.IsActive && !user.IsDeleted),
                AdminUserCount = context.User.Count(user => user.IsActive && !user.IsDeleted && user.IsAdmin),
                ProjectCount = context.Project.Count(project => project.IsActive && !project.IsDeleted),
                ActiveProjectAssignmentCount = context.ProjectUserAssignment.Count(assignment => assignment.IsActive && !assignment.IsDeleted),
                CrossTenantProjectAssignmentCount = CountCrossTenantProjectAssignments(context),
                OpenTaskCount = context.TaskItem.Count(task => !task.IsDeleted && !task.IsCompleted),
                ClosedTaskCount = context.TaskItem.Count(task => !task.IsDeleted && task.IsCompleted),
                TaggedTaskCount = context.TaskItem.Count(task => !task.IsDeleted && task.Tag.Any()),
                NoteCount = context.Note.Count(),
                WebLinkCount = context.WebLink.Count(),
                SystemMarkerLookupItemsSeeded = SystemTimeMarkerSeed.HasSeededLookupItems(context),
                SystemMarkerCategoriesSeeded = SystemTimeMarkerSeed.HasSeededCategories(context),
                AnalysisBookingCount = normalTimeItems.Count,
                AnalysisBookingUserCount = normalTimeItems.Select(item => item.IdUser).Distinct().Count(),
                AnalysisBookingProjectCount = normalTimeItems.Select(item => item.IdProject).Distinct().Count(),
                AnalysisBookingDateCount = bookingDates.Length,
                AnalysisBookingDates = bookingDates,
                FirstAnalysisBookingDate = bookingDates.Length == 0 ? (DateTime?)null : bookingDates[0],
                LastAnalysisBookingDate = bookingDates.Length == 0 ? (DateTime?)null : bookingDates[bookingDates.Length - 1],
                CurrentDayAnalysisBookingCount = normalTimeItems.Count(item => item.BookingDate.Value.Date == today),
                MultiBookingUserProjectDateGroupCount = multiBookingGroups.Length,
                MultiBookingDateCount = multiBookingGroups.Select(group => group.Key.BookingDate).Distinct().Count(),
                AnalysisBookedHours = normalTimeItems.Sum(item => item.DurationTicksToNext.Value) / (decimal)TimeSpan.TicksPerHour
            };
        }

        public void Validate()
        {
            if (TenantCount == 0 ||
                UserCount == 0 ||
                AdminUserCount == 0 ||
                ProjectCount == 0 ||
                ActiveProjectAssignmentCount == 0 ||
                OpenTaskCount == 0 ||
                ClosedTaskCount == 0 ||
                TaggedTaskCount == 0 ||
                NoteCount == 0 ||
                WebLinkCount == 0 ||
                !SystemMarkerLookupItemsSeeded ||
                !SystemMarkerCategoriesSeeded ||
                AnalysisBookingCount == 0 ||
                AnalysisBookingUserCount == 0 ||
                AnalysisBookingProjectCount == 0 ||
                AnalysisBookingDateCount == 0 ||
                CurrentDayAnalysisBookingCount == 0 ||
                MultiBookingUserProjectDateGroupCount == 0 ||
                AnalysisBookedHours <= 0)
            {
                throw new InvalidOperationException("Demo data smoke validation failed because analysis-relevant master data or bookings are missing.");
            }

            if (CrossTenantProjectAssignmentCount != 0)
            {
                throw new InvalidOperationException("Demo data smoke validation failed because cross-tenant project assignments were found.");
            }
        }

        public void Validate(DemoTimeItemDateOptions dateOptions, DateTime referenceDate)
        {
            if (dateOptions == null)
            {
                throw new ArgumentNullException(nameof(dateOptions));
            }

            Validate();

            var expectedDates = dateOptions.GetBookingDates(referenceDate)
                .OrderBy(date => date)
                .ToArray();
            if (!expectedDates.SequenceEqual(AnalysisBookingDates))
            {
                throw new InvalidOperationException("Demo data smoke validation failed because bookings do not cover the configured date range.");
            }

            if (MultiBookingDateCount != expectedDates.Length)
            {
                throw new InvalidOperationException("Demo data smoke validation failed because each configured booking date must contain a multi-booking user/project group.");
            }
        }

        public string ToSummaryString()
        {
            return "Tenants=" + TenantCount +
                ", Users=" + UserCount +
                " (Admins=" + AdminUserCount + ")" +
                ", Projects=" + ProjectCount +
                ", Assignments=" + ActiveProjectAssignmentCount +
                ", Tasks open/closed=" + OpenTaskCount + "/" + ClosedTaskCount +
                ", TaggedTasks=" + TaggedTaskCount +
                ", Notes=" + NoteCount +
                ", Links=" + WebLinkCount +
                ", SystemMarkers=" + (SystemMarkerLookupItemsSeeded && SystemMarkerCategoriesSeeded ? "Seeded" : "Missing") +
                ", AnalysisBookings=" + AnalysisBookingCount +
                " (" + AnalysisBookedHours.ToString("0.##") + "h)" +
                ", BookingUsers=" + AnalysisBookingUserCount +
                ", BookingProjects=" + AnalysisBookingProjectCount +
                ", BookingDates=" + AnalysisBookingDateCount +
                " (" + FormatDate(FirstAnalysisBookingDate) + ".." + FormatDate(LastAnalysisBookingDate) + ")" +
                ", TodayBookings=" + CurrentDayAnalysisBookingCount +
                ", MultiUserProjectDateGroups=" + MultiBookingUserProjectDateGroupCount +
                ", MultiBookingDates=" + MultiBookingDateCount +
                ", CrossTenantAssignments=" + CrossTenantProjectAssignmentCount;
        }

        private static string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "none";
        }

        private static int CountCrossTenantProjectAssignments(TaskOTimeContext context)
        {
            return (from assignment in context.ProjectUserAssignment
                    join project in context.Project on assignment.IdProject equals project.IdProject
                    join user in context.User on assignment.IdUser equals user.IdUser
                    where assignment.IsActive &&
                          !assignment.IsDeleted &&
                          project.IdTenant != user.IdTenant
                    select assignment).Count();
        }
    }
}
