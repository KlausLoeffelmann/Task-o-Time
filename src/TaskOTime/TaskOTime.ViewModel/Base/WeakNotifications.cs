using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;

namespace TaskOTime.ViewModel.Base
{
    /// <summary>
    /// Observes model notifications without retaining the recipient. Use a non-capturing callback.
    /// A dead recipient is detached on the next notification; Dispose detaches immediately.
    /// </summary>
    public static class WeakNotifications
    {
        public static IDisposable SubscribePropertyChanged<T>(INotifyPropertyChanged source, T target, Action<T> callback) where T : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var subscription = new Subscription<T>(target, callback);
            PropertyChangedEventHandler handler = (_, _) => subscription.Notify();
            subscription.Unsubscribe = () => source.PropertyChanged -= handler;
            source.PropertyChanged += handler;
            return subscription;
        }

        public static IDisposable SubscribeCollectionChanged<T>(INotifyCollectionChanged source, T target, Action<T> callback) where T : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var subscription = new Subscription<T>(target, callback);
            NotifyCollectionChangedEventHandler handler = (_, _) => subscription.Notify();
            subscription.Unsubscribe = () => source.CollectionChanged -= handler;
            source.CollectionChanged += handler;
            return subscription;
        }

        private sealed class Subscription<T> : IDisposable where T : class
        {
            private readonly WeakReference<T> _target;
            private readonly Action<T> _callback;
            public Action Unsubscribe;

            public Subscription(T target, Action<T> callback)
            {
                _target = new WeakReference<T>(target ?? throw new ArgumentNullException(nameof(target)));
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            public void Notify()
            {
                if (Volatile.Read(ref Unsubscribe) == null) return;
                if (_target.TryGetTarget(out var target)) _callback(target);
                else Dispose();
            }

            public void Dispose() => Interlocked.Exchange(ref Unsubscribe, null)?.Invoke();
        }
    }
}
