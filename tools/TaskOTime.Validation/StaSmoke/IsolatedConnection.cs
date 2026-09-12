using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace TaskOTime.Validation;

internal static class IsolatedConnection
{
    internal const string OwnerProperty = "TaskOTime.Validation.Owner";

    internal static SqlConnectionStringBuilder Validate(string connectionString, string owner)
    {
        if (!Guid.TryParseExact(owner, "N", out var token) || token == Guid.Empty)
            throw new ArgumentException("An explicit nonempty fixture ownership token is required.");
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connection.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(connection.InitialCatalog, @"\ATaskOTime_Validation_[0-9a-f]{32}\z") ||
            !connection.IntegratedSecurity || connection.UserID.Length != 0 || connection.Password.Length != 0 ||
            connection.AttachDBFilename.Length != 0 || connection.UserInstance ||
            connection.FailoverPartner.Length != 0 ||
            connection.Authentication != SqlAuthenticationMethod.NotSpecified)
            throw new ArgumentException("Only an explicitly named, GUID-suffixed, integrated-security LocalDB fixture is accepted.");

        // Reconstruct rather than forward routing/authentication options to a second provider.
        return new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = connection.InitialCatalog,
            IntegratedSecurity = true,
            Pooling = false,
            ConnectTimeout = 15,
            Encrypt = SqlConnectionEncryptOption.Optional
        };
    }

    internal static void VerifyOwnership(SqlConnectionStringBuilder connectionString, string owner)
    {
        using var connection = new SqlConnection(connectionString.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COUNT(*) FROM sys.extended_properties
WHERE class = 0 AND name = @property AND CONVERT(nvarchar(128), value) = @owner";
        command.Parameters.AddWithValue("@property", OwnerProperty);
        command.Parameters.AddWithValue("@owner", owner);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Refusing smoke: database ownership marker does not match.");
    }
}
