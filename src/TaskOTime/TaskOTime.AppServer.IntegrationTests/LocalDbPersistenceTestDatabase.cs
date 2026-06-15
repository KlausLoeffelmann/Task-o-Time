using System;
using System.Collections.Generic;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TaskOTime.DataLayer;

namespace TaskOTime.AppServer.IntegrationTests
{
    internal static class LocalDbPersistenceTestDatabase
    {
        private const string DatabaseName = "TaskOTime_AppServerIntegrationTests";
        private const string ServerConnectionString = @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;MultipleActiveResultSets=True";

        public static void Reset()
        {
            DropDatabaseIfExists();
            ExecuteScript(RewriteDatabaseName(File.ReadAllText(FindGenerationScript(), Encoding.UTF8)));
        }

        public static TaskOTimeContext CreateContext()
        {
            return new TaskOTimeContext(CreateEntityConnectionString());
        }

        private static string CreateEntityConnectionString()
        {
            var providerBuilder = new SqlConnectionStringBuilder(ServerConnectionString)
            {
                InitialCatalog = DatabaseName
            };

            var entityBuilder = new EntityConnectionStringBuilder
            {
                Metadata = @".\Model\TaskOTime.csdl|.\Model\TaskOTime.ssdl|.\Model\TaskOTime.msl",
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = providerBuilder.ConnectionString
            };

            return entityBuilder.ConnectionString;
        }

        private static void DropDatabaseIfExists()
        {
            using (var connection = OpenMasterConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
IF DB_ID(N'TaskOTime_AppServerIntegrationTests') IS NOT NULL
BEGIN
    ALTER DATABASE [TaskOTime_AppServerIntegrationTests] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [TaskOTime_AppServerIntegrationTests];
END";
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }
        }

        private static string FindGenerationScript()
        {
            var outputScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql");
            if (File.Exists(outputScript))
            {
                return outputScript;
            }

            var sourceTreeScript = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\TaskOTime.DataLayer\DbGenerationScript.sql"));
            if (File.Exists(sourceTreeScript))
            {
                return sourceTreeScript;
            }

            throw new FileNotFoundException("DbGenerationScript.sql was not found for integration tests.", outputScript);
        }

        private static string RewriteDatabaseName(string script)
        {
            return script
                .Replace("DB_ID(N'TaskOTime')", "DB_ID(N'TaskOTime_AppServerIntegrationTests')")
                .Replace("[TaskOTime]", "[TaskOTime_AppServerIntegrationTests]");
        }

        private static void ExecuteScript(string script)
        {
            using (var connection = OpenMasterConnection())
            {
                foreach (var batch in SplitSqlBatches(script))
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = batch;
                        command.CommandTimeout = 0;
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        private static SqlConnection OpenMasterConnection()
        {
            var builder = new SqlConnectionStringBuilder(ServerConnectionString)
            {
                InitialCatalog = "master"
            };

            var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private static IEnumerable<string> SplitSqlBatches(string script)
        {
            var builder = new StringBuilder();
            using (var reader = new StringReader(script))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (Regex.IsMatch(line, @"^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase))
                    {
                        var batch = builder.ToString();
                        if (!string.IsNullOrWhiteSpace(batch))
                        {
                            yield return batch;
                        }

                        builder.Clear();
                    }
                    else
                    {
                        builder.AppendLine(line);
                    }
                }
            }

            var finalBatch = builder.ToString();
            if (!string.IsNullOrWhiteSpace(finalBatch))
            {
                yield return finalBatch;
            }
        }
    }
}
