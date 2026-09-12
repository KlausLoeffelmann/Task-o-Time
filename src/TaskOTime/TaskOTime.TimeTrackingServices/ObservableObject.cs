using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ActiveDevelop.TimeTrackingServices
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        // '' <summary>
        // ''  vergelijkt met de standaardgelijkheid en meldt alleen wanneer de opgeslagen waarde verandert.
        // '' </summary>
        // '' <returns>
        // ''  waar nadat opslag en melding zijn uitgevoerd; onwaar wanneer beide waarden gelijk zijn.
        // '' </returns>
        // '' <remarks>
        // ''  de opslag gaat aan de melding vooraf.  een uitzondering uit een afnemer wordt doorgegeven,
        // ''  zonder dat deze helper de opgeslagen waarde terugzet.
        // '' </remarks>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // '' <summary>
        // ''  geeft de eigenschapsnaam direct door, zonder zelf gelijkheid of afhankelijkheden te toetsen.
        // '' </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }
    }
}