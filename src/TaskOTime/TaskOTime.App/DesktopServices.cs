using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity.Core.EntityClient;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.DataLayer;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App
{
    public sealed class DesktopServices
    {
        public IAuthenticationService Authentication { get; private set; }
        public IAdminMasterDataService Admin { get; private set; }
        public IUserAdministrationService Users { get; private set; }
        public ITimeBookingService Bookings { get; private set; }
        private string customModeDescription;
        public string ModeDescriptionKey { get; private set; }
        public string ModeDescription
        {
            get => ModeDescriptionKey == null ? customModeDescription : ViewModel.Localization.LocalizationService.Current[ModeDescriptionKey];
            private set => customModeDescription = value;
        }
        private Func<TaskOTimeContext> contextFactory;
        private TenantDto configuredTenant;

        public static DesktopServices Create()
        {
            var mode = Environment.GetEnvironmentVariable("TASKOTIME_MODE") ?? "Demo";
            Func<TaskOTimeContext> factory;
            string description;
            if (string.Equals(mode, "Demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "LocalDemo", StringComparison.OrdinalIgnoreCase))
            {
                factory = TaskOTimeContextFactory.Create;
                description = "Login_ModeDemo";
            }
            else if (string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase))
            {
                var connection = Environment.GetEnvironmentVariable("TASKOTIME_CONNECTION_STRING");
                if (string.IsNullOrWhiteSpace(connection))
                    throw new InvalidOperationException("Production benötigt TASKOTIME_CONNECTION_STRING für eine eingerichtete Task-o-Time-Datenbank.");
                var entityConnection = ResolveProductionConnection(connection);
                factory = () => TaskOTimeContextFactory.Create(entityConnection);
                description = "Login_ModeProduction";
            }
            else
            {
                throw new InvalidOperationException("TASKOTIME_MODE muss Demo, LocalDemo oder Production sein.");
            }

            VerifyDatabase(factory);
            var hasher = new Pbkdf2PasswordHasher();
            return new DesktopServices
            {
                Authentication = new AuthenticationService(factory, hasher),
                Admin = new AdminMasterDataService(factory, hasher),
                Users = new UserAdministrationService(factory, hasher),
                Bookings = new TimeBookingService(factory, hasher),
                ModeDescriptionKey = description,
                contextFactory = factory
            };
        }

        public static DesktopServices CreateForServices(
            IAuthenticationService authentication,
            IAdminMasterDataService admin,
            IUserAdministrationService users,
            ITimeBookingService bookings,
            TenantDto tenant,
            string modeDescription = "Konfigurierte Testdienste")
        {
            if (authentication == null) throw new ArgumentNullException(nameof(authentication));
            if (admin == null) throw new ArgumentNullException(nameof(admin));
            if (users == null) throw new ArgumentNullException(nameof(users));
            if (bookings == null) throw new ArgumentNullException(nameof(bookings));
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            return new DesktopServices
            {
                Authentication = authentication,
                Admin = admin,
                Users = users,
                Bookings = bookings,
                configuredTenant = tenant,
                ModeDescription = modeDescription
            };
        }

        public TenantDto TenantFor(TenantUserDto user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (configuredTenant != null)
            {
                if (configuredTenant.IdTenant != user.IdTenant)
                    throw new InvalidOperationException("Der konfigurierte Mandant stimmt nicht mit der Anmeldung überein.");
                return configuredTenant;
            }
            if (contextFactory == null)
                throw new InvalidOperationException("Für den Mandanten ist kein Datenbankkontext konfiguriert.");

            using (var context = contextFactory())
            {
                var tenant = context.Tenant.SingleOrDefault(item => item.IdTenant == user.IdTenant);
                if (tenant == null)
                    throw new InvalidOperationException("Der angemeldete Mandant wurde in der Datenbank nicht gefunden.");
                return new TenantDto
                {
                    IdTenant = tenant.IdTenant,
                    TenantName = tenant.TenantName,
                    TenantIdentifier = tenant.TenantIdentifier,
                    Description = tenant.Description,
                    IsActive = tenant.IsActive,
                    IsDeleted = tenant.IsDeleted,
                    DateCreated = tenant.DateCreated,
                    DateModified = tenant.DateModified
                };
            }
        }

        public VmMain CreateMain(TenantUserDto user)
        {
            var query = Query(user);
            var projects = Require(Admin.GetProjects(query));
            var categories = Require(Admin.GetCategories(query));
            var access = new TimeBookingAccessContextDto
            {
                IdTenant = user.IdTenant, IdActingUser = user.IdUser, IdBookingUser = user.IdUser
            };
            var time = new TimeCollectionViewModel(DateTime.Today, Bookings, access,
                projects,
                categories.FirstOrDefault(category =>
                    category.IdCategory != TaskOTime.DTOs.SystemTimeMarkerIds.WorkBreakCategoryId &&
                    category.IdCategory != TaskOTime.DTOs.SystemTimeMarkerIds.StopMarkCategoryId)?.IdCategory ?? Guid.Empty,
                null,
                categories);
            var tasks = new TaskManagementViewModel(LoadTaskLists(user), saveTask: task => SaveTask(user, task));
            return new VmMain(time, tasks);
        }

        public IEnumerable<TaskListViewModel> LoadTaskLists(TenantUserDto user)
        {
            var query = Query(user);
            var lists = Require(Admin.GetTaskLists(query));
            var tasks = Require(Admin.GetTaskItems(query));
            return lists.Select(list => new TaskListViewModel(list.TaskListName, list.TaskListDescription,
                tasks.Where(task => task.IdTaskList == list.IdTaskList).Select(task =>
                {
                    var result = new TaskItemViewModel(task.IdTaskItem, task.TaskItemName,
                        task.TaskItemDescription, task.DueDate?.ToString("dd.MM.yyyy") ?? "")
                    {
                        IdProject = task.IdProject, IdTask = task.IdTaskItem
                    };
                    if (task.IsCompleted) result.MarkDone();
                    return result;
                }))).ToList();
        }

        private void SaveTask(TenantUserDto user, TaskItemViewModel task)
        {
            if (task.IdTask == Guid.Empty) return;
            var item = Require(Admin.GetTaskItems(Query(user))).Single(x => x.IdTaskItem == task.IdTask);
            item.IsCompleted = task.IsDone;
            item.TaskItemName = task.Title;
            item.TaskItemDescription = task.Description;
            Require(Admin.UpdateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = user.IdTenant, IdActingUser = user.IdUser, Item = item
            }));
        }

        public static MasterDataQueryRequest Query(TenantUserDto user) => new MasterDataQueryRequest
        {
            IdTenant = user.IdTenant, IdActingUser = user.IdUser
        };

        public static T Require<T>(ServiceResult<T> result)
        {
            if (result == null) throw new InvalidOperationException("Keine Antwort vom Dienst.");
            if (!result.Success) throw new InvalidOperationException(result.ErrorCode + ": " + result.ErrorMessage);
            return result.Value;
        }

        private static string ResolveProductionConnection(string configuredValue)
        {
            var value = configuredValue.Trim();
            if (value.StartsWith("name=", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOf("metadata=", StringComparison.OrdinalIgnoreCase) >= 0)
                return value;

            var named = ConfigurationManager.ConnectionStrings[value];
            if (named != null)
                return "name=" + value;

            var local = ConfigurationManager.ConnectionStrings["TaskOTimeContext"];
            if (local == null || string.IsNullOrWhiteSpace(local.ConnectionString))
                throw new InvalidOperationException("Die EF6-Verbindung TaskOTimeContext fehlt in App.config.");
            var builder = new EntityConnectionStringBuilder(local.ConnectionString)
            {
                ProviderConnectionString = value
            };
            return builder.ConnectionString;
        }

        private static void VerifyDatabase(Func<TaskOTimeContext> factory)
        {
            try
            {
                using (var context = factory())
                {
                    if (!context.Tenant.Any())
                        throw new InvalidOperationException("Die konfigurierte Task-o-Time-Datenbank enthält keine Mandanten.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Die konfigurierte Task-o-Time-SQL-Datenbank konnte nicht geladen werden. " + ex.Message, ex);
            }
        }
    }
}
