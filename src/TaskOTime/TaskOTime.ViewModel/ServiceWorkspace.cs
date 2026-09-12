using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;

namespace TaskOTime.ViewModel
{

    internal sealed class ServiceWorkspace
    {
        public ServiceWorkspace(TenantDto tenant, Guid actingUserId, IAdminMasterDataService adminService, IUserAdministrationService userService, ITimeBookingService timeBookingService)
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
            Replace(Users, Require(userService.GetTenantUsers(tenant.IdTenant), "Benutzer laden"));
            var query = new MasterDataQueryRequest() { IdTenant = tenant.IdTenant, IdActingUser = actingUserId };
            Replace(Projects, Require(adminService.GetProjects(query), "Projekte laden"));
            Replace(TaskLists, Require(adminService.GetTaskLists(query), "Aufgabenlisten laden"));
            Replace(Tasks, Require(adminService.GetTaskItems(query), "Aufgaben laden"));
            Replace(Categories, Require(adminService.GetCategories(query), "Kategorien laden"));
            Replace(Tags, Require(adminService.GetTags(query), "Tags laden"));
            Replace(Notes, Require(adminService.GetNotes(query), "Notizen laden"));
            Replace(WebLinks, Require(adminService.GetWebLinks(query), "Weblinks laden"));
            Logs.Add("Stammdaten vom konfigurierten Dienst geladen.");
        }

        public TenantDto Tenant { get; private set; }
        public Guid ActingUserId { get; private set; }
        public IAdminMasterDataService AdminService { get; private set; }
        public IUserAdministrationService UserService { get; private set; }
        public ITimeBookingService TimeBookingService { get; private set; }
        public readonly ObservableCollection<TenantDto> Tenants = new ObservableCollection<TenantDto>();
        public readonly ObservableCollection<TenantUserDto> Users = new ObservableCollection<TenantUserDto>();
        public readonly ObservableCollection<ProjectMainDataDto> Projects = new ObservableCollection<ProjectMainDataDto>();
        public readonly ObservableCollection<TaskListMasterDataDto> TaskLists = new ObservableCollection<TaskListMasterDataDto>();
        public readonly ObservableCollection<TaskItemMasterDataDto> Tasks = new ObservableCollection<TaskItemMasterDataDto>();
        public readonly ObservableCollection<CategoryMasterDataDto> Categories = new ObservableCollection<CategoryMasterDataDto>();
        public readonly ObservableCollection<TagMasterDataDto> Tags = new ObservableCollection<TagMasterDataDto>();
        public readonly ObservableCollection<NoteMasterDataDto> Notes = new ObservableCollection<NoteMasterDataDto>();
        public readonly ObservableCollection<WebLinkMasterDataDto> WebLinks = new ObservableCollection<WebLinkMasterDataDto>();
        public readonly ObservableCollection<string> Logs = new ObservableCollection<string>();

        public MasterDataQueryRequest Query()
        {
            return new MasterDataQueryRequest() { IdTenant = Tenant.IdTenant, IdActingUser = ActingUserId };
        }

        public static T Require<T>(ServiceResult<T> result, string operation)
        {
            if (result is null)
                throw new InvalidOperationException(operation + ": kein ServiceResult");
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