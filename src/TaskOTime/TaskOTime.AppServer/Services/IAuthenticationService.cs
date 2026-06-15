using System;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IAuthenticationService
    {
        ServiceResult<AuthenticationResult> Authenticate(AuthenticateUserRequest request);

        ServiceResult<TenantUserDto> ChangePassword(Guid idTenant, Guid idUser, string currentPassword, string newPassword);

        ServiceResult<TenantUserDto> ChangeTemporaryPassword(Guid idTenant, Guid idUser, string temporaryPassword, string newPassword);
    }
}
