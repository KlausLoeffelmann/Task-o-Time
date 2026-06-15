using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.TimeBooking
{
    public static class TimeBookingAlgorithm
    {
        public static TimeBookingChangeSet NormalizeBookingDay(IList<TimeItem> bookingDayItems, TimeBookingOptions options = null)
        {
            options = options ?? new TimeBookingOptions();
            var timelineItems = GetTimelineItems(bookingDayItems, options);
            EnsureNoDuplicateEventTimes(timelineItems, null);

            var removedItems = ApplyPlausibilityCleanup(timelineItems, options);
            RemoveItemsFromSource(bookingDayItems, removedItems);
            RelinkTimeline(timelineItems, options);

            return new TimeBookingChangeSet(timelineItems, removedItems, null);
        }

        public static TimeBookingChangeSet AddTimeItem(IList<TimeItem> bookingDayItems, TimeItem timeItemToAdd, TimeBookingOptions options = null)
        {
            if (bookingDayItems == null)
            {
                throw new ArgumentNullException(nameof(bookingDayItems));
            }

            options = options ?? new TimeBookingOptions();
            PrepareTimeItemForBooking(timeItemToAdd, options);

            if (bookingDayItems.Any(item => item.IdTimeItem == timeItemToAdd.IdTimeItem))
            {
                throw new TimeBookingValidationException("A time item with the same ID already exists in this booking day.");
            }

            EnsureNoDuplicateEventTime(bookingDayItems, timeItemToAdd.EventTime.Value, null, options);
            bookingDayItems.Add(timeItemToAdd);

            var result = NormalizeBookingDay(bookingDayItems, options);
            return new TimeBookingChangeSet(result.TimelineItems, result.RemovedItems, timeItemToAdd);
        }

        public static TimeBookingChangeSet EditTimeItem(IList<TimeItem> bookingDayItems, TimeItem editedTimeItem, TimeBookingOptions options = null)
        {
            if (bookingDayItems == null)
            {
                throw new ArgumentNullException(nameof(bookingDayItems));
            }

            options = options ?? new TimeBookingOptions();
            PrepareTimeItemForBooking(editedTimeItem, options);

            var existingItem = bookingDayItems.SingleOrDefault(item => item.IdTimeItem == editedTimeItem.IdTimeItem);
            if (existingItem == null)
            {
                throw new TimeBookingValidationException("The time item to edit does not exist in this booking day.");
            }

            EnsureNoDuplicateEventTime(bookingDayItems, editedTimeItem.EventTime.Value, editedTimeItem.IdTimeItem, options);
            if (!ReferenceEquals(existingItem, editedTimeItem))
            {
                CopyEditableValues(editedTimeItem, existingItem);
            }

            var result = NormalizeBookingDay(bookingDayItems, options);
            return new TimeBookingChangeSet(result.TimelineItems, result.RemovedItems, existingItem);
        }

        public static TimeBookingChangeSet RemoveTimeItem(IList<TimeItem> bookingDayItems, Guid idTimeItem, TimeBookingOptions options = null)
        {
            if (bookingDayItems == null)
            {
                throw new ArgumentNullException(nameof(bookingDayItems));
            }

            var itemToRemove = bookingDayItems.SingleOrDefault(item => item.IdTimeItem == idTimeItem);
            if (itemToRemove == null)
            {
                throw new TimeBookingValidationException("The time item to remove does not exist in this booking day.");
            }

            bookingDayItems.Remove(itemToRemove);
            ClearLinks(itemToRemove);

            var result = NormalizeBookingDay(bookingDayItems, options);
            var removedItems = result.RemovedItems.Concat(new[] { itemToRemove });
            return new TimeBookingChangeSet(result.TimelineItems, removedItems, itemToRemove);
        }

        public static TimeBookingChangeSet InsertWorkBreak(
            IList<TimeItem> userTimeItems,
            Guid idUser,
            DateTimeOffset workBreakTime,
            TimeBookingOptions options = null,
            Guid? newTimeItemId = null)
        {
            if (userTimeItems == null)
            {
                throw new ArgumentNullException(nameof(userTimeItems));
            }

            options = options ?? new TimeBookingOptions();

            var userItems = userTimeItems.Where(item => item.IdUser == idUser).ToList();
            var latestItems = GetTimelineItems(userItems, options)
                .OrderByDescending(item => item.EventTime.Value)
                .Take(2)
                .ToList();

            if (latestItems.Count == 0)
            {
                throw new TimeBookingValidationException("A booking series cannot start with a work break or stop mark.");
            }

            var lastItem = latestItems[0];
            var bookingDate = ResolveBookingDateForWorkBreak(lastItem, workBreakTime, options);
            EnsureNoDuplicateEventTime(userItems, workBreakTime, null, options);

            TimeItem timeItemToAdd;
            if (options.GetMarkerKind(lastItem) != SystemTimeMarkerKind.WorkBreak)
            {
                timeItemToAdd = CreateWorkBreakItem(lastItem, idUser, workBreakTime, bookingDate, options, newTimeItemId);
            }
            else
            {
                if (latestItems.Count < 2 || options.GetMarkerKind(latestItems[1]) == SystemTimeMarkerKind.WorkBreak)
                {
                    throw new TimeBookingValidationException("Two successive work-break bookings do not make sense.");
                }

                timeItemToAdd = CreateResumeWorkItem(latestItems[1], idUser, workBreakTime, bookingDate, options, newTimeItemId);
            }

            userTimeItems.Add(timeItemToAdd);
            var bookingDayItems = userTimeItems
                .Where(item => item.IdUser == idUser && SameBookingDate(item.BookingDate, bookingDate))
                .ToList();

            var result = NormalizeBookingDay(bookingDayItems, options);
            RemoveItemsFromSource(userTimeItems, result.RemovedItems);

            return new TimeBookingChangeSet(result.TimelineItems, result.RemovedItems, timeItemToAdd);
        }

        public static SystemTimeMarkerKind GetMarkerKind(TimeItem timeItem, TimeBookingOptions options = null)
        {
            return (options ?? new TimeBookingOptions()).GetMarkerKind(timeItem);
        }

        private static List<TimeItem> GetTimelineItems(IEnumerable<TimeItem> items, TimeBookingOptions options)
        {
            if (items == null)
            {
                return new List<TimeItem>();
            }

            var timelineItems = items
                .Where(item => IsTimelineItem(item, options))
                .ToList();

            foreach (var item in timelineItems)
            {
                if (!item.EventTime.HasValue)
                {
                    throw new TimeBookingValidationException("A time booking must have an event time.");
                }
            }

            return timelineItems
                .OrderBy(item => item.EventTime.Value)
                .ThenBy(item => item.IdTimeItem)
                .ToList();
        }

        private static bool IsTimelineItem(TimeItem item, TimeBookingOptions options)
        {
            if (item == null)
            {
                return false;
            }

            if (options.IgnoreDeletedItems && item.IsItemDeleted)
            {
                return false;
            }

            return item.EventTypeInfo == options.TimeEventType;
        }

        private static void PrepareTimeItemForBooking(TimeItem timeItem, TimeBookingOptions options)
        {
            if (timeItem == null)
            {
                throw new ArgumentNullException(nameof(timeItem));
            }

            if (timeItem.EventTypeInfo != options.TimeEventType)
            {
                throw new TimeBookingValidationException("Only time-booking items can be linked in the booking-day timeline.");
            }

            if (!timeItem.EventTime.HasValue)
            {
                throw new TimeBookingValidationException("A time booking must have an event time.");
            }

            if (!timeItem.BookingDate.HasValue)
            {
                timeItem.BookingDate = timeItem.EventTime.Value.Date;
            }

            if (timeItem.IdTimeItem == Guid.Empty)
            {
                timeItem.IdTimeItem = Guid.NewGuid();
            }
        }

        private static void EnsureNoDuplicateEventTimes(IList<TimeItem> timelineItems, Guid? ignoredTimeItemId)
        {
            var duplicate = timelineItems
                .Where(item => !ignoredTimeItemId.HasValue || item.IdTimeItem != ignoredTimeItemId.Value)
                .GroupBy(item => item.EventTime.Value)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate != null)
            {
                throw new TimeBookingValidationException("The same point in time cannot be recorded twice for one user.");
            }
        }

        private static void EnsureNoDuplicateEventTime(
            IEnumerable<TimeItem> items,
            DateTimeOffset eventTime,
            Guid? ignoredTimeItemId,
            TimeBookingOptions options)
        {
            var hasDuplicate = items
                .Where(item => IsTimelineItem(item, options))
                .Any(item => item.EventTime.HasValue &&
                             item.EventTime.Value == eventTime &&
                             (!ignoredTimeItemId.HasValue || item.IdTimeItem != ignoredTimeItemId.Value));

            if (hasDuplicate)
            {
                throw new TimeBookingValidationException("The same point in time cannot be recorded twice for one user.");
            }
        }

        private static List<TimeItem> ApplyPlausibilityCleanup(IList<TimeItem> timelineItems, TimeBookingOptions options)
        {
            var removedItems = new List<TimeItem>();
            if (timelineItems.Count < 2)
            {
                return removedItems;
            }

            var index = 0;
            var lastItemWasWorkBreak = false;

            while (index <= timelineItems.Count - 1)
            {
                var markerKind = options.GetMarkerKind(timelineItems[index]);

                if (index < timelineItems.Count - 1 && markerKind == SystemTimeMarkerKind.StopMark)
                {
                    removedItems.Add(timelineItems[index]);
                    ClearLinks(timelineItems[index]);
                    timelineItems.RemoveAt(index);
                    continue;
                }

                if (markerKind == SystemTimeMarkerKind.WorkBreak)
                {
                    if (lastItemWasWorkBreak)
                    {
                        var previousWorkBreak = timelineItems[index - 1];
                        removedItems.Add(previousWorkBreak);
                        ClearLinks(previousWorkBreak);
                        timelineItems.RemoveAt(index - 1);
                        continue;
                    }

                    lastItemWasWorkBreak = true;
                }
                else
                {
                    lastItemWasWorkBreak = false;
                }

                index++;
            }

            return removedItems;
        }

        private static void RelinkTimeline(IList<TimeItem> timelineItems, TimeBookingOptions options)
        {
            foreach (var item in timelineItems)
            {
                ClearLinks(item);
            }

            for (var index = 0; index < timelineItems.Count - 1; index++)
            {
                var current = timelineItems[index];
                var next = timelineItems[index + 1];

                if (options.GetMarkerKind(current) == SystemTimeMarkerKind.StopMark)
                {
                    continue;
                }

                var duration = next.EventTime.Value - current.EventTime.Value;
                current.IdNextItem = next.IdTimeItem;
                current.DurationToNext = duration;
                current.DurationTicksToNext = duration.Ticks;
                next.IdPreviousItem = current.IdTimeItem;
                next.DurationToPrevious = duration;
                next.DurationTicksToPrevious = duration.Ticks;
            }
        }

        private static void ClearLinks(TimeItem item)
        {
            item.IdNextItem = null;
            item.IdPreviousItem = null;
            item.DurationToNext = null;
            item.DurationTicksToNext = null;
            item.DurationToPrevious = null;
            item.DurationTicksToPrevious = null;
        }

        private static void RemoveItemsFromSource(IList<TimeItem> sourceItems, IEnumerable<TimeItem> removedItems)
        {
            if (sourceItems == null)
            {
                return;
            }

            foreach (var removedItem in removedItems.ToList())
            {
                sourceItems.Remove(removedItem);
            }
        }

        private static DateTime ResolveBookingDateForWorkBreak(TimeItem lastItem, DateTimeOffset workBreakTime, TimeBookingOptions options)
        {
            var bookingDate = workBreakTime.Date;
            if (lastItem.BookingDate.HasValue &&
                lastItem.BookingDate.Value.Date < bookingDate &&
                lastItem.EventTime.HasValue)
            {
                if (workBreakTime - lastItem.EventTime.Value > options.BookingDateThreshold)
                {
                    throw new TimeBookingValidationException("A booking series cannot start with a work break or stop mark.");
                }

                bookingDate = bookingDate.AddDays(-1);
            }

            return bookingDate;
        }

        private static TimeItem CreateWorkBreakItem(
            TimeItem lastItem,
            Guid idUser,
            DateTimeOffset eventTime,
            DateTime bookingDate,
            TimeBookingOptions options,
            Guid? newTimeItemId)
        {
            return new TimeItem
            {
                IdTimeItem = newTimeItemId ?? Guid.NewGuid(),
                IdUser = idUser,
                IdProject = options.WorkBreakProjectId ?? lastItem.IdProject,
                IdCategory = options.WorkBreakCategoryId,
                EventTime = eventTime,
                BookingDate = bookingDate,
                EventTypeInfo = options.TimeEventType,
                DateCreated = DateTimeOffset.Now,
                DateModified = DateTimeOffset.Now,
                SyncId = Guid.NewGuid()
            };
        }

        private static TimeItem CreateResumeWorkItem(
            TimeItem previousWorkItem,
            Guid idUser,
            DateTimeOffset eventTime,
            DateTime bookingDate,
            TimeBookingOptions options,
            Guid? newTimeItemId)
        {
            return new TimeItem
            {
                IdTimeItem = newTimeItemId ?? Guid.NewGuid(),
                IdUser = idUser,
                IdProject = previousWorkItem.IdProject,
                IdCategory = previousWorkItem.IdCategory,
                IdTask = previousWorkItem.IdTask,
                ShortTitle = previousWorkItem.ShortTitle,
                Description = previousWorkItem.Description,
                EventTime = eventTime,
                BookingDate = bookingDate,
                EventTypeInfo = options.TimeEventType,
                Scope = previousWorkItem.Scope,
                IsStartAction = previousWorkItem.IsStartAction,
                IsEndAction = previousWorkItem.IsEndAction,
                Value = previousWorkItem.Value,
                Priority = previousWorkItem.Priority,
                MachineID = previousWorkItem.MachineID,
                DateCreated = DateTimeOffset.Now,
                DateModified = DateTimeOffset.Now,
                SyncId = Guid.NewGuid()
            };
        }

        private static bool SameBookingDate(DateTime? left, DateTime right)
        {
            return left.HasValue && left.Value.Date == right.Date;
        }

        private static void CopyEditableValues(TimeItem source, TimeItem target)
        {
            target.IdUser = source.IdUser;
            target.IdProject = source.IdProject;
            target.IdCategory = source.IdCategory;
            target.IdTask = source.IdTask;
            target.ShortTitle = source.ShortTitle;
            target.Description = source.Description;
            target.EventTime = source.EventTime;
            target.BookingDate = source.BookingDate;
            target.EventInfo = source.EventInfo;
            target.EventTypeInfo = source.EventTypeInfo;
            target.IdParentItem = source.IdParentItem;
            target.Scope = source.Scope;
            target.IsItemCompleted = source.IsItemCompleted;
            target.IsItemDeleted = source.IsItemDeleted;
            target.IsStartAction = source.IsStartAction;
            target.IsEndAction = source.IsEndAction;
            target.Value = source.Value;
            target.Priority = source.Priority;
            target.DateItemAcceptedOrRejected = source.DateItemAcceptedOrRejected;
            target.MachineID = source.MachineID;
            target.IdItemAssignedToUser = source.IdItemAssignedToUser;
            target.IdUserItemFrom = source.IdUserItemFrom;
            target.DateItemFinished = source.DateItemFinished;
            target.DateModified = source.DateModified;
            target.SyncId = source.SyncId;
            target.SyncStatus = source.SyncStatus;
            target.ExternalId = source.ExternalId;
        }
    }
}
