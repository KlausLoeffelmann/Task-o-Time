using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Tests
{
    [TestClass]
    public class TimeBookingAlgorithmTests
    {
        private static readonly Guid UserId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid ProjectId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid CategoryId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid TaskId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly DateTimeOffset DayStart = new DateTimeOffset(2026, 6, 14, 9, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void NormalizeBookingDay_WithNoItems_ReturnsEmptyTimeline()
        {
            var items = new List<TimeItem>();

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            Assert.AreEqual(0, result.TimelineItems.Count);
            Assert.AreEqual(0, result.RemovedItems.Count);
            Assert.IsFalse(result.HasRemovedItems);
        }

        [TestMethod]
        public void NormalizeBookingDay_WithOneItem_ClearsStaleLinks()
        {
            var item = CreateItem(1, DayStart);
            item.IdNextItem = Guid.NewGuid();
            item.IdPreviousItem = Guid.NewGuid();
            item.DurationToNext = TimeSpan.FromMinutes(10);
            item.DurationTicksToNext = TimeSpan.FromMinutes(10).Ticks;
            item.DurationToPrevious = TimeSpan.FromMinutes(5);
            item.DurationTicksToPrevious = TimeSpan.FromMinutes(5).Ticks;
            var items = new List<TimeItem> { item };

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            Assert.AreSame(item, result.TimelineItems.Single());
            AssertNoLinks(item);
        }

        [TestMethod]
        public void NormalizeBookingDay_WithMultipleItems_RelinksInTimelineOrder()
        {
            var first = CreateItem(1, DayStart.AddHours(2));
            var second = CreateItem(2, DayStart);
            var third = CreateItem(3, DayStart.AddHours(1));
            var items = new List<TimeItem> { first, second, third };

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            CollectionAssert.AreEqual(
                new[] { second.IdTimeItem, third.IdTimeItem, first.IdTimeItem },
                result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            AssertLinked(second, third, TimeSpan.FromHours(1));
            AssertLinked(third, first, TimeSpan.FromHours(1));
            Assert.IsNull(first.IdNextItem);
            Assert.IsNull(second.IdPreviousItem);
        }

        [TestMethod]
        public void AddTimeItem_WithDuplicateEventTime_RejectsDuplicate()
        {
            var items = new List<TimeItem> { CreateItem(1, DayStart) };
            var duplicate = CreateItem(2, DayStart);

            Assert.ThrowsException<TimeBookingValidationException>(
                () => TimeBookingAlgorithm.AddTimeItem(items, duplicate));
        }

        [TestMethod]
        public void AddTimeItem_WithRetrospectiveInsertion_ReordersAndRecalculatesTimeline()
        {
            var first = CreateItem(1, DayStart);
            var third = CreateItem(3, DayStart.AddHours(2));
            var second = CreateItem(2, DayStart.AddHours(1));
            var items = new List<TimeItem> { first, third };
            TimeBookingAlgorithm.NormalizeBookingDay(items);

            var result = TimeBookingAlgorithm.AddTimeItem(items, second);

            CollectionAssert.AreEqual(
                new[] { first.IdTimeItem, second.IdTimeItem, third.IdTimeItem },
                result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            AssertLinked(first, second, TimeSpan.FromHours(1));
            AssertLinked(second, third, TimeSpan.FromHours(1));
            Assert.AreSame(second, result.AffectedItem);
        }

        [TestMethod]
        public void InsertWorkBreak_AfterWorkAndBreak_CreatesBreakThenResume()
        {
            var work = CreateItem(1, DayStart);
            work.IdTask = TaskId;
            work.ShortTitle = "Original work";
            var items = new List<TimeItem> { work };
            var breakId = new Guid("00000000-0000-0000-0000-000000000101");
            var resumeId = new Guid("00000000-0000-0000-0000-000000000102");

            var breakResult = TimeBookingAlgorithm.InsertWorkBreak(items, UserId, DayStart.AddMinutes(45), newTimeItemId: breakId);
            var resumeResult = TimeBookingAlgorithm.InsertWorkBreak(items, UserId, DayStart.AddMinutes(60), newTimeItemId: resumeId);

            var breakItem = breakResult.AffectedItem;
            var resumeItem = resumeResult.AffectedItem;
            Assert.AreEqual(LegacySystemTimeMarkerIds.WorkBreak, breakItem.IdCategory);
            Assert.AreEqual(ProjectId, breakItem.IdProject);
            Assert.AreEqual(CategoryId, resumeItem.IdCategory);
            Assert.AreEqual(ProjectId, resumeItem.IdProject);
            Assert.AreEqual(TaskId, resumeItem.IdTask);
            Assert.AreEqual("Original work", resumeItem.ShortTitle);
            AssertLinked(work, breakItem, TimeSpan.FromMinutes(45));
            AssertLinked(breakItem, resumeItem, TimeSpan.FromMinutes(15));
            CollectionAssert.AreEqual(
                new[] { work.IdTimeItem, breakId, resumeId },
                resumeResult.TimelineItems.Select(item => item.IdTimeItem).ToArray());
        }

        [TestMethod]
        public void NormalizeBookingDay_WithSuccessiveWorkBreaks_CollapsesEarlierBreak()
        {
            var work = CreateItem(1, DayStart);
            var firstBreak = CreateItem(2, DayStart.AddMinutes(30), LegacySystemTimeMarkerIds.WorkBreak);
            var secondBreak = CreateItem(3, DayStart.AddMinutes(45), LegacySystemTimeMarkerIds.WorkBreak);
            var resumedWork = CreateItem(4, DayStart.AddHours(1));
            var items = new List<TimeItem> { work, firstBreak, secondBreak, resumedWork };

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            CollectionAssert.AreEqual(new[] { firstBreak.IdTimeItem }, result.RemovedItems.Select(item => item.IdTimeItem).ToArray());
            CollectionAssert.AreEqual(
                new[] { work.IdTimeItem, secondBreak.IdTimeItem, resumedWork.IdTimeItem },
                result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            Assert.IsFalse(items.Contains(firstBreak));
            AssertLinked(work, secondBreak, TimeSpan.FromMinutes(45));
            AssertLinked(secondBreak, resumedWork, TimeSpan.FromMinutes(15));
            AssertNoLinks(firstBreak);
        }

        [TestMethod]
        public void InsertWorkBreak_WhenLatestTwoItemsAreBreaks_RejectsSuccessiveBreaks()
        {
            var items = new List<TimeItem>
            {
                CreateItem(1, DayStart),
                CreateItem(2, DayStart.AddMinutes(30), LegacySystemTimeMarkerIds.WorkBreak),
                CreateItem(3, DayStart.AddMinutes(45), LegacySystemTimeMarkerIds.WorkBreak)
            };

            Assert.ThrowsException<TimeBookingValidationException>(
                () => TimeBookingAlgorithm.InsertWorkBreak(items, UserId, DayStart.AddHours(1)));
        }

        [TestMethod]
        public void NormalizeBookingDay_WithStopMarkAtEnd_BreaksTimelineChain()
        {
            var work = CreateItem(1, DayStart);
            var stop = CreateItem(2, DayStart.AddMinutes(30), LegacySystemTimeMarkerIds.StopMark);
            var items = new List<TimeItem> { work, stop };

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            CollectionAssert.AreEqual(
                new[] { work.IdTimeItem, stop.IdTimeItem },
                result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            AssertLinked(work, stop, TimeSpan.FromMinutes(30));
            Assert.IsNull(stop.IdNextItem);
            Assert.IsNull(stop.DurationToNext);
            Assert.IsNull(stop.DurationTicksToNext);
        }

        [TestMethod]
        public void TimeBookingOptions_ErrandEventInfoOverridesStopCategory()
        {
            var errand = CreateItem(1, DayStart, LegacySystemTimeMarkerIds.StopMark);
            errand.EventInfo = TimeBookingOptions.ErrandEventInfo;

            var marker = new TimeBookingOptions().GetMarkerKind(errand);

            Assert.AreEqual(SystemTimeMarkerKind.Errand, marker);
        }

        [TestMethod]
        public void NormalizeBookingDay_WithErrand_PreservesContinuousNonWorkingTimeline()
        {
            var work = CreateItem(1, DayStart);
            var errand = CreateItem(2, DayStart.AddHours(1), LegacySystemTimeMarkerIds.StopMark);
            errand.EventInfo = TimeBookingOptions.ErrandEventInfo;
            var nextWork = CreateItem(3, DayStart.AddHours(2));
            var items = new List<TimeItem> { work, errand, nextWork };

            var result = TimeBookingAlgorithm.NormalizeBookingDay(items);

            CollectionAssert.AreEqual(
                new[] { work.IdTimeItem, errand.IdTimeItem, nextWork.IdTimeItem },
                result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            Assert.AreEqual(0, result.RemovedItems.Count);
            AssertLinked(work, errand, TimeSpan.FromHours(1));
            AssertLinked(errand, nextWork, TimeSpan.FromHours(1));
            Assert.AreEqual(SystemTimeMarkerKind.Errand, TimeBookingAlgorithm.GetMarkerKind(errand));
        }

        [TestMethod]
        public void NormalizeBookingDay_SetsDurationTicksConsistently()
        {
            var first = CreateItem(1, DayStart);
            var second = CreateItem(2, DayStart.AddMinutes(37).AddTicks(1234));
            var items = new List<TimeItem> { first, second };

            TimeBookingAlgorithm.NormalizeBookingDay(items);

            var expectedDuration = second.EventTime.Value - first.EventTime.Value;
            Assert.AreEqual(expectedDuration, first.DurationToNext);
            Assert.AreEqual(expectedDuration.Ticks, first.DurationTicksToNext);
            Assert.AreEqual(expectedDuration, second.DurationToPrevious);
            Assert.AreEqual(expectedDuration.Ticks, second.DurationTicksToPrevious);
        }

        [TestMethod]
        public void EditTimeItem_RecalculatesAdjacentDurations()
        {
            var first = CreateItem(1, DayStart);
            var middle = CreateItem(2, DayStart.AddHours(1));
            var last = CreateItem(3, DayStart.AddHours(2));
            var items = new List<TimeItem> { first, middle, last };
            TimeBookingAlgorithm.NormalizeBookingDay(items);
            var editedMiddle = CreateItem(2, DayStart.AddMinutes(90));

            var result = TimeBookingAlgorithm.EditTimeItem(items, editedMiddle);

            Assert.AreSame(middle, result.AffectedItem);
            Assert.AreEqual(DayStart.AddMinutes(90), middle.EventTime);
            AssertLinked(first, middle, TimeSpan.FromMinutes(90));
            AssertLinked(middle, last, TimeSpan.FromMinutes(30));
        }

        [TestMethod]
        public void RemoveTimeItem_RecalculatesTimelineAndClearsRemovedItem()
        {
            var first = CreateItem(1, DayStart);
            var middle = CreateItem(2, DayStart.AddHours(1));
            var last = CreateItem(3, DayStart.AddHours(2));
            var items = new List<TimeItem> { first, middle, last };
            TimeBookingAlgorithm.NormalizeBookingDay(items);

            var result = TimeBookingAlgorithm.RemoveTimeItem(items, middle.IdTimeItem);

            Assert.AreSame(middle, result.AffectedItem);
            CollectionAssert.AreEqual(new[] { middle.IdTimeItem }, result.RemovedItems.Select(item => item.IdTimeItem).ToArray());
            CollectionAssert.AreEqual(new[] { first.IdTimeItem, last.IdTimeItem }, result.TimelineItems.Select(item => item.IdTimeItem).ToArray());
            Assert.IsFalse(items.Contains(middle));
            AssertLinked(first, last, TimeSpan.FromHours(2));
            AssertNoLinks(middle);
        }

        private static TimeItem CreateItem(int idSuffix, DateTimeOffset eventTime)
        {
            return CreateItem(idSuffix, eventTime, CategoryId);
        }

        private static TimeItem CreateItem(int idSuffix, DateTimeOffset eventTime, Guid categoryId)
        {
            return new TimeItem
            {
                IdTimeItem = GuidFromSuffix(idSuffix),
                IdUser = UserId,
                IdProject = ProjectId,
                IdCategory = categoryId,
                EventTime = eventTime,
                BookingDate = eventTime.Date,
                EventTypeInfo = (int)TimeBookingEventType.Time,
                DateCreated = eventTime,
                DateModified = eventTime,
                SyncId = GuidFromSuffix(1000 + idSuffix)
            };
        }

        private static Guid GuidFromSuffix(int suffix)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-{0:000000000000}", suffix));
        }

        private static void AssertLinked(TimeItem current, TimeItem next, TimeSpan duration)
        {
            Assert.AreEqual(next.IdTimeItem, current.IdNextItem);
            Assert.AreEqual(current.IdTimeItem, next.IdPreviousItem);
            Assert.AreEqual(duration, current.DurationToNext);
            Assert.AreEqual(duration.Ticks, current.DurationTicksToNext);
            Assert.AreEqual(duration, next.DurationToPrevious);
            Assert.AreEqual(duration.Ticks, next.DurationTicksToPrevious);
        }

        private static void AssertNoLinks(TimeItem item)
        {
            Assert.IsNull(item.IdNextItem);
            Assert.IsNull(item.IdPreviousItem);
            Assert.IsNull(item.DurationToNext);
            Assert.IsNull(item.DurationTicksToNext);
            Assert.IsNull(item.DurationToPrevious);
            Assert.IsNull(item.DurationTicksToPrevious);
        }
    }
}
