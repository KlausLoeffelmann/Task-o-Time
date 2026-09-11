using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;

namespace TaskOTime.TimeTrackingServices.Tests.Doubles
{
    public sealed class TestAuthenticationService : IAuthenticationService
    {
        private readonly TenantUserDto user;
        private readonly Pbkdf2PasswordHasher hasher = new Pbkdf2PasswordHasher();
        private readonly Func<DateTimeOffset> clock;
        private PasswordHashResult passwordHash;

        public TestAuthenticationService(TenantUserDto user, string password,
            Func<DateTimeOffset> clock = null)
        {
            this.user = user ?? throw new ArgumentNullException(nameof(user));
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
            passwordHash = hasher.CreateHash(password);
        }

        public ServiceResult<AuthenticationResult> Authenticate(AuthenticateUserRequest request)
        {
            if (request == null || (request.IdTenant.HasValue && request.IdTenant != user.IdTenant) ||
                !(string.Equals(request.UserIdentOrEmail?.Trim(), user.UserIdent, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(request.UserIdentOrEmail?.Trim(), user.EMail, StringComparison.OrdinalIgnoreCase)))
                return ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "Benutzer oder Kennwort ungültig.");
            if (!user.IsActive || user.IsDeleted)
                return ServiceResult<AuthenticationResult>.Fail("UserInactive", "Benutzer ist nicht aktiv.");
            if (user.LockoutUntil > clock())
                return ServiceResult<AuthenticationResult>.Fail("UserLockedOut", "Benutzer ist vorübergehend gesperrt.");
            if (!hasher.VerifyHash(request.Password, passwordHash.Hash, passwordHash.Salt))
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5) user.LockoutUntil = clock().AddMinutes(15);
                return ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "Benutzer oder Kennwort ungültig.");
            }
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            user.LastLogin = clock();
            return ServiceResult<AuthenticationResult>.Ok(new AuthenticationResult
            {
                User = user, MustChangePassword = user.MustChangePassword
            });
        }

        public ServiceResult<TenantUserDto> ChangePassword(Guid idTenant, Guid idUser,
            string currentPassword, string newPassword) =>
            Change(idTenant, idUser, currentPassword, newPassword, false);

        public ServiceResult<TenantUserDto> ChangeTemporaryPassword(Guid idTenant, Guid idUser,
            string temporaryPassword, string newPassword) =>
            Change(idTenant, idUser, temporaryPassword, newPassword, true);

        private ServiceResult<TenantUserDto> Change(Guid tenant, Guid id, string oldPassword,
            string newPassword, bool temporary)
        {
            if (tenant != user.IdTenant || id != user.IdUser || !user.IsActive || user.IsDeleted ||
                !hasher.VerifyHash(oldPassword, passwordHash.Hash, passwordHash.Salt))
                return ServiceResult<TenantUserDto>.Fail("InvalidCredentials", "Benutzer oder Kennwort ungültig.");
            if (temporary && !user.MustChangePassword)
                return ServiceResult<TenantUserDto>.Fail("PasswordChangeNotRequired", "Kein vorläufiges Kennwort vorhanden.");
            if (string.IsNullOrWhiteSpace(newPassword))
                return ServiceResult<TenantUserDto>.Fail("InvalidRequest", "Neues Kennwort fehlt.");
            passwordHash = hasher.CreateHash(newPassword);
            user.MustChangePassword = false;
            user.PasswordChangedAt = clock();
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            return ServiceResult<TenantUserDto>.Ok(user);
        }
    }
}
