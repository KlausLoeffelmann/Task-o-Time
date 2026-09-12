using System;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaskOTime.AppServer.IntegrationTests;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.Validation
{
    internal sealed class SeededFixture : IDisposable
    {
        internal LocalDbPersistenceTestDatabase Database { get; } = new LocalDbPersistenceTestDatabase();
        internal string Password { get; } = "Validation-" + Guid.NewGuid().ToString("N") + "!a9";
        internal string UserName { get; } = "validation-" + Guid.NewGuid().ToString("N");

        internal void Create()
        {
            PrepareMetadata();
            Database.Create();
            var hasher = new Pbkdf2PasswordHasher();
            var users = new UserAdministrationService(Database.CreateContext, hasher);
            var created = Require(users.CreateTenantAdmin(new CreateTenantAdminRequest
            {
                TenantName = "Validation " + Guid.NewGuid().ToString("N"),
                TenantIdentifier = "validation",
                UserIdent = UserName,
                FirstName = "Validation",
                LastName = "Administrator",
                EMail = UserName + "@example.invalid",
                TemporaryPassword = Password,
                PreliminaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            }));
            var authentication = new AuthenticationService(Database.CreateContext, hasher);
            using (var context = Database.CreateContext())
            {
                SystemTimeMarkerSeed.EnsureCategories(context, created.AdminUser.IdUser);
                context.SaveChanges();
            }
            VerifyMarkerCategories(Database.CreateContext, created.AdminUser.IdUser);
            var login = Require(authentication.Authenticate(new AuthenticateUserRequest
            {
                IdTenant = created.Tenant.IdTenant, UserIdentOrEmail = UserName, Password = Password
            }));
            if (!login.MustChangePassword)
                throw new InvalidOperationException("The real service did not require initial password change.");

            var admin = new AdminMasterDataService(Database.CreateContext, hasher);
            foreach (var name in new[] { "First validation project", "Second validation project" })
                Require(admin.CreateProject(new SaveProjectRequest
                {
                    IdTenant = created.Tenant.IdTenant,
                    IdActingUser = created.AdminUser.IdUser,
                    Item = new ProjectMainDataDto
                    {
                        IdProject = Guid.NewGuid(), ProjectName = name, IsActive = true
                    }
                }));
            Require(admin.CreateCategory(new SaveCategoryRequest
            {
                IdTenant = created.Tenant.IdTenant,
                IdActingUser = created.AdminUser.IdUser,
                Item = new CategoryMasterDataDto
                {
                    IdCategory = Guid.NewGuid(), IdUser = created.AdminUser.IdUser,
                    CategoryName = "Validation work", IsPublic = true, DisplayOrder = 1
                }
            }));
            var query = new MasterDataQueryRequest
            {
                IdTenant = created.Tenant.IdTenant, IdActingUser = created.AdminUser.IdUser
            };
            var categories = Require(admin.GetCategories(query));
            if (Require(admin.GetProjects(query)).Count != 2 ||
                !categories.Any(category => category.IdCategory == SystemTimeMarkerIds.WorkBreakCategoryId) ||
                !categories.Any(category => category.IdCategory == SystemTimeMarkerIds.StopMarkCategoryId))
                throw new InvalidOperationException("Real service seed round-trip did not return projects/categories.");
        }

        internal static void VerifyMarkerCategories(Func<TaskOTimeContext> factory, Guid ownerUserId)
        {
            using (var context = factory())
            {
                var ids = new[] { SystemTimeMarkerIds.WorkBreakCategoryId, SystemTimeMarkerIds.StopMarkCategoryId };
                var categories = context.Category.Where(category => ids.Contains(category.IdCategory)).ToList();
                if (categories.Count != 2 || categories.Any(category => category.IdUser != ownerUserId || !category.IsPublic) ||
                    categories.Single(category => category.IdCategory == ids[0]).CategoryName != SystemTimeMarkerIds.WorkBreakCategoryName ||
                    categories.Single(category => category.IdCategory == ids[1]).CategoryName != SystemTimeMarkerIds.StopMarkCategoryName ||
                    !SystemTimeMarkerSeed.HasSeededLookupItems(context))
                    throw new InvalidOperationException("Owned fixture marker categories/lookup IDs did not round-trip correctly.");
            }
        }

        internal static void PrepareMetadata()
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var runtime = XDocument.Load(Path.Combine(root, "TaskOTime.edmx")).Root.Elements()
                .Single(element => element.Name.LocalName == "Runtime");
            var model = Path.Combine(root, "Model");
            Directory.CreateDirectory(model);
            foreach (var pair in new[]
            {
                new[] { "ConceptualModels", "csdl" },
                new[] { "StorageModels", "ssdl" },
                new[] { "Mappings", "msl" }
            })
            {
                var element = runtime.Elements().Single(item => item.Name.LocalName == pair[0]).Elements().Single();
                element.Save(Path.Combine(model, "TaskOTime." + pair[1]));
            }
        }

        internal static SqlConnectionStringBuilder ValidateConnection(string value, string owner)
        {
            var builder = new SqlConnectionStringBuilder(value);
            if (!Guid.TryParseExact(owner, "N", out var token) || token == Guid.Empty ||
                !Regex.IsMatch(builder.InitialCatalog, @"\ATaskOTime_Validation_[0-9a-f]{32}\z") ||
                !string.Equals(builder.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
                !builder.IntegratedSecurity || builder.AttachDBFilename.Length != 0 ||
                builder.UserID.Length != 0 || builder.Password.Length != 0 || builder.UserInstance ||
                builder.FailoverPartner.Length != 0)
                throw new ArgumentException("An explicit owned LocalDB connection and owner token are required.");
            return new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = builder.InitialCatalog,
                IntegratedSecurity = true, Pooling = false, ConnectTimeout = 15
            };
        }

        internal static Func<TaskOTimeContext> VerifyConnection(string value, string owner)
        {
            var normalized = ValidateConnection(value, owner);
            using (var connection = new SqlConnection(normalized.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0 AND name = @name AND CONVERT(nvarchar(128), value) = @owner";
                    command.Parameters.AddWithValue("@name", LocalDbPersistenceTestDatabase.OwnershipProperty);
                    command.Parameters.AddWithValue("@owner", owner);
                    if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                        throw new InvalidOperationException("Database ownership marker does not match.");
                }
            }
            var metadata = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "TaskOTime");
            var entity = new EntityConnectionStringBuilder
            {
                Provider = "System.Data.SqlClient", ProviderConnectionString = normalized.ConnectionString,
                Metadata = metadata + ".csdl|" + metadata + ".ssdl|" + metadata + ".msl"
            };
            return () => new TaskOTimeContext(entity.ConnectionString);
        }

        internal static bool Exists(string name)
        {
            using (var connection = LocalDbPersistenceTestDatabase.OpenMasterConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DB_ID(@name)";
                command.Parameters.AddWithValue("@name", name);
                return command.ExecuteScalar() != DBNull.Value;
            }
        }

        internal static T Require<T>(ServiceResult<T> result)
        {
            if (result == null || !result.Success)
                throw new InvalidOperationException("Production service failed: " + result?.ErrorCode);
            return result.Value;
        }

        public void Dispose() => Database.Dispose();
    }
}
