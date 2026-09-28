using Microsoft.Data.Sqlite;
namespace IngaCal.Data;

public sealed record DatabaseStorage(string ConnectionString, string DatabasePath, string KeyDirectory)
{
    public static DatabaseStorage Configure(IConfiguration configuration, string contentRoot)
    {
        var connection = new SqliteConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=Data/app.db");
        if (connection.Mode == SqliteOpenMode.Memory || connection.DataSource == ":memory:" || string.IsNullOrWhiteSpace(connection.DataSource))
            throw new InvalidOperationException("IngaCal requires a persistent SQLite database path.");
        var path = Path.GetFullPath(connection.DataSource, contentRoot);
        connection.DataSource = path;
        connection.ForeignKeys = true;
        connection.Cache = SqliteCacheMode.Default;
        var directory = Path.GetDirectoryName(path)!;
        var keys = Path.GetFullPath(configuration["Storage:DataProtectionPath"] ?? Path.Combine(directory, "keys"), contentRoot);
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(keys);
            foreach (var writable in new[] { directory, keys }.Distinct())
            {
                var probe = Path.Combine(writable, $".write-check-{Guid.NewGuid():N}");
                using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            if (File.Exists(path))
            {
                using var database = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException($"The SQLite data directory '{directory}' or key directory '{keys}' is not writable. Configure a persistent writable location.", e); }
        return new(connection.ToString(), path, keys);
    }

    public static void Backup(string connectionString, string destination)
    {
        if (File.Exists(destination)) throw new IOException("The backup destination already exists. Choose a new filename.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var sourceOptions = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly };
        using var source = new SqliteConnection(sourceOptions.ToString());
        source.Open();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }
}
