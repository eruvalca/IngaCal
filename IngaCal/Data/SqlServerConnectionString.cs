using Microsoft.Data.SqlClient;

namespace IngaCal.Data;

public static class SqlServerConnectionString
{
    public static string Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection with a SQL Server connection string using user secrets or environment variables.");
        SqlConnectionStringBuilder connection;
        try { connection = new(value); }
        catch (ArgumentException)
        {
            // Do not include supplied credentials in diagnostics.
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be a valid SQL Server connection string.");
        }
        if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must specify a SQL Server and Database.");
        return connection.ConnectionString;
    }
}
