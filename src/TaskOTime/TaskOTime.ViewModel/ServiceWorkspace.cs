using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;

namespace TaskOTime.ViewModel
{

    /// <summary>Shared service-backed collections for the Main Data workspace.</summary>
    public sealed class ServiceWorkspace
    {
        public ServiceWorkspace(TenantDto tenant, Guid actingUserId, IAdminMainDataService adminService, IUserAdministrationService userService, ITimeBookingService timeBookingService)
        {
            if (tenant is null)
                throw new ArgumentNullException(nameof(tenant));
            if (adminService is null)
                throw new ArgumentNullException(nameof(adminService));
            if (userService is null)
                throw new ArgumentNullException(nameof(userService));
            if (timeBookingService is null)
                throw new ArgumentNullException(nameof(timeBookingService));
            Tenant = tenant;
            ActingUserId = actingUserId;
            AdminService = adminService;
            UserService = userService;
            TimeBookingService = timeBookingService;

            Tenants.Add(tenant);
            Replace(Users, Require(userService.GetTenantUsers(tenant.IdTenant), "Load users"));
            var query = new MainDataQueryRequest() { IdTenant = tenant.IdTenant, IdActingUser = actingUserId };
            Replace(Projects, Require(adminService.GetProjects(query), "Load projects"));
            Replace(TaskLists, Require(adminService.GetTaskLists(query), "Load task lists"));
            Replace(Tasks, Require(adminService.GetTaskItems(query), "Load tasks"));
            Replace(Categories, Require(adminService.GetCategories(query), "Load categories"));
            Replace(Tags, Require(adminService.GetTags(query), "Load tags"));
            Replace(Notes, Require(adminService.GetNotes(query), "Load notes"));
            Replace(WebLinks, Require(adminService.GetWebLinks(query), "Load web links"));
            Logs.Add("Main Data loaded from the configured service.");
        }

        public TenantDto Tenant { get; private set; }
        public Guid ActingUserId { get; private set; }
        public IAdminMainDataService AdminService { get; private set; }
        public IUserAdministrationService UserService { get; private set; }
        public ITimeBookingService TimeBookingService { get; private set; }
        public bool CanManage => Users.Any(user => user.IdUser == ActingUserId
            && user.IdTenant == Tenant.IdTenant && user.IsAdmin && user.IsActive && !user.IsDeleted);
        public readonly ObservableCollection<TenantDto> Tenants = new ObservableCollection<TenantDto>();
        public readonly ObservableCollection<TenantUserDto> Users = new ObservableCollection<TenantUserDto>();
        public readonly ObservableCollection<ProjectMainDataDto> Projects = new ObservableCollection<ProjectMainDataDto>();
        public readonly ObservableCollection<TaskListMainDataDto> TaskLists = new ObservableCollection<TaskListMainDataDto>();
        public readonly ObservableCollection<TaskItemMainDataDto> Tasks = new ObservableCollection<TaskItemMainDataDto>();
        public readonly ObservableCollection<CategoryMainDataDto> Categories = new ObservableCollection<CategoryMainDataDto>();
        public readonly ObservableCollection<TagMainDataDto> Tags = new ObservableCollection<TagMainDataDto>();
        public readonly ObservableCollection<NoteMainDataDto> Notes = new ObservableCollection<NoteMainDataDto>();
        public readonly ObservableCollection<WebLinkMainDataDto> WebLinks = new ObservableCollection<WebLinkMainDataDto>();
        public readonly ObservableCollection<string> Logs = new ObservableCollection<string>();

        public MainDataQueryRequest Query()
        {
            return new MainDataQueryRequest() { IdTenant = Tenant.IdTenant, IdActingUser = ActingUserId };
        }

        public static T Require<T>(ServiceResult<T> result, string operation)
        {
            if (result is null)
                throw new InvalidOperationException(operation + ": no service result");
            if (!result.Success)
            {
                throw new InvalidOperationException(operation + ": " + result.ErrorCode + " - " + result.ErrorMessage);
            }
            return result.Value;
        }

        private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            target.Clear();
            foreach (var item in source)
                target.Add(item);
        }
    }
}