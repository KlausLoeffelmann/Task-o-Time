using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class ProjectAssignmentService : ApplicationServiceBase, IProjectAssignmentService
    {
        public ProjectAssignmentService()
        {
        }

        public ProjectAssignmentService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<ProjectAssignmentDto> AssignUserToProject(AssignUserToProjectRequest request)
        {
            if (request == null)
            {
                return ServiceResult<ProjectAssignmentDto>.Fail("InvalidRequest", "An assignment request is required.");
            }

            using (var context = CreateContext())
            {
                var project = context.Project.SingleOrDefault(p =>
                    p.IdTenant == request.IdTenant &&
                    p.IdProject == request.IdProject &&
                    p.IsActive &&
                    !p.IsDeleted);
                if (project == null)
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("ProjectNotFound", "The active tenant project was not found.");
                }

                var user = context.User.SingleOrDefault(u =>
                    u.IdTenant == request.IdTenant &&
                    u.IdUser == request.IdUser &&
                    u.IsActive &&
                    !u.IsDeleted);
                if (user == null)
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("UserNotFound", "The active tenant user was not found.");
                }

                if (request.IdAssignedByUser.HasValue &&
                    !context.User.Any(u =>
                        u.IdTenant == request.IdTenant &&
                        u.IdUser == request.IdAssignedByUser.Value &&
                        u.IsActive &&
                        !u.IsDeleted))
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("AssignedByUserNotFound", "The assigning tenant user was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                var assignment = context.ProjectUserAssignment.SingleOrDefault(a =>
                    a.IdProject == request.IdProject &&
                    a.IdUser == request.IdUser);

                if (assignment == null)
                {
                    assignment = new ProjectUserAssignment
                    {
                        IdProjectUserAssignment = Guid.NewGuid(),
                        IdProject = request.IdProject,
                        IdUser = request.IdUser,
                        DateCreated = now
                    };
                    context.ProjectUserAssignment.Add(assignment);
                }

                assignment.IdAssignedByUser = request.IdAssignedByUser;
                assignment.AssignmentRole = request.AssignmentRole;
                assignment.CanBookTime = request.CanBookTime;
                assignment.CanManageTasks = request.CanManageTasks;
                assignment.CanManageProject = request.CanManageProject;
                assignment.DisplayOrder = request.DisplayOrder ?? GetNextDisplayOrder(context, request.IdProject);
                assignment.IsActive = true;
                assignment.IsDeleted = false;
                assignment.DateAssigned = now;
                assignment.DateRemoved = null;
                assignment.DateModified = now;

                context.SaveChanges();
                return ServiceResult<ProjectAssignmentDto>.Ok(ToAssignmentDto(assignment));
            }
        }

        public ServiceResult<ProjectAssignmentDto> UnassignUserFromProject(Guid idTenant, Guid idProject, Guid idUser)
        {
            using (var context = CreateContext())
            {
                if (!context.Project.Any(p => p.IdTenant == idTenant && p.IdProject == idProject && !p.IsDeleted))
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("ProjectNotFound", "The tenant project was not found.");
                }

                var assignment = context.ProjectUserAssignment.SingleOrDefault(a => a.IdProject == idProject && a.IdUser == idUser);
                if (assignment == null)
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("AssignmentNotFound", "The project user assignment was not found.");
                }

                if (!context.User.Any(u => u.IdTenant == idTenant && u.IdUser == idUser))
                {
                    return ServiceResult<ProjectAssignmentDto>.Fail("UserNotFound", "The tenant user was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                assignment.IsActive = false;
                assignment.IsDeleted = true;
                assignment.DateRemoved = now;
                assignment.DateModified = now;
                context.SaveChanges();

                return ServiceResult<ProjectAssignmentDto>.Ok(ToAssignmentDto(assignment));
            }
        }

        public ServiceResult<IReadOnlyList<TenantProjectDto>> GetTenantProjects(Guid idTenant, bool includeDeleted = false)
        {
            using (var context = CreateContext())
            {
                if (!context.Tenant.Any(t => t.IdTenant == idTenant && !t.IsDeleted))
                {
                    return ServiceResult<IReadOnlyList<TenantProjectDto>>.Fail("TenantNotFound", "The tenant was not found.");
                }

                var projects = context.Project
                    .Where(p => p.IdTenant == idTenant && (includeDeleted || !p.IsDeleted))
                    .OrderBy(p => p.ProjectName)
                    .ThenBy(p => p.ProjectIdentifier)
                    .ToList()
                    .Select(ToProjectDto)
                    .ToList();

                return ServiceResult<IReadOnlyList<TenantProjectDto>>.Ok(projects);
            }
        }

        public ServiceResult<IReadOnlyList<ProjectAssignmentDto>> GetTenantAssignments(Guid idTenant, bool includeDeleted = false)
        {
            using (var context = CreateContext())
            {
                if (!context.Tenant.Any(t => t.IdTenant == idTenant && !t.IsDeleted))
                {
                    return ServiceResult<IReadOnlyList<ProjectAssignmentDto>>.Fail("TenantNotFound", "The tenant was not found.");
                }

                var assignments = (from assignment in context.ProjectUserAssignment
                                   join project in context.Project on assignment.IdProject equals project.IdProject
                                   where project.IdTenant == idTenant &&
                                         (includeDeleted || (!assignment.IsDeleted && !project.IsDeleted))
                                   orderby project.ProjectName, assignment.DisplayOrder
                                   select assignment)
                    .ToList()
                    .Select(ToAssignmentDto)
                    .ToList();

                return ServiceResult<IReadOnlyList<ProjectAssignmentDto>>.Ok(assignments);
            }
        }

        public ServiceResult<IReadOnlyList<ProjectAssignmentDto>> GetProjectAssignments(Guid idTenant, Guid idProject, bool includeDeleted = false)
        {
            using (var context = CreateContext())
            {
                if (!context.Project.Any(p => p.IdTenant == idTenant && p.IdProject == idProject && (includeDeleted || !p.IsDeleted)))
                {
                    return ServiceResult<IReadOnlyList<ProjectAssignmentDto>>.Fail("ProjectNotFound", "The tenant project was not found.");
                }

                var assignments = context.ProjectUserAssignment
                    .Where(a => a.IdProject == idProject && (includeDeleted || !a.IsDeleted))
                    .OrderBy(a => a.DisplayOrder)
                    .ThenBy(a => a.IdUser)
                    .ToList()
                    .Select(ToAssignmentDto)
                    .ToList();

                return ServiceResult<IReadOnlyList<ProjectAssignmentDto>>.Ok(assignments);
            }
        }

        private static int GetNextDisplayOrder(TaskOTimeContext context, Guid idProject)
        {
            var maxDisplayOrder = context.ProjectUserAssignment
                .Where(a => a.IdProject == idProject)
                .Select(a => (int?)a.DisplayOrder)
                .Max();

            return (maxDisplayOrder ?? -1) + 1;
        }
    }
}
