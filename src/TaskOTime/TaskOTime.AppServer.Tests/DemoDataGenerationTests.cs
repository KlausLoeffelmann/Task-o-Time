using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.Cli;
using TaskOTime.DataLayer;

namespace TaskOTime.AppServer.Tests
{
    [TestClass]
    public class DemoDataGenerationTests
    {
        private static readonly DateTimeOffset ReferenceTime =
            new DateTimeOffset(2026, 9, 10, 13, 30, 0, TimeSpan.FromHours(2));

        [TestMethod]
        public void DemoDataConfigLoader_ReadsTimeItemDateSettingsAndDefaults()
        {
            var defaults = DemoDataConfigLoader.LoadJson("{}");
            var configured = DemoDataConfigLoader.LoadJson(
                "{\"timeItems\":180,\"timeItemDates\":{\"days\":45,\"includeToday\":true,\"skipWeekends\":true}}");

            Assert.AreEqual(30, defaults.TimeItemDates.Days);
            Assert.IsTrue(defaults.TimeItemDates.IncludeToday);
            Assert.IsFalse(defaults.TimeItemDates.SkipWeekends);
            Assert.AreEqual(45, configured.TimeItemDates.Days);
            Assert.IsTrue(configured.TimeItemDates.IncludeToday);
            Assert.IsTrue(configured.TimeItemDates.SkipWeekends);
        }

        [TestMethod]
        public void DemoDataOptions_RejectsRangesWithoutTodayOrEnoughBookings()
        {
            var withoutToday = CreateOptions();
            withoutToday.TimeItemDates.IncludeToday = false;
            var tooFewBookings = CreateOptions();
            tooFewBookings.TimeItemDates.Days = 31;

            Assert.ThrowsException<InvalidOperationException>(() => withoutToday.Validate());
            Assert.ThrowsException<InvalidOperationException>(() => tooFewBookings.Validate());
        }

