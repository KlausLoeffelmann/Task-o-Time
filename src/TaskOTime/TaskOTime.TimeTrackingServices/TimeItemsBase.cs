using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ActiveDevelop.TimeTrackingServices
{
    // '' <summary>
    // ''  bewaart tijdregels in een eigen gesorteerde lijst en meldt wijzigingen aan afnemers.
    // ''  implementeert zelf de lijst- en collectiecontracten; dit is geen afgeleide van
    // ''  <see cref="System.Collections.ObjectModel.ObservableCollection(Of TimeItemType)"/>.
    // '' </summary>
    // '' <remarks>
    // ''  de volgorde volgt <see cref="ITimeItem(Of IndexType).EventTime"/>, niet de invoegvolgorde.
    // ''  sorteren en herladen kunnen hetzelfde collectieobject blijven gebruiken.  de koppelingen
    // ''  tussen naburige regels en de bijbehorende meldingen worden hier afzonderlijk onderhouden.
    // '' </remarks>
    public class TimeItemsBase<IndexType, TimeItemType> : IEnumerable<TimeItemType>, ICollection<TimeItemType>, IList<TimeItemType>, IList, INotifyCollectionChanged, INotifyPropertyChanged where IndexType : struct, IComparable<IndexType> where TimeItemType : class, ITimeItem<IndexType>, INotifyPropertyChanged, new()
    {
        private const string EventTimePropertyName = "EventTime";
        private readonly PropertyChangedEventArgs _countPropertyChangedEventArgs;
        private readonly PropertyChangedEventArgs _itemPropertyChangedEventArgs = new PropertyChangedEventArgs("Item[]");
        private readonly List<TimeItemType> _sortedList = new List<TimeItemType>();
        private readonly TimeItemByEventTimeComparer<IndexType, TimeItemType> _comparer = new TimeItemByEventTimeComparer<IndexType, TimeItemType>();
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        public TimeItemsBase()
        {
            _countPropertyChangedEventArgs = new PropertyChangedEventArgs(nameof(Count));
        }

        private void AddRangeSuspended(IEnumerable<TimeItemType> range)
        {
            foreach (var timeItem in range)
                Add(timeItem);
        }

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

        // '' <summary>
        // ''  vergelijkt alleen met de gekoppelde buren en geeft een richting, geen invoegindex.
        // '' </summary>
        // '' <remarks>
        // ''  vergelijkingen met ontbrekende regels of tijdstippen worden overgeslagen.  nul bewijst dus niet
        // ''  dat een tijdstip uniek is; die controle staat los van deze voorspelling.
        // '' </remarks>
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

        // '' <summary>
        // ''  voegt een tijdregel volgens de tijdvolgorde in en geeft de invoegpositie terug.
        // '' </summary>
        // '' <remarks>
        // ''  een ontbrekend object wordt geweigerd.  een object zonder tijdstip mag wel vooraan staan.
        // ''  gelijke tijdstippen worden geweigerd, ook als beide tijdstippen ontbreken.
        // ''  na invoegen volgen meldingen voor <see cref="Count"/> en <see cref="Item(Integer)"/>.
        // ''  de collectiemelding bevat daarna het toegevoegde object en zijn positie.
        // '' </remarks>
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
            // de teller beschrijft de omvang; de indexermelding maakt gewijzigde posities zichtbaar.
            // de collectiemelding draagt daarnaast het toegevoegde object en zijn positie.
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

        // '' <summary>
        // ''  legt beide richtingen van een buurrelatie vast met dezelfde berekende tijdsafstand.
        // '' </summary>
        // '' <remarks>
        // ''  ook aan een uiteinde wordt de aanwezige buur bijgewerkt.  daar ontbreken zowel
        // ''  de verwijzing naar buiten als de duur; een ontbrekende duur is niet hetzelfde als nul.
        // '' </remarks>
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

        // '' <summary>
        // ''  berekent het tijdsverschil, of geen waarde als een buur of een tijdstip ontbreekt.
        // '' </summary>
        // '' <remarks>
        // ''  deze collectieberekening leest geen actievlaggen.  zij beschrijft de afstand tussen
        // ''  twee tijdstippen, niet zelfstandig de betekenis van een boeking of onderbreking.
        // '' </remarks>
        private static TimeSpan? CalculateDuration(TimeItemType previous, TimeItemType next)
        {
            if (previous is null || next is null || !previous.EventTime.HasValue || !next.EventTime.HasValue)
            {
                return default;
            }

            return next.EventTime.Value - previous.EventTime.Value;
        }

        // '' <summary>
        // ''  controleert tijdstipconflicten voordat een bestaande regel wordt verplaatst of vervangen.
        // '' </summary>
        // '' <param name="oldValue">
        // ''  de te vervangen regel; zonder deze waarde wordt het bestaande object opnieuw geplaatst.
        // '' </param>
        // '' <remarks>
        // ''  vervanging op dezelfde positie meldt een vervanging en alleen de indexerwijziging.
        // ''  bij een andere positie wordt eerst verwijderd en daarna toegevoegd, met beide tellermeldingen.
        // '' </remarks>
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

        // '' <summary>
        // ''  sluit de oude buurrelatie en koppelt hetzelfde object opnieuw op zijn gesorteerde positie.
        // '' </summary>
        // '' <remarks>
        // ''  de omvang verandert niet, dus <see cref="Count"/> wordt niet gemeld.  de indexer wel;
        // ''  een verplaatsingsmelding volgt alleen wanneer de uiteindelijke index anders is.
        // '' </remarks>
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

        protected virtual void OnNotifyCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

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

        // '' <summary>
        // ''  herordent alleen bij de melding voor het tijdstip, niet bij meldingen voor buren of duren.
        // '' </summary>
        // '' <remarks>
        // ''  de tijdens het herkoppelen ontstane meldingen starten daardoor geen nieuwe sortering.
        // '' </remarks>
        private void PropertyChangeEventHandlerProc(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not null && (e.PropertyName ?? "") == EventTimePropertyName)
            {
                SetItemInternal((TimeItemType)sender);
            }
        }

        public IEnumerator<TimeItemType> GetEnumerator()
        {
            return _sortedList.GetEnumerator();
        }

        private IEnumerator GetUntypedEnumerator()
        {
            return _sortedList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetUntypedEnumerator();
        public int Count
        {
            get
            {
                return _sortedList.Count;
            }
        }

        // '' <summary>
        // ''  verwijdert alle abonnementen op eigenschapswijzigingen en leegt de interne lijst.
        // '' </summary>
        // '' <remarks>
        // ''  meldt teller, indexer en een volledige verversing, ook als de lijst al leeg was.
        // ''  de oude objecten worden hier niet onderling losgekoppeld.  de collectie zelf blijft bestaan.
        // '' </remarks>
        public void Clear()
        {
            foreach (var timeItem in _sortedList)
                UnwirePropertyChangeEvent(timeItem);
            _sortedList.Clear();
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // '' <summary>
        // ''  zoekt op tijdstipvergelijking, niet op objectidentiteit; een ontbrekend object geeft onwaar.
        // '' </summary>
        public bool Contains(TimeItemType item)
        {
            return item is not null && _sortedList.BinarySearch(item, _comparer) >= 0;
        }

        public void CopyTo(TimeItemType[] array, int arrayIndex)
        {
            _sortedList.CopyTo(array, arrayIndex);
        }

        public int IndexOf(TimeItemType item)
        {
            // een ontbrekend object krijgt min één.  een andere misser behoudt het negatieve zoekresultaat.
            if (item is null)
            {
                return -1;
            }

            return _sortedList.BinarySearch(item, _comparer);
        }

        public int IndexOf(DateTimeOffset dateOfTimeItem)
        {
            var tmpTimeItem = new TimeItemType()
            {
                EventTime = dateOfTimeItem
            };
            return _sortedList.BinarySearch(tmpTimeItem, _comparer);
        }

        public void Insert(int index, TimeItemType item)
        {
            throw new NotImplementedException("Inserting TimeItems at a certain position does not apply, since the order is determined by the TimeItem's UtcEventTime value. ");
        }

        public void RemoveAt(int index)
        {
            var item = _sortedList[index];
            RemoveAtCore(index);
            OnPropertyChanged(_countPropertyChangedEventArgs);
            OnPropertyChanged(_itemPropertyChangedEventArgs);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        }

        // '' <summary>
        // ''  verbindt de overblijvende buren en verwijdert het abonnement van de vertrekkende regel.
        // '' </summary>
        // '' <remarks>
        // ''  wist ook de buurverwijzingen en duren van die regel.  de aanroeper verzorgt de meldingen
        // ''  over de gewijzigde collectie; deze stap past alleen de inhoud en koppelingen aan.
        // '' </remarks>
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

        public bool Contains(object value)
        {
            return value is TimeItemType && Contains((TimeItemType)value);
        }

        public int IndexOf(object value)
        {
            if (!(value is TimeItemType))
            {
                return -1;
            }

            return IndexOf((TimeItemType)value);
        }

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

        public void Remove(object value)
        {
            if (value is TimeItemType)
            {
                Remove((TimeItemType)value);
            }
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)_sortedList).CopyTo(array, index);
        }

        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        public bool IsFixedSize
        {
            get
            {
                return false;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return false;
            }
        }

        public object SyncRoot
        {
            get
            {
                return ((ICollection)_sortedList).SyncRoot;
            }
        }

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

        // '' <summary>
        // ''  zoekt een exact tijdstip en geeft geen object terug wanneer dat tijdstip niet voorkomt.
        // '' </summary>
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

        object IList.this[int index] { get => get_UntypedItem(index); set => set_UntypedItem(index, value); }
    }
}