using System.Windows;

namespace TaskOTime.Validation;

internal static class MainDataCompositionSelfTest
{
    internal static void Run()
    {
        var cleanup = new List<Window>();
        try
        {
            var tenant = new Tenant();
            var services = new Services();
            var user = Guid.NewGuid();
            var legacy = new MainDataComposition.Contract(typeof(TestWindow), typeof(LegacyVm),
                typeof(Tenant), typeof(IAdmin), typeof(IUsers), typeof(IBookings));
            var modern = legacy with
            {
                ViewModelType = typeof(ModernVm),
                InteractionType = typeof(Interaction),
                InteractionContract = typeof(IInteraction)
            };
            var oldWindow = MainDataComposition.Create(legacy, tenant, user, services, services, services, cleanup);
            var newWindow = MainDataComposition.Create(modern, tenant, user, services, services, services, cleanup);
            if (oldWindow.DataContext is not LegacyVm oldVm || !ReferenceEquals(oldVm.Owner, oldWindow) ||
                newWindow.DataContext is not ModernVm newVm || !ReferenceEquals(newVm.Interaction.Owner, newWindow) ||
                newVm.User != user || !ReferenceEquals(newVm.Tenant, tenant) || newVm.Tab != 1)
                throw new InvalidOperationException("Legacy/modern maintenance composition self-test failed.");

            var rejected = 0;
            foreach (var invalid in new[]
            {
                modern with { ViewModelType = typeof(UnexpectedVm) },
                modern with { InteractionType = typeof(object) },
                modern with { InteractionContract = null },
                legacy with { WindowType = typeof(object) }
            })
            {
                try { MainDataComposition.Create(invalid, tenant, user, services, services, services, cleanup); }
                catch (InvalidOperationException) { rejected++; }
            }
            try { MainDataComposition.Resolve(typeof(MainDataCompositionSelfTest).Assembly, typeof(MainDataCompositionSelfTest).Assembly); }
            catch (InvalidOperationException) { rejected++; }
            if (rejected != 5 || cleanup.Count != 2)
                throw new InvalidOperationException("Unexpected maintenance contracts were not rejected before construction.");
        }
        finally
        {
            foreach (var window in cleanup) window.Close();
        }
    }

    private sealed class Tenant { }
    private interface IAdmin { }
    private interface IUsers { }
    private interface IBookings { }
    private interface IInteraction { Window Owner { get; } }
    private sealed class Services : IAdmin, IUsers, IBookings { }
    private sealed class TestWindow : Window { public TestWindow() { } }
    private sealed class Interaction : IInteraction
    {
        public Interaction(Window owner) => Owner = owner;
        public Window Owner { get; }
    }
    private sealed class LegacyVm
    {
        public LegacyVm(TestWindow owner, Tenant tenant, Guid user, IAdmin admin, IUsers users, IBookings bookings, int tab) => Owner = owner;
        internal Window Owner { get; }
    }
    private sealed class ModernVm
    {
        public ModernVm(Tenant tenant, Guid user, IAdmin admin, IUsers users, IBookings bookings, int tab, IInteraction interaction)
        {
            Tenant = tenant;
            User = user;
            Tab = tab;
            Interaction = interaction;
        }
        internal Tenant Tenant { get; }
        internal Guid User { get; }
        internal int Tab { get; }
        internal IInteraction Interaction { get; }
    }
    private sealed class UnexpectedVm
    {
        public UnexpectedVm(Tenant tenant, Guid user, IAdmin admin, IUsers users, IBookings bookings, int tab, object interaction) { }
    }
}