        [TestMethod]
        public void CreateDemoData_CoversConfiguredRangeAndAvoidsUserEventTimeDuplicates()
        {
            var context = new TaskOTimeContext();
            var options = CreateOptions();
            var report = new DemoDataGenerator(() => context, () => ReferenceTime).CreateDemoData(options);
            var expectedDates = options.TimeItemDates.GetBookingDates(ReferenceTime.Date)
                .OrderBy(date => date)
                .ToArray();
            var actualDates = context.TimeItem.Select(item => item.BookingDate.Value.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToArray();

            CollectionAssert.AreEqual(expectedDates, actualDates);
            Assert.AreEqual(expectedDates.First(), report.FirstAnalysisBookingDate);
            Assert.AreEqual(expectedDates.Last(), report.LastAnalysisBookingDate);
            Assert.AreEqual(expectedDates.Length, report.MultiBookingDateCount);
            Assert.AreEqual(options.Users.Count, report.AnalysisBookingUserCount);
            Assert.AreEqual(options.Projects, report.AnalysisBookingProjectCount);
            Assert.IsTrue(report.CurrentDayAnalysisBookingCount > 0);
            Assert.IsFalse(context.TimeItem
                .GroupBy(item => new { item.IdUser, item.EventTime })
                .Any(group => group.Count() > 1));
            report.Validate(options.TimeItemDates, ReferenceTime.Date);
        }

        [TestMethod]
        public void CreateDemoData_UsesDeterministicBookingDistribution()
        {
            var firstContext = new TaskOTimeContext();
            var secondContext = new TaskOTimeContext();
            var options = CreateOptions();

            new DemoDataGenerator(() => firstContext, () => ReferenceTime).CreateDemoData(options);
            new DemoDataGenerator(() => secondContext, () => ReferenceTime).CreateDemoData(options);

            CollectionAssert.AreEqual(
                GetBookingDistribution(firstContext).ToArray(),
                GetBookingDistribution(secondContext).ToArray());
        }

        [TestMethod]
        public void CurrentConfig_BookingTimelineMatchesServiceNormalizationSemantics()
        {
            var context = new TaskOTimeContext();
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demodataconfig.json");
            var options = DemoDataConfigLoader.Load(configPath);

            var report = new DemoDataGenerator(() => context, () => ReferenceTime).CreateDemoData(options);
            var expectedDates = options.TimeItemDates.GetBookingDates(ReferenceTime.Date);

            Assert.AreEqual(45, expectedDates.Count);
            Assert.AreEqual(360, context.TimeItem.Count());
            Assert.AreEqual(expectedDates.Count, report.AnalysisBookingDateCount);
            CollectionAssert.AreEqual(
                expectedDates.OrderBy(date => date).ToArray(),
                context.TimeItem.Select(item => item.BookingDate.Value.Date).Distinct().OrderBy(date => date).ToArray());
            Assert.IsFalse(context.TimeItem
                .GroupBy(item => new { item.IdUser, item.EventTime })
                .Any(group => group.Count() > 1));

            foreach (var userDay in context.TimeItem.GroupBy(item => new { item.IdUser, BookingDate = item.BookingDate.Value.Date }))
            {
                var timeline = userDay.OrderBy(item => item.EventTime).ToList();
                var generatedTimelineState = GetTimelineState(timeline);

                Assert.IsTrue(timeline.First().EventTime.Value.TimeOfDay >= TimeSpan.FromHours(8));
                Assert.IsTrue(timeline.Last().EventTime.Value.TimeOfDay <= TimeSpan.FromHours(20));

                for (var index = 0; index < timeline.Count - 1; index++)
                {
                    var current = timeline[index];
                    var next = timeline[index + 1];
                    var spacing = next.EventTime.Value - current.EventTime.Value;

                    Assert.IsTrue(spacing >= TimeSpan.FromMinutes(30));
                    Assert.IsTrue(spacing <= TimeSpan.FromMinutes(90));
                    Assert.AreEqual(spacing, current.DurationToNext);
                    Assert.AreEqual(spacing.Ticks, current.DurationTicksToNext);
                    Assert.AreEqual(next.EventTime, current.EventTime.Value.Add(current.DurationToNext.Value));
                    Assert.AreEqual(next.IdTimeItem, current.IdNextItem);
                    Assert.AreEqual(current.IdTimeItem, next.IdPreviousItem);
                    Assert.AreEqual(spacing, next.DurationToPrevious);
                    Assert.AreEqual(spacing.Ticks, next.DurationTicksToPrevious);
                    Assert.AreEqual(next.EventTime, current.DateItemFinished);
                    Assert.AreEqual((decimal)spacing.TotalHours, current.Value);
                    Assert.IsTrue(current.EventTime.Value.Add(current.DurationToNext.Value) <= next.EventTime.Value);
                }

                Assert.IsNull(timeline.Last().DurationToNext);
                Assert.IsNull(timeline.Last().DurationTicksToNext);
                Assert.IsNull(timeline.Last().IdNextItem);
                Assert.IsNull(timeline.Last().DateItemFinished);
                Assert.IsNull(timeline.Last().Value);

                var normalized = TimeBookingAlgorithm.NormalizeBookingDay(timeline);
                Assert.AreEqual(0, normalized.RemovedItems.Count);
                CollectionAssert.AreEqual(generatedTimelineState, GetTimelineState(timeline));
            }
        }

        [TestMethod]
        public void TimeItemDates_SkipsPastWeekendsButAlwaysIncludesReferenceDate()
        {
            var options = new DemoTimeItemDateOptions
            {
                Days = 7,
                IncludeToday = true,
                SkipWeekends = true
            };

            var dates = options.GetBookingDates(new DateTime(2026, 9, 12));

            CollectionAssert.AreEqual(
                new[]
                {
                    new DateTime(2026, 9, 12),
                    new DateTime(2026, 9, 11),
                    new DateTime(2026, 9, 10),
                    new DateTime(2026, 9, 9),
                    new DateTime(2026, 9, 8),
                    new DateTime(2026, 9, 7)
                },
                dates.ToArray());
        }

        private static DemoDataOptions CreateOptions()
        {
            return new DemoDataOptions
            {
                Tenants = 1,
                Projects = 2,
                Users = new List<DemoUserOptions>
                {
                    new DemoUserOptions
                    {
                        Handle = "Admin",
                        FirstName = "Avery",
                        LastName = "Admin",
                        Email = "admin@example.test",
                        IsAdmin = true
                    },
                    new DemoUserOptions
                    {
                        Handle = "Booker",
                        FirstName = "Blair",
                        LastName = "Booker",
                        Email = "booker@example.test",
                        IsAdmin = false
                    }
                },
                Categories = 10,
                Lists = 2,
                Tasks = new DemoTaskOptions { Open = 15, Closed = 15 },
                TimeItems = 60,
                TimeItemDates = new DemoTimeItemDateOptions
                {
                    Days = 10,
                    IncludeToday = true,
                    SkipWeekends = false
                },
                ProjectAssignments = new DemoProjectAssignmentOptions
                {
                    Mode = "AllTenantUsers",
                    IncludeAdmins = true
                }
            };
        }

        private static IEnumerable<string> GetBookingDistribution(TaskOTimeContext context)
        {
            var userHandles = context.User.ToDictionary(user => user.IdUser, user => user.UserIdent);
            var projectNumbers = context.Project.ToDictionary(project => project.IdProject, project => project.ProjectNumber);
            return context.TimeItem
                .OrderBy(item => ParseExternalIndex(item.ExternalId))
                .Select(item =>
                    userHandles[item.IdUser] + "|" +
                    projectNumbers[item.IdProject] + "|" +
                    item.BookingDate.Value.ToString("yyyy-MM-dd") + "|" +
                    item.EventTime.Value.ToString("HH:mm:ss"));
        }

        private static int ParseExternalIndex(string externalId)
        {
            return int.Parse(externalId.Substring("demo-time-".Length));
        }

        private static string[] GetTimelineState(IEnumerable<TaskOTime.DTOs.TimeItem> timeline)
        {
            return timeline.Select(item =>
                    item.IdTimeItem + "|" +
                    item.IdPreviousItem + "|" +
                    item.IdNextItem + "|" +
                    item.DurationTicksToPrevious + "|" +
                    item.DurationTicksToNext)
                .ToArray();
        }
    }
}
