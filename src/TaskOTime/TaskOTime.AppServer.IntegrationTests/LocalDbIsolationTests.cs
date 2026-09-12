using System;
using System.Data.SqlClient;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TaskOTime.AppServer.IntegrationTests
{
    [TestClass]
    public sealed class LocalDbGuardrailTests
    {
        [TestMethod]
        public void UncreatedFixturesHaveUniqueNamesAndCannotProvideContexts()
        {
            using (var first = new LocalDbPersistenceTestDatabase())
            using (var second = new LocalDbPersistenceTestDatabase())
            {
                Assert.AreNotEqual(first.DatabaseName, second.DatabaseName);
                Assert.AreNotEqual(first.OwnerToken, second.OwnerToken);
                StringAssert.StartsWith(first.DatabaseName, LocalDbPersistenceTestDatabase.Prefix);
                Assert.IsTrue(Guid.TryParseExact(first.DatabaseName.Substring(LocalDbPersistenceTestDatabase.Prefix.Length), "N", out _));
                Assert.ThrowsException<InvalidOperationException>(() => first.CreateContext());
                first.Dispose();
                first.Dispose();
                Assert.ThrowsException<InvalidOperationException>(() => first.Create());
            }
        }

        [TestMethod]
        public void GenerationScriptIsSchemaOnlyAfterValidatedPreambleRemoval()
        {
            var script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql"));
            var schema = string.Join("\n", LocalDbPersistenceTestDatabase.PrepareSchema(script));
            Assert.IsFalse(schema.Contains("[TaskOTime]"));
            Assert.IsFalse(schema.Contains("DB_ID("));
            Assert.IsFalse(schema.Contains("CREATE DATABASE"));
            StringAssert.Contains(schema, "CREATE TABLE [dbo].[Tenant]");
        }

        [DataTestMethod]
        [DataRow("USE [TaskOTime];")]
        [DataRow("USE \"TaskOTime\";")]
        [DataRow("USE [TaskOTime_AppServerIntegrationTests];")]
        [DataRow("EXEC(N'USE [TaskOTime]');")]
        [DataRow("ALTER DATABASE [other] SET SINGLE_USER;")]
        [DataRow("SELECT DB_ID(N'TaskOTime');")]
        public void UnexpectedDatabaseRoutingIsRejectedBeforeConnecting(string unsafeSql)
        {
            var script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql"));
            Assert.ThrowsException<InvalidOperationException>(() =>
                LocalDbPersistenceTestDatabase.PrepareSchema(script + "\nGO\n" + unsafeSql));
        }

        [TestMethod]
        public void ChangedQuotedPreambleFailsClosedInsteadOfPartialRewriting()
        {
            var script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql"));
            Assert.ThrowsException<InvalidOperationException>(() =>
                LocalDbPersistenceTestDatabase.PrepareSchema(script.Replace("[TaskOTime]", "\"TaskOTime\"")));
        }
    }

    [TestClass]
    [TestCategory("IsolatedSql")]
    public sealed class LocalDbIsolationTests
    {
        [TestMethod]
        public void SchemaInitializationFailureCleansUpTheOwnedDatabase()
        {
            var script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql"));
            using (var fixture = new LocalDbPersistenceTestDatabase())
            {
                Assert.ThrowsException<SqlException>(() =>
                    fixture.CreateFromScript(script + "\nGO\nTHROW 51001, 'Intentional isolated schema failure', 1;"));
                Assert.IsFalse(Exists(fixture.DatabaseName));
                Assert.ThrowsException<InvalidOperationException>(() => fixture.CreateContext());
            }
        }

        [TestMethod]
        public void CreateCollisionDoesNotAdoptOrResetAnExistingDatabase()
        {
            var id = Guid.NewGuid();
            using (var owner = new LocalDbPersistenceTestDatabase(id))
            {
                owner.Create();
                using (var collision = new LocalDbPersistenceTestDatabase(id))
                {
                    Assert.ThrowsException<SqlException>(() => collision.Create());
                    collision.Dispose();
                }
                Assert.IsTrue(Exists(owner.DatabaseName));
                using (var connection = new SqlConnection(owner.ProviderConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class = 0 AND name = @name";
                        command.Parameters.AddWithValue("@name", LocalDbPersistenceTestDatabase.OwnershipProperty);
                        Assert.AreEqual(owner.OwnerToken, command.ExecuteScalar());
                    }
                }
            }
        }

        [TestMethod]
        public void IndependentFixturesCleanUpOnlyTheirOwnedDatabase()
        {
            var first = new LocalDbPersistenceTestDatabase();
            var second = new LocalDbPersistenceTestDatabase();
            try
            {
                first.Create();
                second.Create();
                Assert.IsTrue(Exists(first.DatabaseName));
                Assert.IsTrue(Exists(second.DatabaseName));
                Assert.ThrowsException<InvalidOperationException>(() => first.Create());
                first.Dispose();
                Assert.IsFalse(Exists(first.DatabaseName));
                Assert.IsTrue(Exists(second.DatabaseName));
                using (var connection = new SqlConnection(second.ProviderConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM dbo.Tenant";
                        Assert.AreEqual(0, Convert.ToInt32(command.ExecuteScalar()));
                    }
                }
                second.Dispose();
                second.Dispose();
                Assert.IsFalse(Exists(second.DatabaseName));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [TestMethod]
        public void CleanupRefusesDatabaseWithChangedOwnershipMarker()
        {
            var fixture = new LocalDbPersistenceTestDatabase();
            fixture.Create();
            try
            {
                SetOwner(fixture, Guid.NewGuid().ToString("N"));
                Assert.ThrowsException<SqlException>(() => fixture.Dispose());
                Assert.IsTrue(Exists(fixture.DatabaseName));
            }
            finally
            {
                SetOwner(fixture, fixture.OwnerToken);
                fixture.Dispose();
            }
            Assert.IsFalse(Exists(fixture.DatabaseName));
        }

        private static void SetOwner(LocalDbPersistenceTestDatabase fixture, string owner)
        {
            using (var connection = new SqlConnection(fixture.ProviderConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "EXEC sys.sp_updateextendedproperty @name, @value";
                    command.Parameters.AddWithValue("@name", LocalDbPersistenceTestDatabase.OwnershipProperty);
                    command.Parameters.AddWithValue("@value", owner);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static bool Exists(string name)
        {
            using (var connection = LocalDbPersistenceTestDatabase.OpenMasterConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DB_ID(@name)";
                command.Parameters.AddWithValue("@name", name);
                return command.ExecuteScalar() != DBNull.Value;
            }
        }
    }
}
