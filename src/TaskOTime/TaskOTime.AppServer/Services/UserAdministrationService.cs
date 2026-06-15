using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class UserAdministrationService : ApplicationServiceBase, IUserAdministrationService
    {
        public UserAdministrationService()
        {
        }

        public UserAdministrationService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<CreateTenantAdminResult> CreateTenantAdmin(CreateTenantAdminRequest request)
        {
            if (request == null)
            {
                return ServiceResult<CreateTenantAdminResult>.Fail("InvalidRequest", "A tenant admin request is required.");
            }

            try
            {
                var tenantName = NormalizeRequired(request.TenantName, nameof(request.TenantName));
                var userIdent = NormalizeRequired(request.UserIdent, nameof(request.UserIdent));
                var lastName = NormalizeRequired(request.LastName, nameof(request.LastName));
                var email = NormalizeRequired(request.EMail, nameof(request.EMail));
                var hash = PasswordHasher.CreateHash(request.TemporaryPassword);
                var now = DateTimeOffset.UtcNow;

                using (var context = CreateContext())
                using (var transaction = context.Database.BeginTransaction())
                {
                    if (context.Tenant.Any(t => t.TenantName == tenantName && !t.IsDeleted))
                    {
                        return ServiceResult<CreateTenantAdminResult>.Fail("TenantExists", "A tenant with this name already exists.");
                    }

                    if (context.User.Any(u => u.UserIdent == userIdent || u.EMail == email))
                    {
                        return ServiceResult<CreateTenantAdminResult>.Fail("UserExists", "A user with this identifier or e-mail already exists.");
                    }

                    var tenant = new Tenant
                    {
                        IdTenant = Guid.NewGuid(),
                        TenantName = tenantName,
                        TenantIdentifier = string.IsNullOrWhiteSpace(request.TenantIdentifier) ? null : request.TenantIdentifier.Trim(),
                        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                        IsActive = true,
                        IsDeleted = false,
                        DateCreated = now,
                        DateModified = now
                    };

                    var admin = CreateUserEntity(
                        tenant.IdTenant,
                        userIdent,
                        request.FirstName,
                        request.MiddleName,
                        lastName,
                        email,
                        true,
                        hash,
                        request.PreliminaryPasswordExpiresAt ?? now.AddDays(14),
                        request: null,
                        now: now);

                    context.Tenant.Add(tenant);
                    context.User.Add(admin);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<CreateTenantAdminResult>.Ok(new CreateTenantAdminResult
                    {
                        Tenant = ToTenantDto(tenant),
                        AdminUser = ToUserDto(admin)
                    });
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<CreateTenantAdminResult>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<TenantUserDto> CreateUser(CreateUserRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TenantUserDto>.Fail("InvalidRequest", "A user request is required.");
            }

            try
            {
                var userIdent = NormalizeRequired(request.UserIdent, nameof(request.UserIdent));
                var lastName = NormalizeRequired(request.LastName, nameof(request.LastName));
                var email = NormalizeRequired(request.EMail, nameof(request.EMail));
                var hash = PasswordHasher.CreateHash(request.TemporaryPassword);
                var now = DateTimeOffset.UtcNow;

                using (var context = CreateContext())
                {
                    if (!context.Tenant.Any(t => t.IdTenant == request.IdTenant && t.IsActive && !t.IsDeleted))
                    {
                        return ServiceResult<TenantUserDto>.Fail("TenantNotFound", "The tenant was not found or is inactive.");
                    }

                    if (context.User.Any(u => u.UserIdent == userIdent || u.EMail == email))
                    {
                        return ServiceResult<TenantUserDto>.Fail("UserExists", "A user with this identifier or e-mail already exists.");
                    }

                    var user = CreateUserEntity(
                        request.IdTenant,
                        userIdent,
                        request.FirstName,
                        request.MiddleName,
                        lastName,
                        email,
                        request.IsAdmin,
                        hash,
                        request.PreliminaryPasswordExpiresAt ?? now.AddDays(14),
                        request,
                        now);

                    context.User.Add(user);
                    context.SaveChanges();

                    return ServiceResult<TenantUserDto>.Ok(ToUserDto(user));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TenantUserDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<TenantUserDto> DeactivateUser(Guid idTenant, Guid idUser)
        {
            return SetUserFlags(idTenant, idUser, isActive: false, isDeleted: false);
        }

        public ServiceResult<TenantUserDto> DeleteUser(Guid idTenant, Guid idUser)
        {
            return SetUserFlags(idTenant, idUser, isActive: false, isDeleted: true);
        }

        public ServiceResult<TenantUserDto> SetUserFlags(Guid idTenant, Guid idUser, bool isActive, bool isDeleted)
        {
            using (var context = CreateContext())
            {
                var user = context.User.SingleOrDefault(u => u.IdTenant == idTenant && u.IdUser == idUser);
                if (user == null)
                {
                    return ServiceResult<TenantUserDto>.Fail("UserNotFound", "The tenant user was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                user.IsDeleted = isDeleted;
                user.IsActive = isDeleted ? false : isActive;
                user.DateModified = now;

                if (!user.IsActive && user.DateDeactivated == null)
                {
                    user.DateDeactivated = now;
                }
                else if (user.IsActive)
                {
                    user.DateDeactivated = null;
                }

                if (isDeleted)
                {
                    user.DateDeleted = user.DateDeleted ?? now;
                }
                else
                {
                    user.DateDeleted = null;
                }

                context.SaveChanges();
                return ServiceResult<TenantUserDto>.Ok(ToUserDto(user));
            }
        }

        public ServiceResult<IReadOnlyList<TenantUserDto>> GetTenantUsers(Guid idTenant, bool includeDeleted = false)
        {
            using (var context = CreateContext())
            {
                if (!context.Tenant.Any(t => t.IdTenant == idTenant && !t.IsDeleted))
                {
                    return ServiceResult<IReadOnlyList<TenantUserDto>>.Fail("TenantNotFound", "The tenant was not found.");
                }

                var users = context.User
                    .Where(u => u.IdTenant == idTenant && (includeDeleted || !u.IsDeleted))
                    .OrderBy(u => u.LastName)
                    .ThenBy(u => u.FirstName)
                    .ThenBy(u => u.UserIdent)
                    .ToList()
                    .Select(ToUserDto)
                    .ToList();

                return ServiceResult<IReadOnlyList<TenantUserDto>>.Ok(users);
            }
        }

        private static User CreateUserEntity(
            Guid idTenant,
            string userIdent,
            string firstName,
            string middleName,
            string lastName,
            string email,
            bool isAdmin,
            PasswordHashResult hash,
            DateTimeOffset preliminaryPasswordExpiresAt,
            CreateUserRequest request,
            DateTimeOffset now)
        {
            return new User
            {
                IdUser = Guid.NewGuid(),
                IdTenant = idTenant,
                UserIdent = userIdent,
                FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim(),
                LastName = lastName,
                EMail = email,
                IsAdmin = isAdmin,
                IsActive = true,
                IsDeleted = false,
                MustChangePassword = true,
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                PasswordChangedAt = null,
                PreliminaryPasswordExpiresAt = preliminaryPasswordExpiresAt,
                FailedLoginCount = 0,
                LockoutUntil = null,
                EmojiIndex = request?.EmojiIndex,
                MaxProjects = request?.MaxProjects ?? 50,
                LastLogin = now,
                DateCreated = now,
                DateModified = now,
                SyncId = Guid.NewGuid(),
                SyncStatus = 0
            };
        }
    }
}
