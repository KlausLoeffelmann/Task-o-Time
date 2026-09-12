using System;

namespace ActiveDevelop.TimeTrackingServices
{
    // '' <summary>
    // ''  bewaart een tijdregel met wijzigingsmeldingen, optionele tijdstippen en verwijzingen naar buren.
    // ''  de regel beheert geen eigen sorteerlijst.  een omringende collectie kan de buurrelaties wijzigen.
    // '' </summary>
    // '' <remarks>
    // ''  ontbrekende actievlaggen zijn niet zonder meer gelijk aan onwaar: zij kunnen een berekening
    // ''  overslaan.  de collectieberekening van tijdsafstanden staat los van deze vlagafhankelijke logica.
    // ''  eigenschapsmeldingen worden meteen doorgegeven en vormen geen gezamenlijke wijzigingstransactie.
    // '' </remarks>
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

        // '' <summary>
        // ''  meldt een gewijzigd tijdstip voordat de afzonderlijke tijdstipmelding en duurberekening volgen.
        // '' </summary>
        // '' <remarks>
        // ''  bij een uitzondering wordt alleen het tijdstipveld teruggezet.  reeds uitgevoerde reacties
        // ''  van afnemers worden niet teruggedraaid en er volgt geen extra herstelmelding.
        // '' </remarks>
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

        // '' <summary>
        // ''  berekent per richting alleen wanneer de betreffende buur en de bijbehorende actievlag bestaan.
        // '' </summary>
        // '' <remarks>
        // ''  binnen zo'n berekening wist een begin- of eindactie de duur, net als een ontbrekend tijdstip.
        // ''  ontbreekt de buur of de benodigde vlag, dan blijft de bestaande duur in die richting staan.
        // '' </remarks>
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