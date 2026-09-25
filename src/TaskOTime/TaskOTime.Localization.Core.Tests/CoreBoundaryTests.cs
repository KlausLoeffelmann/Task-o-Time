using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.ViewModel.Base;
using TaskOTime.ViewModel.Localization;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.Localization.Core.Tests;

[TestClass]
[DoNotParallelize]
public class CoreBoundaryTests
{
    [TestCleanup]
    public void ResetCulture() => LocalizationService.Current.SetCulture("en");

    [TestMethod]
    public void ProductionProviderAndModelsRunWithoutWindowsDesktop()
    {
        var service = LocalizationService.Current;
        Assert.IsInstanceOfType<ResourceManagerStringLocalizer>(service.Strings);
        service.SetCulture("de-AT");
        Assert.AreEqual("de-AT", service.CultureName);
        Assert.AreEqual("Anmelden", service["Login_SignIn"]);
        var model = new TimeEntryViewModel();
        Assert.AreEqual("Zeitbuchung", model.MarkerLabel);
        var task = new TaskItemViewModel("Customer task", "", "")
        {
            DueDate = new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.FromHours(2))
        };
        var dialog = new DialogShellViewModel(new LocalizedMessage("Report_Day_Title"),
            new LocalizedMessage("Report_Day_Heading"), new LocalizedMessage("Report_Day_Lead", task.DueDate.Value));
        service.SetCulture("es");
        Assert.AreEqual("Registro de tiempo", model.MarkerLabel);
        Assert.AreEqual(task.DueDate.Value.ToString("d", service.Culture), task.DueText);
        Assert.AreEqual("Análisis diario", dialog.Title);
        StringAssert.Contains(dialog.LeadText, task.DueText);
        Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name is "PresentationFramework" or "PresentationCore" or "WindowsBase"));
    }

    [TestMethod]
    public void HostContextEnforcesThreadAccessAndRestoresNestedRegistrations()
    {
        var service = LocalizationService.Current;
        var context = new ThreadContext();
        using (var outer = service.UseChangeContext(context))
        {
            using (var inner = service.UseChangeContext(context))
            {
                Assert.ThrowsException<InvalidOperationException>(() => outer.Dispose());
                Assert.ThrowsException<InvalidOperationException>(() =>
                    Task.Run(() => service.SetCulture("de")).GetAwaiter().GetResult());
                service.SetCulture("nl");
            }
            Assert.AreEqual("nl", service.CultureName);
        }
        Task.Run(() => service.SetCulture("es")).GetAwaiter().GetResult();
        Assert.AreEqual("es", service.CultureName);
    }

    [TestMethod]
    public void DisposedRegistrationDoesNotRetainItsHost()
    {
        var (registration, weak) = RegisterAndDisposeContext();
        Collect();
        Assert.IsFalse(weak.IsAlive);
        GC.KeepAlive(registration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable, WeakReference) RegisterAndDisposeContext()
    {
        var context = new ThreadContext();
        var weak = new WeakReference(context);
        var registration = LocalizationService.Current.UseChangeContext(context);
        registration.Dispose();
        return (registration, weak);
    }

    [TestMethod]
    public void WeakSubscriptionsDeliverAndDetachWithoutRetainingRecipients()
    {
        var source = new NotificationSource();
        var recipient = new Recipient();
        using (WeakNotifications.SubscribePropertyChanged(source, recipient, static target => target.Count++))
        using (WeakNotifications.SubscribeCollectionChanged(source, recipient, static target => target.Count++))
        {
            source.Raise();
            Assert.AreEqual(2, recipient.Count);
        }
        source.Raise();
        Assert.AreEqual(2, recipient.Count);
        Assert.AreEqual(0, source.HandlerCount);

        var weak = SubscribeDiscarded(source);
        Collect();
        Assert.IsFalse(weak.IsAlive);
        source.Raise();
        Assert.AreEqual(0, source.HandlerCount);
    }

    [TestMethod]
    public void LocalizedModelNotifiesWithoutRetainingIt()
    {
        var model = new LabelModel();
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        LocalizationService.Current.SetCulture("nl");
        Assert.AreEqual("Aanmelden", model.Label);
        Assert.IsTrue(notifications > 0);
        var weak = CreateDiscardedModel();
        Collect();
        Assert.IsFalse(weak.IsAlive);
        LocalizationService.Current.SetCulture("de");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDiscardedModel() => new WeakReference(new LabelModel());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeDiscarded(NotificationSource source)
    {
        var recipient = new Recipient();
        WeakNotifications.SubscribePropertyChanged(source, recipient, static target => target.Count++);
        WeakNotifications.SubscribeCollectionChanged(source, recipient, static target => target.Count++);
        return new WeakReference(recipient);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ThreadContext : ICultureChangeContext
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        public void VerifyAccess()
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("The culture change is on the wrong host thread.");
        }
    }

    private sealed class Recipient { public int Count; }
    private sealed class LabelModel : LocalizedViewModelBase { public string Label => Text("Login_SignIn"); }

    private sealed class NotificationSource : INotifyPropertyChanged, INotifyCollectionChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public int HandlerCount => (PropertyChanged?.GetInvocationList().Length ?? 0) +
            (CollectionChanged?.GetInvocationList().Length ?? 0);
        public void Raise()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Value"));
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
