using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public abstract class ApplicationServiceBase
    {
        private readonly Func<TaskOTimeContext> contextFactory;

        protected ApplicationServiceBase()
            : this(TaskOTimeContextFactory.Create, new Pbkdf2PasswordHasher())
        {
        }

        protected ApplicationServiceBase(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
        {
            if (contextFactory == null)
            {
                throw new ArgumentNullException(nameof(contextFactory));
            }

            if (passwordHasher == null)
            {
                throw new ArgumentNullException(nameof(passwordHasher));
            }

            this.contextFactory = contextFactory;
            PasswordHasher = passwordHasher;
        }

        protected IPasswordHasher PasswordHasher { get; }

        protected TaskOTimeContext CreateContext()
        {
            return contextFactory();
        }

        protected static TenantDto ToTenantDto(Tenant tenant)
        {
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

        protected static TenantUserDto ToUserDto(User user)
        {
            return new TenantUserDto
            {
                IdUser = user.IdUser,
                IdTenant = user.IdTenant,
                UserIdent = user.UserIdent,
                FirstName = user.FirstName,
                MiddleName = user.MiddleName,
                LastName = user.LastName,
                EMail = user.EMail,
                IsAdmin = user.IsAdmin,
                IsActive = user.IsActive,
                IsDeleted = user.IsDeleted,
                MustChangePassword = user.MustChangePassword,
                PasswordChangedAt = user.PasswordChangedAt,
                PreliminaryPasswordExpiresAt = user.PreliminaryPasswordExpiresAt,
                FailedLoginCount = user.FailedLoginCount,
                LockoutUntil = user.LockoutUntil,
                LastLogin = user.LastLogin,
                DateCreated = user.DateCreated,
                DateModified = user.DateModified,
                DateDeactivated = user.DateDeactivated,
                DateDeleted = user.DateDeleted
            };
        }

        protected static TenantProjectDto ToProjectDto(Project project)
        {
            return new TenantProjectDto
            {
                IdProject = project.IdProject,
                IdTenant = project.IdTenant,
                IdUser = project.IdUser,
                ProjectName = project.ProjectName,
                ProjectType = project.ProjectType,
                ProjectNumber = project.ProjectNumber,
                ProjectDescription = project.ProjectDescription,
                ProjectIdentifier = project.ProjectIdentifier,
                IsActive = project.IsActive,
                IsDeleted = project.IsDeleted,
                DateCreated = project.DateCreated,
                DateModified = project.DateModified
            };
        }

        protected static ProjectAssignmentDto ToAssignmentDto(ProjectUserAssignment assignment)
        {
            return new ProjectAssignmentDto
            {
                IdProjectUserAssignment = assignment.IdProjectUserAssignment,
                IdProject = assignment.IdProject,
                IdUser = assignment.IdUser,
                IdAssignedByUser = assignment.IdAssignedByUser,
                AssignmentRole = assignment.AssignmentRole,
                CanBookTime = assignment.CanBookTime,
                CanManageTasks = assignment.CanManageTasks,
                CanManageProject = assignment.CanManageProject,
                DisplayOrder = assignment.DisplayOrder,
                IsActive = assignment.IsActive,
                IsDeleted = assignment.IsDeleted,
                DateAssigned = assignment.DateAssigned,
                DateRemoved = assignment.DateRemoved,
                DateCreated = assignment.DateCreated,
                DateModified = assignment.DateModified
            };
        }

        protected static string NormalizeRequired(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(name + " is required.", name);
            }

            return value.Trim();
        }
    }
}
