using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel
{

    /// <summary>Shared service-backed collections for the Main Data workspace.</summary>
    public sealed class ServiceWorkspace : ViewModelBase
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
            ActingUserId = actingUserId;
            AdminService = adminService;
            UserService = userService;
            TimeBookingService = timeBookingService;

            Tenant = Require(adminService.GetTenant(new GetTenantRequest
            {
                IdTenant = tenant.IdTenant, IdActingUser = actingUserId
            }), "Load tenant");
            Tenants.Add(Tenant);
            Replace(Users, Require(userService.GetTenantUsers(Tenant.IdTenant), "Load users"));
            ReloadMainData();
        }

        private bool _isMainDataLoaded;
        public bool IsMainDataLoaded
        {
            get => _isMainDataLoaded;
            private set => SetProperty(ref _isMainDataLoaded, value, nameof(IsMainDataLoaded));
        }
        public TenantDto Tenant { get; private set; }
        public Guid ActingUserId { get; private set; }
        public IAdminMainDataService AdminService { get; private set; }
        public IUserAdministrationService UserService { get; private set; }
        public ITimeBookingService TimeBookingService { get; private set; }
        public void ApplyTenant(TenantDto tenant)
        {
            if (tenant == null || tenant.IdTenant != Tenant.IdTenant)
                throw new InvalidOperationException("The saved tenant does not match this workspace.");
            var index = Tenants.IndexOf(Tenant);
            Tenant = tenant;
            Tenants[index] = tenant;
            if (!tenant.IsActive) ClearMainData();
            OnPropertyChanged(nameof(Tenant));
        }

        public void ReloadMainData()
        {
            if (!Tenant.IsActive)
            {
                ClearMainData();
                Logs.Add("Tenant is inactive. Only tenant administration is available.");
                return;
            }
            if (IsMainDataLoaded) return;

            var query = Query();
            // Load the complete snapshot before publishing collections or enabling normal mutations.
            var projects = Require(AdminService.GetProjects(query), "Load projects");
            var taskLists = Require(AdminService.GetTaskLists(query), "Load task lists");
            var tasks = Require(AdminService.GetTaskItems(query), "Load tasks");
            var categories = Require(AdminService.GetCategories(query), "Load categories");
            var tags = Require(AdminService.GetTags(query), "Load tags");
            var notes = Require(AdminService.GetNotes(query), "Load notes");
            var webLinks = Require(AdminService.GetWebLinks(query), "Load web links");
            Replace(Projects, projects);
            Replace(TaskLists, taskLists);
            Replace(Tasks, tasks);
            Replace(Categories, categories);
            Replace(Tags, tags);
            Replace(Notes, notes);
            Replace(WebLinks, webLinks);
            IsMainDataLoaded = true;
            Logs.Add("Main Data loaded from the configured service.");
        }

        private void ClearMainData()
        {
            IsMainDataLoaded = false;
            Projects.Clear();
            TaskLists.Clear();
            Tasks.Clear();
            Categories.Clear();
            Tags.Clear();
            Notes.Clear();
            WebLinks.Clear();
        }

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