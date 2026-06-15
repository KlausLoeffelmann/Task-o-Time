using System;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class AuthenticationService : ApplicationServiceBase, IAuthenticationService
    {
        private const int MaximumFailedLogins = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public AuthenticationService()
        {
        }

        public AuthenticationService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<AuthenticationResult> Authenticate(AuthenticateUserRequest request)
        {
            if (request == null)
            {
                return ServiceResult<AuthenticationResult>.Fail("InvalidRequest", "An authentication request is required.");
            }

            var userIdentOrEmail = string.IsNullOrWhiteSpace(request.UserIdentOrEmail)
                ? null
                : request.UserIdentOrEmail.Trim();
            if (string.IsNullOrWhiteSpace(userIdentOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            {
                return ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "The user identifier and password are required.");
            }

            using (var context = CreateContext())
            {
                var query = context.User.Where(u => u.UserIdent == userIdentOrEmail || u.EMail == userIdentOrEmail);
                if (request.IdTenant.HasValue)
                {
                    query = query.Where(u => u.IdTenant == request.IdTenant.Value);
                }

                var user = query.FirstOrDefault();
                if (user == null)
                {
                    return ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "The user identifier or password is invalid.");
                }

                var now = DateTimeOffset.UtcNow;
                if (!user.IsActive || user.IsDeleted)
                {
                    return ServiceResult<AuthenticationResult>.Fail("UserInactive", "The user is inactive or deleted.");
                }

                if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
                {
                    return ServiceResult<AuthenticationResult>.Fail("UserLockedOut", "The user is temporarily locked out.");
                }

                if (user.MustChangePassword &&
                    user.PreliminaryPasswordExpiresAt.HasValue &&
                    user.PreliminaryPasswordExpiresAt.Value <= now)
                {
                    return ServiceResult<AuthenticationResult>.Fail("TemporaryPasswordExpired", "The temporary password has expired.");
                }

                if (!PasswordHasher.VerifyHash(request.Password, user.PasswordHash, user.PasswordSalt))
                {
                    RegisterFailedLogin(context, user, now);
                    return ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "The user identifier or password is invalid.");
                }

                user.FailedLoginCount = 0;
                user.LockoutUntil = null;
                user.LastLogin = now;
                user.DateModified = now;
                context.SaveChanges();

                return ServiceResult<AuthenticationResult>.Ok(new AuthenticationResult
                {
                    User = ToUserDto(user),
                    MustChangePassword = user.MustChangePassword
                });
            }
        }

        public ServiceResult<TenantUserDto> ChangePassword(Guid idTenant, Guid idUser, string currentPassword, string newPassword)
        {
            return ChangePasswordCore(idTenant, idUser, currentPassword, newPassword, requireTemporaryPassword: false);
        }

        public ServiceResult<TenantUserDto> ChangeTemporaryPassword(Guid idTenant, Guid idUser, string temporaryPassword, string newPassword)
        {
            return ChangePasswordCore(idTenant, idUser, temporaryPassword, newPassword, requireTemporaryPassword: true);
        }

        private ServiceResult<TenantUserDto> ChangePasswordCore(
            Guid idTenant,
            Guid idUser,
            string currentPassword,
            string newPassword,
            bool requireTemporaryPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                return ServiceResult<TenantUserDto>.Fail("InvalidRequest", "The current and new passwords are required.");
            }

            using (var context = CreateContext())
            {
                var user = context.User.SingleOrDefault(u => u.IdTenant == idTenant && u.IdUser == idUser);
                if (user == null || user.IsDeleted || !user.IsActive)
                {
                    return ServiceResult<TenantUserDto>.Fail("UserNotFound", "The active tenant user was not found.");
                }

                if (requireTemporaryPassword && !user.MustChangePassword)
                {
                    return ServiceResult<TenantUserDto>.Fail("PasswordChangeNotRequired", "The user is not required to change the temporary password.");
                }

                if (!PasswordHasher.VerifyHash(currentPassword, user.PasswordHash, user.PasswordSalt))
                {
                    return ServiceResult<TenantUserDto>.Fail("InvalidCredentials", "The current password is invalid.");
                }

                var hash = PasswordHasher.CreateHash(newPassword);
                var now = DateTimeOffset.UtcNow;
                user.PasswordHash = hash.Hash;
                user.PasswordSalt = hash.Salt;
                user.PasswordChangedAt = now;
                user.PreliminaryPasswordExpiresAt = null;
                user.MustChangePassword = false;
                user.FailedLoginCount = 0;
                user.LockoutUntil = null;
                user.DateModified = now;
                context.SaveChanges();

                return ServiceResult<TenantUserDto>.Ok(ToUserDto(user));
            }
        }

        private static void RegisterFailedLogin(TaskOTimeContext context, User user, DateTimeOffset now)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaximumFailedLogins)
            {
                user.LockoutUntil = now.Add(LockoutDuration);
            }

            user.DateModified = now;
            context.SaveChanges();
        }
    }
}
