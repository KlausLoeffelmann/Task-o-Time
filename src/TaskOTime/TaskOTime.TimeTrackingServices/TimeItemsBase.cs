using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ActiveDevelop.TimeTrackingServices
{
    /// <summary>
    /// Maintains time items in its own sorted list and notifies subscribers of changes.
    /// Implements the list and collection contracts directly rather than inheriting from
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{TimeItemType}"/>.
    /// </summary>
    /// <remarks>
    /// Items are ordered by <see cref="ITimeItem{IndexType}.EventTime"/>, not by insertion order.
    /// Sorting and reloading can retain the same collection instance. Neighbor links and their
    /// associated notifications are maintained separately by this collection.
    /// Timestamp comparisons, not identifiers, determine lookup and uniqueness. Positional insertion
    /// is unsupported even though the collection is mutable. Notifications are synchronous, not transactional.
    /// </remarks>
    public class TimeItemsBase<IndexType, TimeItemType> : IEnumerable<TimeItemType>, ICollection<TimeItemType>, IList<TimeItemType>, IList, INotifyCollectionChanged, INotifyPropertyChanged where IndexType : struct, IComparable<IndexType> where TimeItemType : class, ITimeItem<IndexType>, INotifyPropertyChanged, new()
    {
        private const string EventTimePropertyName = "EventTime";
        private readonly PropertyChangedEventArgs _countPropertyChangedEventArgs;
        private readonly PropertyChangedEventArgs _itemPropertyChangedEventArgs = new PropertyChangedEventArgs("Item[]");
        private readonly List<TimeItemType> _sortedList = new List<TimeItemType>();
        private readonly TimeItemByEventTimeComparer<IndexType, TimeItemType> _comparer = new TimeItemByEventTimeComparer<IndexType, TimeItemType>();
        /// <summary>
        /// Reports additions, removals, replacements, moves and resets of the sorted collection.
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        /// <summary>
        /// Reports count and indexer changes separately from the collection-change event.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>
        /// Creates an empty collection that retains its own list and reusable notification arguments.
        /// </summary>
        public TimeItemsBase()
        {
            _countPropertyChangedEventArgs = new PropertyChangedEventArgs(nameof(Count));
        }

        private void AddRangeSuspended(IEnumerable<TimeItemType> range)
        {
            foreach (var timeItem in range)
                Add(timeItem);
        }

        /// <summary>
        /// Creates an item with the supplied timestamp and identifier, stamps its audit times, and inserts it in sorted order.
        /// </summary>
        /// <remarks>
        /// The date value's kind determines its offset. Insertion uses the same duplicate-timestamp checks as existing items.
        /// </remarks>
        public void Add(DateTime eventTime, IndexType newID)
        {
            var timeItem = new TimeItemType()
            {
                IDTimeItem = newID,
                EventTime = new DateTimeOffset(eventTime),
                DateCreated = DateTimeOffset.Now,
                LastChanged = DateTimeOffset.Now
            };
            Add(timeItem);
        }

        /// <summary>
        /// Compares only the linked neighbors and returns a direction, not an insertion index.
        /// </summary>
        /// <remarks>
        /// Comparisons involving missing items or timestamps are skipped. A zero result therefore does
        /// not prove timestamp uniqueness; duplicate detection is separate from this prediction.
        /// </remarks>
        public int PredictNewPosition(TimeItemType item)
        {
            if (item is not null && item.PreviousItem is not null && item.PreviousItem.EventTime.HasValue && item.EventTime.HasValue && item.PreviousItem.EventTime.Value > item.EventTime.Value)
            {
                return -1;
            }

            if (item is not null && item.NextItem is not null && item.NextItem.EventTime.HasValue && item.EventTime.HasValue && item.NextItem.EventTime.Value < item.EventTime.Value)
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Inserts a time item in timestamp order and returns its insertion index.
        /// </summary>
        /// <remarks>
        /// Null items are rejected, but an item with no timestamp may appear at the beginning.
        /// Equal timestamps are rejected, including two missing timestamps.
        /// Insertion raises notifications for <see cref="Count"/> and <see cref="this[int]"/>.
        /// The subsequent collection notification includes the added item and its index.
        /// </remarks>
        public int AddWithPositionInfo(TimeItemType item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (_sortedList.Count == 0)
            {
                item.ItemState = TimeItemState.Added;
                _sortedList.Add(item);
                UpdateItem(item, 0);
                WirePropertyChangeEvent(item);
                OnPropertyChanged(_countPropertyChangedEventArgs);
                OnPropertyChanged(_itemPropertyChangedEventArgs);
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, 0));
                return 0;
            }

            int index = _sortedList.BinarySearch(item, _comparer);
            if (index >= 0)
            {
                ThrowSamePointInTimeException(item);
            }

            int insertionIndex = -index - 1;
            InsertWithUpdate(item, insertionIndex);
            return insertionIndex;
        }

        /// <summary>
        /// Adds the supplied instance in timestamp order, updating neighbor links and notifying subscribers.
        /// </summary>
        /// <remarks>
        /// The item is not cloned. Null items and duplicate timestamps are rejected by <see cref="AddWithPositionInfo"/>.
        /// </remarks>
        public void Add(TimeItemType item)
        {
            AddWithPositionInfo(item);
        }

        private static void ThrowSamePointInTimeException(TimeItemType item)
        {
            throw new ArgumentException("The same point in time can't be recorded twice for one EventSource: " + item.ToString());
        }

        private void InsertWithUpdate(TimeItemType item, int index)
        {
            item.ItemState = TimeItemState.Added;
            if (index == _sortedList.Count)
            {
                _sortedList.Add(item);
            }
            else
            {
                _sortedList.Insert(index, item);
            }

            WirePropertyChangeEvent(item);
            UpdateItem(item, index);
            // Count reports the size; the indexer notification exposes changes to item positions.
            // The collection notification also carries the added item and its index.
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        }

        private TimeItemType UpdateItem(TimeItemType item, int index)
        {
            var previous = index > 0 ? _sortedList[index - 1] : null;
            var next = index < _sortedList.Count - 1 ? _sortedList[index + 1] : null;
            item.PreviousItem = null;
            item.NextItem = null;
            item.DurationToPrevious = default;
            item.DurationToNext = default;
            LinkItems(previous, item);
            LinkItems(item, next);
            return item;
        }

        /// <summary>
        /// Sets both directions of a neighbor link using the same calculated time difference.
        /// </summary>
        /// <remarks>
        /// At either end of the list, the remaining neighbor is still updated. Its outward reference
        /// and duration are both absent; a missing duration is not the same as zero.
        /// </remarks>
        private static void LinkItems(TimeItemType previous, TimeItemType next)
        {
            var duration = CalculateDuration(previous, next);
            if (previous is not null)
            {
                previous.NextItem = next;
                previous.DurationToNext = duration;
            }

            if (next is not null)
            {
                next.PreviousItem = previous;
                next.DurationToPrevious = duration;
            }
        }

        /// <summary>
        /// Calculates the time difference, or returns no value if either neighbor or timestamp is missing.
        /// </summary>
        /// <remarks>
        /// This collection-level calculation does not inspect action flags. It describes the distance
        /// between timestamps, not the business meaning of a booking or interruption.
        /// </remarks>
        private static TimeSpan? CalculateDuration(TimeItemType previous, TimeItemType next)
        {
            if (previous is null || next is null || !previous.EventTime.HasValue || !next.EventTime.HasValue)
            {
                return default;
            }

            return next.EventTime.Value - previous.EventTime.Value;
        }

        /// <summary>
        /// Checks for timestamp conflicts before moving or replacing an existing item.
        /// </summary>
        /// <param name="item">The item to reposition or use as the replacement.</param>
        /// <param name="oldValue">
        /// The item being replaced; when absent, the existing item is repositioned.
        /// </param>
        /// <remarks>
        /// Replacement at the same index raises a replacement event and only the indexer property notification.
        /// A different index causes removal followed by insertion, each with its own count notification.
        /// </remarks>
        private void SetItemInternal(TimeItemType item, TimeItemType oldValue = null)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var oldItem = oldValue ?? item;
            int oldIndex = _sortedList.IndexOf(oldItem);
            if (oldIndex < 0)
            {
                throw new InvalidOperationException("The time item is not part of this collection.");
            }

            CheckForSameEventTime(item, oldItem);
            if (oldValue is null)
            {
                RepositionItem(item, oldIndex);
                return;
            }

            item.PreviousItem = oldItem.PreviousItem;
            item.NextItem = oldItem.NextItem;
            if (PredictNewPosition(item) == 0)
            {
                UnwirePropertyChangeEvent(oldItem);
                oldItem.DurationToNext = default;
                oldItem.DurationToPrevious = default;
                _sortedList[oldIndex] = item;
                WirePropertyChangeEvent(item);
                UpdateItem(item, oldIndex);
                OnPropertyChanged(_itemPropertyChangedEventArgs);
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, oldIndex));
                return;
            }

            RemoveAtCore(oldIndex);
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, oldIndex));
            AddWithPositionInfo(item);
        }

        /// <summary>
        /// Closes the old neighbor gap and relinks the same item at its sorted position.
        /// </summary>
        /// <remarks>
        /// The size does not change, so <see cref="Count"/> is not notified; the indexer is.
        /// A move event is raised only if the final index differs from the original one.
        /// </remarks>
        private void RepositionItem(TimeItemType item, int oldIndex)
        {
            var previous = oldIndex > 0 ? _sortedList[oldIndex - 1] : null;
            var next = oldIndex < _sortedList.Count - 1 ? _sortedList[oldIndex + 1] : null;
            LinkItems(previous, next);
            _sortedList.RemoveAt(oldIndex);
            int index = _sortedList.BinarySearch(item, _comparer);
            int newIndex = index < 0 ? -index - 1 : index;
            _sortedList.Insert(newIndex, item);
            UpdateItem(item, newIndex);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            if (newIndex != oldIndex)
            {
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex));
            }
        }

        private void CheckForSameEventTime(TimeItemType item, TimeItemType ignoredItem)
        {
            foreach (var existingItem in _sortedList)
            {
                if (ReferenceEquals(existingItem, ignoredItem))
                {
                    continue;
                }

                if (_comparer.Compare(existingItem, item) == 0)
                {
                    ThrowSamePointInTimeException(item);
                }
            }
        }

        /// <summary>
        /// Removes the entry matching the supplied item's timestamp and reconnects the remaining neighbors.
        /// </summary>
        /// <returns>True when a matching entry was removed; false when the timestamp search found no entry.</returns>
        /// <remarks>
        /// Matching uses the timestamp comparer, not reference equality. Count and indexer notifications
        /// precede the removal event, whose item argument is the supplied instance.
        /// </remarks>
        public bool Remove(TimeItemType item)
        {
            int index = _sortedList.BinarySearch(item, _comparer);
            if (index < 0)
            {
                return false;
            }

            RemoveAtCore(index);
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            return true;
        }

        /// <summary>
        /// Forwards a collection change synchronously; subscriber exceptions propagate to the caller.
        /// </summary>
        protected virtual void OnNotifyCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Forwards the supplied property notification without batching changes or restoring prior collection state.
        /// </summary>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        private void WirePropertyChangeEvent(TimeItemType item)
        {
            item.PropertyChanged += PropertyChangeEventHandlerProc;
        }

        private void UnwirePropertyChangeEvent(TimeItemType item)
        {
            item.PropertyChanged -= PropertyChangeEventHandlerProc;
        }

        /// <summary>
        /// Reorders items only for timestamp notifications, not for neighbor or duration notifications.
        /// </summary>
        /// <remarks>
        /// Notifications raised while relinking therefore do not trigger another sort.
        /// </remarks>
        private void PropertyChangeEventHandlerProc(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not null && (e.PropertyName ?? "") == EventTimePropertyName)
            {
                SetItemInternal((TimeItemType)sender);
            }
        }

        /// <summary>
        /// Enumerates the stored item instances in timestamp order without creating a snapshot.
        /// </summary>
        /// <remarks>Changing the backing list invalidates its active enumerators.</remarks>
        public IEnumerator<TimeItemType> GetEnumerator()
        {
            return _sortedList.GetEnumerator();
        }

        private IEnumerator GetUntypedEnumerator()
        {
            return _sortedList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetUntypedEnumerator();
        /// <summary>
        /// Gets the number of entries currently stored, including an entry whose timestamp is missing.
        /// </summary>
        public int Count
        {
            get
            {
                return _sortedList.Count;
            }
        }

        /// <summary>
        /// Unsubscribes from every item's property changes and empties the internal list.
        /// </summary>
        /// <remarks>
        /// Raises count, indexer and collection-reset notifications even when the list was already empty.
        /// Former items are not unlinked from one another here. The collection instance is retained.
        /// </remarks>
        public void Clear()
        {
            foreach (var timeItem in _sortedList)
                UnwirePropertyChangeEvent(timeItem);
            _sortedList.Clear();
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Searches by timestamp comparison rather than object identity; a null item returns false.
        /// </summary>
        public bool Contains(TimeItemType item)
        {
            return item is not null && _sortedList.BinarySearch(item, _comparer) >= 0;
        }

        /// <summary>
        /// Copies item references in sorted order into the destination array without cloning or removing entries.
        /// </summary>
        public void CopyTo(TimeItemType[] array, int arrayIndex)
        {
            _sortedList.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Locates an item by timestamp comparison; the supplied instance need not be the stored instance.
        /// </summary>
        /// <returns>
        /// The matching index, -1 for a null item, or the bitwise complement of the insertion index for another miss.
        /// </returns>
        public int IndexOf(TimeItemType item)
        {
            // A null item returns -1. Other misses retain the negative binary-search result.
            if (item is null)
            {
                return -1;
            }

            return _sortedList.BinarySearch(item, _comparer);
        }

        /// <summary>
        /// Searches for a timestamp without requiring the caller to supply a time-item instance.
        /// </summary>
        /// <returns>The matching index, or the bitwise complement of the insertion index when absent.</returns>
        public int IndexOf(DateTimeOffset dateOfTimeItem)
        {
            var tmpTimeItem = new TimeItemType()
            {
                EventTime = dateOfTimeItem
            };
            return _sortedList.BinarySearch(tmpTimeItem, _comparer);
        }

        /// <summary>
        /// Rejects positional insertion because timestamp order, rather than a caller-selected index, governs the list.
        /// </summary>
        /// <exception cref="NotImplementedException">Always thrown; use an Add overload for sorted insertion.</exception>
        public void Insert(int index, TimeItemType item)
        {
            throw new NotImplementedException("Inserting TimeItems at a certain position does not apply, since the order is determined by the TimeItem's UtcEventTime value. ");
        }

        /// <summary>
        /// Removes the item at the sorted index, clears its links and durations, and reconnects its former neighbors.
        /// </summary>
        /// <remarks>Count and indexer notifications precede the collection-removal event.</remarks>
        public void RemoveAt(int index)
        {
            var item = _sortedList[index];
            RemoveAtCore(index);
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        }

        /// <summary>
        /// Links the remaining neighbors and unsubscribes from the departing item's property changes.
        /// </summary>
        /// <remarks>
        /// Also clears that item's neighbor references and durations. The caller raises collection
        /// notifications; this step only updates the list contents and links.
        /// </remarks>
        private void RemoveAtCore(int index)
        {
            var removedItem = _sortedList[index];
            var previous = index > 0 ? _sortedList[index - 1] : null;
            var next = index < _sortedList.Count - 1 ? _sortedList[index + 1] : null;
            LinkItems(previous, next);
            UnwirePropertyChangeEvent(removedItem);
            removedItem.PreviousItem = null;
            removedItem.NextItem = null;
            removedItem.DurationToPrevious = default;
            removedItem.DurationToNext = default;
            _sortedList.RemoveAt(index);
        }

        /// <summary>
        /// Validates an untyped item and inserts its existing instance in timestamp order.
        /// </summary>
        /// <returns>The item's sorted insertion index, not necessarily the previous count.</returns>
        /// <exception cref="NullReferenceException">The supplied value is null.</exception>
        /// <exception cref="ArgumentException">The value has the wrong item type or conflicts with an existing timestamp.</exception>
        public int Add(object value)
        {
            if (value is null)
            {
                throw new NullReferenceException("Value for time item cannot be null.");
            }

            if (!(value is TimeItemType))
            {
                throw new ArgumentException("Value must be a time item.", nameof(value));
            }

            return AddWithPositionInfo((TimeItemType)value);
        }

        /// <summary>
        /// Checks a correctly typed value by timestamp; null and incompatible values are not contained.
        /// </summary>
        public bool Contains(object value)
        {
            return value is TimeItemType && Contains((TimeItemType)value);
        }

        /// <summary>
        /// Searches a correctly typed value by timestamp, returning -1 for null or incompatible values.
        /// </summary>
        /// <remarks>Other unsuccessful searches retain the negative binary-search insertion result.</remarks>
        public int IndexOf(object value)
        {
            if (!(value is TimeItemType))
            {
                return -1;
            }

            return IndexOf((TimeItemType)value);
        }

        /// <summary>
        /// Validates the untyped value before rejecting caller-selected insertion positions.
        /// </summary>
        /// <exception cref="NullReferenceException">The supplied value is null.</exception>
        /// <exception cref="ArgumentException">The supplied value is not the collection's item type.</exception>
        /// <exception cref="NotImplementedException">The value is valid, but positional insertion is unsupported.</exception>
        public void Insert(int index, object value)
        {
            if (value is null)
            {
                throw new NullReferenceException("Value for time item cannot be null.");
            }

            if (!(value is TimeItemType))
            {
                throw new ArgumentException("Value must be a time item.", nameof(value));
            }

            Insert(index, (TimeItemType)value);
        }

        /// <summary>
        /// Removes a correctly typed value by timestamp; null and incompatible values leave the collection unchanged.
        /// </summary>
        public void Remove(object value)
        {
            if (value is TimeItemType)
            {
                Remove((TimeItemType)value);
            }
        }

        /// <summary>
        /// Copies sorted item references through the backing list's untyped collection contract.
        /// </summary>
        /// <remarks>The backing list validates destination rank, element compatibility and available capacity.</remarks>
        public void CopyTo(Array array, int index)
        {
            ((ICollection)_sortedList).CopyTo(array, index);
        }

        /// <summary>
        /// Returns false because items can be added, replaced and removed; positional insertion remains unsupported.
        /// </summary>
        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns false because sorted additions and removals can change the collection's size.
        /// </summary>
        public bool IsFixedSize
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns false; the collection does not automatically synchronize access between threads.
        /// </summary>
        public bool IsSynchronized
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Exposes the backing list's synchronization object for caller-managed locking.
        /// </summary>
        /// <remarks>Reading this property does not acquire a lock or make notifications thread-safe.</remarks>
        public object SyncRoot
        {
            get
            {
                return ((ICollection)_sortedList).SyncRoot;
            }
        }

        /// <summary>
        /// Gets the stored instance at a sorted index, or replaces it while maintaining timestamp uniqueness and links.
        /// </summary>
        /// <remarks>
        /// Replacement can move to a different index when its timestamp changes. Null replacements are rejected;
        /// the operation updates property subscriptions and sends the corresponding collection notifications.
        /// </remarks>
        public TimeItemType this[int index]
        {
            get
            {
                return _sortedList[index];
            }

            set
            {
                if (value is null)
                {
                    throw new NullReferenceException("Value for time item cannot be null.");
                }

                var oldValue = _sortedList[index];
                SetItemInternal(value, oldValue);
            }
        }

        /// <summary>
        /// Looks up an exact timestamp and returns null when no item has that timestamp.
        /// </summary>
        public TimeItemType this[DateTimeOffset dateOfTimeItem]
        {
            get
            {
                var tmpTimeItem = new TimeItemType()
                {
                    EventTime = dateOfTimeItem
                };
                int index = _sortedList.BinarySearch(tmpTimeItem, _comparer);
                if (index >= 0)
                {
                    return _sortedList[index];
                }

                return null;
            }
        }

        private object get_UntypedItem(int index)
        {
            return this[index];
        }

        private void set_UntypedItem(int index, object value)
        {
            if (value is null)
            {
                throw new NullReferenceException("Value for time item cannot be null.");
            }

            if (!(value is TimeItemType))
            {
                throw new ArgumentException("Value must be a time item.", nameof(value));
            }

            this[index] = (TimeItemType)value;
        }

        /// <summary>
        /// Exposes the sorted indexer through IList, rejecting null and incompatible replacement values.
        /// </summary>
        object IList.this[int index] { get => get_UntypedItem(index); set => set_UntypedItem(index, value); }
    }
}