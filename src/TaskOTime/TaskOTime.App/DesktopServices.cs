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
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.App
{
    public sealed class DesktopServices
    {
        public IAuthenticationService Authentication { get; private set; }
        public IAdminMainDataService Admin { get; private set; }
        public IUserAdministrationService Users { get; private set; }
        public ITimeBookingService Bookings { get; private set; }
        private string customModeDescription;
        public string ModeDescriptionKey { get; private set; }
        public string ModeDescription
        {
            get => ModeDescriptionKey == null ? customModeDescription : ViewModel.Localization.LocalizationService.Current[ModeDescriptionKey];
            private set => customModeDescription = value;
        }
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
                    throw new InvalidOperationException(LocalizationService.Current["Login_ConnectionRequired"]);
                var entityConnection = ResolveProductionConnection(connection);
                factory = () => TaskOTimeContextFactory.Create(entityConnection);
                description = "Login_ModeProduction";
            }
            else
            {
                throw new InvalidOperationException(LocalizationService.Current["Login_InvalidMode"]);
            }

            VerifyDatabase(factory);
            var hasher = new Pbkdf2PasswordHasher();
            return new DesktopServices
            {
                Authentication = new AuthenticationService(factory, hasher),
                Admin = new AdminMainDataService(factory, hasher),
                Users = new UserAdministrationService(factory, hasher),
                Bookings = new TimeBookingService(factory, hasher),
                ModeDescriptionKey = description
            };
        }

        public static DesktopServices CreateForServices(
            IAuthenticationService authentication,
            IAdminMainDataService admin,
            IUserAdministrationService users,
            ITimeBookingService bookings,
            TenantDto tenant,
            string modeDescription = null)
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
                ModeDescription = modeDescription,
                ModeDescriptionKey = modeDescription == null ? "Login_ModeConfigured" : null
            };
        }

        public TenantDto TenantFor(TenantUserDto user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (configuredTenant != null)
            {
                if (configuredTenant.IdTenant != user.IdTenant)
                    throw new InvalidOperationException(LocalizationService.Current["Login_TenantMismatch"]);
            }
            return Require(Admin.GetTenant(new GetTenantRequest
            {
                IdTenant = user.IdTenant, IdActingUser = user.IdUser
            }));
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
                        task.TaskItemDescription, "")
                    {
                        IdProject = task.IdProject, IdTask = task.IdTaskItem, DueDate = task.DueDate
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

        public static MainDataQueryRequest Query(TenantUserDto user) => new MainDataQueryRequest
        {
            IdTenant = user.IdTenant, IdActingUser = user.IdUser
        };

        public static T Require<T>(ServiceResult<T> result)
        {
            if (result == null) throw new InvalidOperationException(LocalizationService.Current["Common_NoServiceResult"]);
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
                throw new InvalidOperationException(LocalizationService.Current["Login_ModelConnectionMissing"]);
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
                        throw new InvalidOperationException(LocalizationService.Current["Login_NoTenants"]);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    LocalizationService.Current.Format("Login_DatabaseFailed", ex.Message), ex);
            }
        }
    }
}
