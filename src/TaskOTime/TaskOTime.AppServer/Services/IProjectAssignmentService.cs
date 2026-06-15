using System;
using System.Collections.Generic;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IProjectAssignmentService
    {
        ServiceResult<ProjectAssignmentDto> AssignUserToProject(AssignUserToProjectRequest request);

        ServiceResult<ProjectAssignmentDto> UnassignUserFromProject(Guid idTenant, Guid idProject, Guid idUser);

        ServiceResult<IReadOnlyList<TenantProjectDto>> GetTenantProjects(Guid idTenant, bool includeDeleted = false);

        ServiceResult<IReadOnlyList<ProjectAssignmentDto>> GetTenantAssignments(Guid idTenant, bool includeDeleted = false);

        ServiceResult<IReadOnlyList<ProjectAssignmentDto>> GetProjectAssignments(Guid idTenant, Guid idProject, bool includeDeleted = false);
    }
}
