using System.Reflection;
using System.Windows;

namespace TaskOTime.Validation;

internal static class MainDataComposition
{
    internal sealed record Contract(
        Type WindowType, Type ViewModelType, Type TenantType,
        Type AdminType, Type UsersType, Type BookingsType,
        Type? InteractionType = null, Type? InteractionContract = null);

    internal static Contract Resolve(Assembly viewModels, Assembly services)
    {
        var modernWindow = viewModels.GetType("TaskOTime.ViewModel.Views.MainDataWindow");
        var modernVm = viewModels.GetType("TaskOTime.ViewModel.ViewModels.MainDataViewModel");
        var legacyWindow = viewModels.GetType("TaskOTime.ViewModel.Views.MasterDataWindow");
        var legacyVm = viewModels.GetType("TaskOTime.ViewModel.ViewModels.MasterDataViewModel");
        var modern = modernWindow is not null || modernVm is not null;
        if (modern && (legacyWindow is not null || legacyVm is not null))
            throw new InvalidOperationException("Ambiguous maintenance layout: both MainData and MasterData types exist.");
        var window = modern ? modernWindow : legacyWindow;
        var vm = modern ? modernVm : legacyVm;
        if (window is null || vm is null)
            throw new InvalidOperationException("Unsupported or incomplete maintenance window/viewmodel layout.");
        var interaction = modern ? viewModels.GetType("TaskOTime.ViewModel.Views.MaintenanceInteraction", true) : null;
        var interactionContracts = interaction?.GetInterfaces().Where(type =>
            type.Name == "IMaintenanceInteraction" && type.Assembly == viewModels).ToArray();
        if (modern && interactionContracts!.Length != 1)
            throw new InvalidOperationException("MaintenanceInteraction must implement one ViewModel-owned IMaintenanceInteraction contract.");

        return new Contract(window, vm,
            services.GetType("TaskOTime.AppServer.Models.TenantDto", true)!,
            services.GetType(modern
                ? "TaskOTime.AppServer.Services.IAdminMainDataService"
                : "TaskOTime.AppServer.Services.IAdminMasterDataService", true)!,
            services.GetType("TaskOTime.AppServer.Services.IUserAdministrationService", true)!,
            services.GetType("TaskOTime.AppServer.Services.ITimeBookingService", true)!,
            interaction, modern ? interactionContracts![0] : null);
    }

    internal static Window Create(Contract contract, object tenant, Guid user,
        object admin, object users, object bookings, ICollection<Window> cleanup)
    {
        var modern = contract.InteractionType is not null;
        if (!typeof(Window).IsAssignableFrom(contract.WindowType) ||
            modern != (contract.InteractionContract is not null) ||
            (modern && (!contract.InteractionContract!.IsInterface ||
                !contract.InteractionContract.IsAssignableFrom(contract.InteractionType))))
            throw new InvalidOperationException("Unsupported maintenance window/interaction contract.");
        var windowConstructor = ExactConstructor(contract.WindowType, Type.EmptyTypes);
        var interactionConstructor = modern ? ExactConstructor(contract.InteractionType!, new[] { typeof(Window) }) : null;
        var signature = modern
            ? new[] { contract.TenantType, typeof(Guid), contract.AdminType, contract.UsersType, contract.BookingsType, typeof(int), contract.InteractionContract! }
            : new[] { contract.WindowType, contract.TenantType, typeof(Guid), contract.AdminType, contract.UsersType, contract.BookingsType, typeof(int) };
        var vmConstructor = ExactConstructor(contract.ViewModelType, signature);
        if (!contract.TenantType.IsInstanceOfType(tenant) || !contract.AdminType.IsInstanceOfType(admin) ||
            !contract.UsersType.IsInstanceOfType(users) || !contract.BookingsType.IsInstanceOfType(bookings))
            throw new InvalidOperationException("Maintenance services do not implement the declared constructor contract.");

        var window = (Window)windowConstructor.Invoke(null);
        cleanup.Add(window);
        var args = modern
            ? new[] { tenant, user, admin, users, bookings, 1, interactionConstructor!.Invoke(new object[] { window }) }
            : new[] { window, tenant, user, admin, users, bookings, 1 };
        window.DataContext = vmConstructor.Invoke(args);
        if (window.DataContext is null || window.DataContext.GetType() != contract.ViewModelType)
            throw new InvalidOperationException("Maintenance DataContext was not composed correctly.");
        return window;
    }

    private static ConstructorInfo ExactConstructor(Type type, Type[] signature)
    {
        var constructors = type.GetConstructors().Where(constructor =>
            constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(signature)).ToArray();
        return constructors.Length == 1 ? constructors[0] :
            throw new InvalidOperationException("Unsupported constructor signature for " + type.FullName + ".");
    }
}
