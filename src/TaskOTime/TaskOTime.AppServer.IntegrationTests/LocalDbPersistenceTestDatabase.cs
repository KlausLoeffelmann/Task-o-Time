using System;
using System.Collections.Generic;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TaskOTime.DataLayer;

namespace TaskOTime.AppServer.IntegrationTests
{
    internal sealed class LocalDbPersistenceTestDatabase : IDisposable
    {
        internal const string Prefix = "TaskOTime_Validation_";
        internal const string OwnershipProperty = "TaskOTime.Validation.Owner";
        private const string ServerConnectionString = @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;Pooling=False;Connect Timeout=15";
        private bool created;
        private bool disposed;

        public string OwnerToken { get; } = Guid.NewGuid().ToString("N");
        public string DatabaseName { get; }
        public LocalDbPersistenceTestDatabase() : this(Guid.NewGuid()) { }

        internal LocalDbPersistenceTestDatabase(Guid databaseId)
        {
            if (databaseId == Guid.Empty)
                throw new ArgumentException("A nonempty run database identifier is required.", nameof(databaseId));
            DatabaseName = Prefix + databaseId.ToString("N");
        }
        public string ProviderConnectionString => new SqlConnectionStringBuilder(ServerConnectionString)
        {
            InitialCatalog = DatabaseName
        }.ConnectionString;

        public void Create()
        {
            CreateFromScript(File.ReadAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql"), Encoding.UTF8));
        }

        internal void CreateFromScript(string script)
        {
            if (disposed || created)
                throw new InvalidOperationException("A fixture may create its database exactly once.");

            // Validate before connecting. The legacy script can reset an existing database;
            // only its schema body may run, and only after our CREATE DATABASE succeeds.
            var batches = PrepareSchema(script);
            using (var connection = OpenMasterConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE DATABASE [" + DatabaseName + "];";
                command.CommandTimeout = 60;
                command.ExecuteNonQuery();
                created = true;
            }

            try
            {
                using (var connection = new SqlConnection(ProviderConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "EXEC sys.sp_addextendedproperty @name, @value;";
                        command.Parameters.AddWithValue("@name", OwnershipProperty);
                        command.Parameters.AddWithValue("@value", OwnerToken);
                        command.ExecuteNonQuery();
                    }
                    foreach (var batch in batches)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = batch;
                            command.CommandTimeout = 60;
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public TaskOTimeContext CreateContext()
        {
            if (!created || disposed)
                throw new InvalidOperationException("The owned database is not active.");
            var model = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "TaskOTime");
            return new TaskOTimeContext(new EntityConnectionStringBuilder
            {
                Metadata = model + ".csdl|" + model + ".ssdl|" + model + ".msl",
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = ProviderConnectionString
            }.ConnectionString);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            if (created)
            {
                using (var connection = OpenMasterConnection())
                using (var command = connection.CreateCommand())
                {
                    // Prefix matching is not ownership. A persisted, unpredictable token
                    // is required as well; missing/replaced markers fail closed.
                    command.CommandText = @"
IF DB_ID(@database) IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [" + DatabaseName + @"].sys.extended_properties
        WHERE class = 0 AND name = @property AND CONVERT(nvarchar(128), value) = @owner)
        THROW 51000, 'Refusing cleanup: database ownership marker does not match.', 1;
    ALTER DATABASE [" + DatabaseName + @"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [" + DatabaseName + @"];
END";
                    command.Parameters.AddWithValue("@database", DatabaseName);
                    command.Parameters.AddWithValue("@property", OwnershipProperty);
                    command.Parameters.AddWithValue("@owner", OwnerToken);
                    command.CommandTimeout = 60;
                    command.ExecuteNonQuery();
                }
            }
            disposed = true;
        }

        internal static string[] PrepareSchema(string script)
        {
            var batches = SplitSqlBatches(script).ToArray();
            // Deliberately reject an unfamiliar preamble rather than doing broad string
            // substitutions (which previously missed quoted database names).
            if (batches.Length < 4 ||
                !Regex.IsMatch(batches[0], @"\A\s*/\*.*?\*/\s*IF DB_ID\(N'TaskOTime'\) IS NULL\s+BEGIN\s+CREATE DATABASE \[TaskOTime\];\s+END\s*\z", RegexOptions.Singleline) ||
                batches[1].Trim() != "ALTER DATABASE [TaskOTime] SET COMPATIBILITY_LEVEL = 110;" ||
                batches[2].Trim() != "USE [TaskOTime];")
                throw new InvalidOperationException("Unrecognized generation script database preamble.");

            var schema = batches.Skip(3).ToArray();
            foreach (var batch in schema)
            {
                if (Regex.IsMatch(batch, @"\bUSE\s|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b|\bDB_ID\s*\(|(?:\[TaskOTime\]|""TaskOTime""|'TaskOTime')", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Database routing/DDL is forbidden in the schema body.");
            }
            return new[] { "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 110;" }.Concat(schema).ToArray();
        }

        internal static SqlConnection OpenMasterConnection()
        {
            var connection = new SqlConnection(new SqlConnectionStringBuilder(ServerConnectionString)
            {
                InitialCatalog = "master"
            }.ConnectionString);
            connection.Open();
            return connection;
        }

        private static IEnumerable<string> SplitSqlBatches(string script)
        {
            return Regex.Split(script, @"^\s*GO\s*(?:--[^\r\n]*)?\r?$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(batch => !string.IsNullOrWhiteSpace(batch));
        }
    }
}
