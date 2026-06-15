using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskOTime.DataLayer
{
    public sealed class TaskOTimeDatabaseAdmin
    {
        private const string DatabaseName = "TaskOTime";
        private readonly string _serverConnectionString;

        public TaskOTimeDatabaseAdmin()
            : this(@"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;MultipleActiveResultSets=True")
        {
        }

        public TaskOTimeDatabaseAdmin(string serverConnectionString)
        {
            if (string.IsNullOrWhiteSpace(serverConnectionString))
            {
                throw new ArgumentException("A SQL Server connection string is required.", nameof(serverConnectionString));
            }

            _serverConnectionString = serverConnectionString;
        }

        public void ResetDatabase(string generationScriptPath)
        {
            if (string.IsNullOrWhiteSpace(generationScriptPath))
            {
                throw new ArgumentException("A database generation script path is required.", nameof(generationScriptPath));
            }

            if (!File.Exists(generationScriptPath))
            {
                throw new FileNotFoundException("The database generation script was not found.", generationScriptPath);
            }

            using (var connection = OpenMasterConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
IF DB_ID(N'TaskOTime') IS NOT NULL
BEGIN
    ALTER DATABASE [TaskOTime] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [TaskOTime];
END";
                    command.ExecuteNonQuery();
                }
            }

            ExecuteScript(File.ReadAllText(generationScriptPath, Encoding.UTF8));
        }

        public void BackupDatabase(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentException("A backup file path is required.", nameof(backupPath));
            }

            var fullPath = Path.GetFullPath(backupPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("The backup target directory does not exist: " + directory);
            }

            using (var connection = OpenMasterConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "BACKUP DATABASE [TaskOTime] TO DISK = @backupPath WITH INIT";
                command.Parameters.Add("@backupPath", SqlDbType.NVarChar, 4000).Value = fullPath;
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }
        }

        public IReadOnlyList<string> ExportTables(string outputDirectory, IEnumerable<string> requestedTables)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("An export output directory is required.", nameof(outputDirectory));
            }

            Directory.CreateDirectory(outputDirectory);
            var exportedFiles = new List<string>();

            using (var connection = OpenDatabaseConnection())
            {
                var availableTables = GetUserTables(connection);
                var tables = ResolveRequestedTables(availableTables, requestedTables);

                foreach (var table in tables)
                {
                    var filePath = Path.Combine(outputDirectory, table + ".csv");
                    ExportTable(connection, table, filePath);
                    exportedFiles.Add(filePath);
                }
            }

            return exportedFiles;
        }

        private void ExecuteScript(string script)
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

        private SqlConnection OpenMasterConnection()
        {
            var builder = new SqlConnectionStringBuilder(_serverConnectionString)
            {
                InitialCatalog = "master"
            };

            var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private SqlConnection OpenDatabaseConnection()
        {
            var builder = new SqlConnectionStringBuilder(_serverConnectionString)
            {
                InitialCatalog = DatabaseName
            };

            var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private static IReadOnlyList<string> GetUserTables(SqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT [name]
FROM sys.tables
WHERE [schema_id] = SCHEMA_ID(N'dbo')
ORDER BY [name];";
                using (var reader = command.ExecuteReader())
                {
                    var tables = new List<string>();
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }

                    return tables;
                }
            }
        }

        private static IReadOnlyList<string> ResolveRequestedTables(IReadOnlyList<string> availableTables, IEnumerable<string> requestedTables)
        {
            var requested = requestedTables == null
                ? availableTables
                : requestedTables.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();

            var available = new HashSet<string>(availableTables, StringComparer.OrdinalIgnoreCase);
            var resolved = new List<string>();
            foreach (var table in requested)
            {
                if (!available.Contains(table))
                {
                    throw new InvalidOperationException("The table '" + table + "' does not exist in dbo.");
                }

                resolved.Add(availableTables.First(t => string.Equals(t, table, StringComparison.OrdinalIgnoreCase)));
            }

            return resolved;
        }

        private static void ExportTable(SqlConnection connection, string table, string filePath)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM [dbo].[" + table.Replace("]", "]]") + "]";
                using (var reader = command.ExecuteReader())
                using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
                {
                    WriteCsvRow(writer, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));

                    while (reader.Read())
                    {
                        var values = Enumerable.Range(0, reader.FieldCount)
                            .Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
                        WriteCsvRow(writer, values);
                    }
                }
            }
        }

        private static void WriteCsvRow(TextWriter writer, IEnumerable<string> values)
        {
            writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
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
