using System;

namespace ActiveDevelop.TimeTrackingServices
{
    /// <summary>
    /// Stores a time item with change notifications, nullable timestamps and references to its neighbors.
    /// The item does not maintain a sorted list; its containing collection may change the neighbor links.
    /// </summary>
    /// <remarks>
    /// Missing action flags are not equivalent to false: they can cause a calculation to be skipped.
    /// The collection's timestamp-distance calculation is separate from this flag-dependent logic.
    /// Property notifications are raised immediately and do not form an atomic change transaction.
    /// </remarks>
    public class TimeItemBase : ObservableObject, ITimeItem<Guid>
    {

        public event EventHandler EventTimeChanged;
        public event EventHandler ActionTargetInternalChanged;

        private TimeSpan? _durationToNext;
        private TimeSpan? _durationToPrevious;
        private DateTimeOffset? _eventTime;
        private Guid _idTimeItem;
        private bool? _isStartAction;
        private bool? _isEndAction;
        private TimeItemState _itemState;
        private DateTimeOffset? _lastChanged;
        private bool _isNotTimeItem;
        private ITimeItem<Guid> _processEndpointItem;
        private ITimeItem<Guid> _previousItem;
        private ITimeItem<Guid> _nextItem;
        private DateTimeOffset? _dateCreated;

        public Guid IDTimeItem
        {
            get
            {
                return _idTimeItem;
            }
            set
            {
                SetProperty(ref _idTimeItem, value);
            }
        }

        public TimeSpan? DurationToNext
        {
            get
            {
                return _durationToNext;
            }
            set
            {
                SetProperty(ref _durationToNext, value);
            }
        }

        public TimeSpan? DurationToPrevious
        {
            get
            {
                return _durationToPrevious;
            }
            set
            {
                SetProperty(ref _durationToPrevious, value);
            }
        }

        /// <summary>
        /// Raises the property change before the separate timestamp event and duration calculation.
        /// </summary>
        /// <remarks>
        /// If an exception occurs, only the timestamp field is restored. Subscriber side effects are
        /// not rolled back, and no additional notification announces the restoration.
        /// </remarks>
        public DateTimeOffset? EventTime
        {
            get
            {
                return _eventTime;
            }
            set
            {
                var oldEventTime = _eventTime;

                try
                {
                    if (SetProperty(ref _eventTime, value))
                    {
                        OnEventTimeChanged();
                        CalculateDurationToLinkedItems();
                    }
                }
                catch
                {
                    _eventTime = oldEventTime;
                    throw;
                }
            }
        }

        protected virtual void OnEventTimeChanged()
        {
            EventTimeChanged?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnActionTargetInternalChanged()
        {
            ActionTargetInternalChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool? IsStartAction
        {
            get
            {
                return _isStartAction;
            }
            set
            {
                SetProperty(ref _isStartAction, value);
                CalculateDurationToLinkedItems();
            }
        }

        public bool? IsEndAction
        {
            get
            {
                return _isEndAction;
            }
            set
            {
                SetProperty(ref _isEndAction, value);
                CalculateDurationToLinkedItems();
            }
        }

        public TimeItemState ItemState
        {
            get
            {
                return _itemState;
            }
            set
            {
                SetProperty(ref _itemState, value);
            }
        }

        public DateTimeOffset? LastChanged
        {
            get
            {
                return _lastChanged;
            }
            set
            {
                SetProperty(ref _lastChanged, value);
            }
        }

        public DateTimeOffset? DateCreated
        {
            get
            {
                return _dateCreated;
            }
            set
            {
                SetProperty(ref _dateCreated, value);
            }
        }

        public ITimeItem<Guid> NextItem
        {
            get
            {
                return _nextItem;
            }
            set
            {
                SetProperty(ref _nextItem, value);
                CalculateDurationToLinkedItems();
            }
        }

        public ITimeItem<Guid> PreviousItem
        {
            get
            {
                return _previousItem;
            }
            set
            {
                SetProperty(ref _previousItem, value);
                CalculateDurationToLinkedItems();
            }
        }

        public ITimeItem<Guid> ProcessEndpointItem
        {
            get
            {
                return _processEndpointItem;
            }
            set
            {
                SetProperty(ref _processEndpointItem, value);
            }
        }

        public bool IsNotTimeItem
        {
            get
            {
                return _isNotTimeItem;
            }
            set
            {
                SetProperty(ref _isNotTimeItem, value);
            }
        }

        /// <summary>
        /// Calculates each direction only when its neighbor and the corresponding action flag exist.
        /// </summary>
        /// <remarks>
        /// Within either calculation, a start or end action clears the duration, as does a missing timestamp.
        /// If the neighbor or required flag is absent, the existing duration in that direction is retained.
        /// </remarks>
        protected virtual void CalculateDurationToLinkedItems()
        {
            if (PreviousItem is not null && IsStartAction.HasValue)
            {
                if (IsStartAction.Value || (IsEndAction ?? false) || !EventTime.HasValue || !PreviousItem.EventTime.HasValue)
                {
                    DurationToPrevious = default;
                }
                else
                {
                    DurationToPrevious = EventTime.Value - PreviousItem.EventTime.Value;
                }
            }

            if (NextItem is not null && IsEndAction.HasValue)
            {
                if (IsEndAction.Value || (IsStartAction ?? false) || !EventTime.HasValue || !NextItem.EventTime.HasValue)
                {
                    DurationToNext = default;
                }
                else
                {
                    DurationToNext = NextItem.EventTime.Value - EventTime.Value;
                }
            }
        }

        public override string ToString()
        {
            string current = EventTime.HasValue ? EventTime.Value.ToString("yy-MM-dd HH:mm") : "##-##-## ##:##";
            string previous = PreviousItem is not null && PreviousItem.EventTime.HasValue ? PreviousItem.EventTime.Value.ToString("yy-MM-dd HH:mm") : "##-##-## ##:##";
            string next = NextItem is not null && NextItem.EventTime.HasValue ? NextItem.EventTime.Value.ToString("yy-MM-dd HH:mm") : "##-##-## ##:##";

            return $"--> {current} (<-- {previous} --> {next}";
        }
    }
}