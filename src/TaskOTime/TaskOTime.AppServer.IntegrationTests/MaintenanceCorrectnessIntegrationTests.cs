using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.AppServer.IntegrationTests
{
    public sealed partial class AppServerPersistenceIntegrationTests
    {
        private const string InitialPassword = "Maintenance-initial-password-42!";

        private MainDataViewModel CreateMaintenanceModel(TenantTestInteraction interaction)
        {
            var hasher = new Pbkdf2PasswordHasher();
            var admin = new AdminMainDataService(database.CreateContext, hasher);
            var tenant = AssertSucceeded(admin.GetTenant(new GetTenantRequest
            {
                IdTenant = TenantId, IdActingUser = AdminUserId
            }));
            return new MainDataViewModel(tenant, AdminUserId, admin,
                new UserAdministrationService(database.CreateContext, hasher),
                new TimeBookingService(database.CreateContext, hasher), 3, interaction);
        }

        [DataTestMethod]
        [DataRow(0, false)]
        [DataRow(0, true)]
        [DataRow(1, false)]
        [DataRow(1, true)]
        [DataRow(2, false)]
        [DataRow(2, true)]
        [DataRow(3, false)]
        [DataRow(3, true)]
        public void CollaborationCommands_CreateReloadAndDeleteThroughSql(int tab, bool atSchemaLimit)
        {
            var interaction = new TenantTestInteraction();
            var vm = CreateMaintenanceModel(interaction).Collaboration;
            var urlPrefix = "https://example.invalid/";
            var values = new[]
            {
                atSchemaLimit ? new string('C', 50) : "Development",
                atSchemaLimit ? new string('T', 30) : "Important",
                atSchemaLimit ? new string('n', 99) + char.ConvertFromUtf32(0x1F600) + new string('n', 3899)
                    : "Meeting notes\nKeep the entire note.",
                atSchemaLimit ? urlPrefix + new string('p', 2000 - urlPrefix.Length)
                    : "https://Example.Invalid/path?x=1"
            };
            var value = values[tab];
            vm.SelectedTab = tab;
            vm.QuickValue = " " + value + " ";
            vm.AddCommand.Execute(null);
            Assert.IsNull(interaction.Message, "Valid input must not produce a service or validation error.");
            Assert.AreEqual("", vm.QuickValue);
            Assert.IsTrue(vm.DeleteCommand.CanExecute(null));

            var reloaded = CreateMaintenanceModel(interaction).Collaboration;
            reloaded.SelectedTab = tab;
            using (var context = database.CreateContext())
            {
                switch (tab)
                {
                    case 0:
                        var category = reloaded.Categories.Single();
                        Assert.AreEqual(vm.SelectedCategory.IdCategory, category.IdCategory);
                        Assert.AreEqual(value, context.Category.Single().CategoryName);
                        Assert.AreEqual(TenantId, category.IdTenant);
                        reloaded.SelectedCategory = category;
                        break;
                    case 1:
                        var tag = reloaded.Tags.Single();
                        Assert.AreEqual(vm.SelectedTag.IdTag, tag.IdTag);
                        Assert.AreEqual(value, context.Tag.Single().Tag1);
                        Assert.AreEqual(AdminUserId, tag.IdUser);
                        reloaded.SelectedTag = tag;
                        break;
                    case 2:
                        var note = reloaded.Notes.Single();
                        Assert.AreEqual(vm.SelectedNote.IdNote, note.IdNote);
                        Assert.AreEqual(value, note.NoteText);
                        Assert.AreEqual(value, context.Note.Single().Note1);
                        Assert.AreEqual(atSchemaLimit ? new string('n', 99) : "Meeting notes", note.NoteMnemonic);
                        Assert.AreEqual(note.NoteMnemonic, context.Note.Single().NoteMnemonic);
                        Assert.AreEqual(AdminUserId, note.IdUser);
                        reloaded.SelectedNote = note;
                        break;
                    case 3:
                        var link = reloaded.WebLinks.Single();
                        Assert.AreEqual(vm.SelectedWebLink.IdWebLink, link.IdWebLink);
                        Assert.AreEqual(new Uri(value).AbsoluteUri, link.Link);
                        Assert.AreEqual("example.invalid", link.Domain);
                        Assert.AreEqual(atSchemaLimit ? value.Substring(0, 100) : value, link.Title);
                        Assert.AreEqual(link.Link, context.WebLink.Single().Link);
                        Assert.AreEqual(link.Domain, context.WebLink.Single().Domain);
                        Assert.AreEqual(AdminUserId, link.IdUser);
                        reloaded.SelectedWebLink = link;
                        break;
                }
            }
            reloaded.DeleteCommand.Execute(null);
            Assert.IsNull(interaction.Message);
            using (var context = database.CreateContext())
            {
                Assert.AreEqual(0, context.Category.Count());
                Assert.AreEqual(0, context.Tag.Count());
                Assert.AreEqual(0, context.Note.Count());
                Assert.AreEqual(0, context.WebLink.Count());
            }
        }

        [DataTestMethod]
        [DataRow("relative/path")]
        [DataRow("file:///C:/private.txt")]
        [DataRow("https://user:secret@example.invalid/")]
        [DataRow("https://example.invalid/invalid path")]
        public void Collaboration_InvalidWebLinkReportsErrorWithoutSqlMutation(string value)
        {
            var interaction = new TenantTestInteraction();
            var vm = CreateMaintenanceModel(interaction).Collaboration;
            vm.SelectedTab = 3;
            vm.QuickValue = value;
            vm.AddCommand.Execute(null);
            Assert.AreEqual(value, vm.QuickValue);
            Assert.AreEqual(0, vm.WebLinks.Count);
            Assert.IsNull(vm.SelectedWebLink);
            StringAssert.Contains(interaction.Message, "absolute HTTP or HTTPS URL");
            using (var context = database.CreateContext())
                Assert.AreEqual(0, context.WebLink.Count());
        }

        [TestMethod]
        public void TenantDeactivation_RequiresExplicitConfirmationBeforeSavingToSql()
        {
            var interaction = new TenantTestInteraction { ConfirmResult = false };
            var main = CreateMaintenanceModel(interaction);
            var original = main.Tenant;
            var vm = main.TenantUsers;
            vm.TenantName = "Deactivation draft";
            vm.TenantActive = false;
            vm.SaveTenantCommand.Execute(null);
            StringAssert.Contains(interaction.Confirmation, "saved to the database");
            StringAssert.Contains(interaction.Confirmation, "prevents sign-in");
            StringAssert.Contains(interaction.Confirmation, "end your session");
            StringAssert.Contains(interaction.Confirmation, "administration API");
            Assert.AreSame(original, main.Tenant);
            Assert.IsTrue(main.Tenant.IsActive);
            Assert.AreEqual("Deactivation draft", vm.TenantName);
            Assert.IsFalse(vm.TenantActive);
            Assert.IsNull(interaction.Message);
            using (var context = database.CreateContext())
            {
                Assert.IsTrue(context.Tenant.Single().IsActive);
                Assert.AreEqual(original.TenantName, context.Tenant.Single().TenantName);
                Assert.AreEqual(original.DateModified, context.Tenant.Single().DateModified);
            }
            interaction.ConfirmResult = true;
            vm.SaveTenantCommand.Execute(null);
            Assert.AreEqual("Tenant saved.", interaction.Message);
            using (var context = database.CreateContext())
            {
                Assert.IsFalse(context.Tenant.Single().IsActive);
                Assert.AreEqual("Deactivation draft", context.Tenant.Single().TenantName);
            }
        }

        [DataTestMethod]
        [DataRow(false, true)]
        [DataRow(false, false)]
        [DataRow(true, true)]
        [DataRow(true, false)]
        public void Authentication_RejectsInactiveOrDeletedTenantForAdminsAndUsers(bool deleted, bool isAdmin)
        {
            ConfigurePasswordUser(isAdmin);
            using (var context = database.CreateContext())
            {
                var tenant = context.Tenant.Single();
                tenant.IsActive = deleted;
                tenant.IsDeleted = deleted;
                context.SaveChanges();
            }
            var auth = new AuthenticationService(database.CreateContext, new Pbkdf2PasswordHasher());
            var result = auth.Authenticate(new AuthenticateUserRequest
            {
                IdTenant = TenantId, UserIdentOrEmail = "integration-admin", Password = InitialPassword
            });
            Assert.IsFalse(result.Success);
            Assert.AreEqual("TenantInactive", result.ErrorCode);
            Assert.IsNull(result.Value);
            Assert.AreEqual("TenantInactive",
                auth.ChangeTemporaryPassword(TenantId, AdminUserId, InitialPassword, "Replacement-password-42!").ErrorCode);
            Assert.AreEqual("TenantInactive",
                auth.ChangePassword(TenantId, AdminUserId, InitialPassword, "Replacement-password-42!").ErrorCode);
            using (var context = database.CreateContext())
            {
                var user = context.User.Single();
                Assert.IsTrue(user.MustChangePassword);
                Assert.AreEqual(2, user.FailedLoginCount);
                Assert.AreEqual(WorkDayStart, user.LastLogin);
                Assert.IsTrue(new Pbkdf2PasswordHasher().VerifyHash(InitialPassword, user.PasswordHash, user.PasswordSalt));
            }
            if (!deleted && isAdmin)
            {
                var admin = new AdminMainDataService(database.CreateContext, new Pbkdf2PasswordHasher());
                AssertSucceeded(admin.UpdateTenant(new UpdateTenantRequest
                {
                    IdTenant = TenantId, IdActingUser = AdminUserId, TenantName = "Reactivated", IsActive = true
                }));
                Assert.IsTrue(AssertSucceeded(auth.Authenticate(new AuthenticateUserRequest
                {
                    IdTenant = TenantId, UserIdentOrEmail = "integration-admin", Password = InitialPassword
                })).MustChangePassword);
            }
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Authentication_ActiveTenantPreservesForcedPasswordReplacement(bool isAdmin)
        {
            ConfigurePasswordUser(isAdmin);
            var auth = new AuthenticationService(database.CreateContext, new Pbkdf2PasswordHasher());
            var initial = AssertSucceeded(auth.Authenticate(new AuthenticateUserRequest
            {
                IdTenant = TenantId, UserIdentOrEmail = "integration-admin", Password = InitialPassword
            }));
            Assert.IsTrue(initial.MustChangePassword);
            Assert.AreEqual(isAdmin, initial.User.IsAdmin);
            var changed = AssertSucceeded(auth.ChangeTemporaryPassword(
                TenantId, AdminUserId, InitialPassword, "Replacement-password-42!"));
            Assert.IsFalse(changed.MustChangePassword);
            Assert.IsTrue(changed.PasswordChangedAt.HasValue);
            Assert.AreEqual("InvalidCredentials", auth.Authenticate(new AuthenticateUserRequest
            {
                IdTenant = TenantId, UserIdentOrEmail = "integration-admin", Password = InitialPassword
            }).ErrorCode);
            var signedIn = AssertSucceeded(auth.Authenticate(new AuthenticateUserRequest
            {
                IdTenant = TenantId, UserIdentOrEmail = "integration-admin", Password = "Replacement-password-42!"
            }));
            Assert.IsFalse(signedIn.MustChangePassword);
            Assert.AreEqual(isAdmin, signedIn.User.IsAdmin);
            Assert.AreEqual(0, signedIn.User.FailedLoginCount);
        }

        private void ConfigurePasswordUser(bool isAdmin)
        {
            var hash = new Pbkdf2PasswordHasher().CreateHash(InitialPassword);
            using (var context = database.CreateContext())
            {
                var user = context.User.Single();
                user.IsAdmin = isAdmin;
                user.PasswordHash = hash.Hash;
                user.PasswordSalt = hash.Salt;
                user.MustChangePassword = true;
                user.PreliminaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
                user.FailedLoginCount = 2;
                user.LastLogin = WorkDayStart;
                context.SaveChanges();
            }
        }
    }
}
