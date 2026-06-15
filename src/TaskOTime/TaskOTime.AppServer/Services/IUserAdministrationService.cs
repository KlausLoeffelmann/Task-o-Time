using System;
using System.Collections.Generic;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IUserAdministrationService
    {
        ServiceResult<CreateTenantAdminResult> CreateTenantAdmin(CreateTenantAdminRequest request);

        ServiceResult<TenantUserDto> CreateUser(CreateUserRequest request);

        ServiceResult<TenantUserDto> DeactivateUser(Guid idTenant, Guid idUser);

        ServiceResult<TenantUserDto> DeleteUser(Guid idTenant, Guid idUser);

        ServiceResult<TenantUserDto> SetUserFlags(Guid idTenant, Guid idUser, bool isActive, bool isDeleted);

        ServiceResult<IReadOnlyList<TenantUserDto>> GetTenantUsers(Guid idTenant, bool includeDeleted = false);
    }
}
