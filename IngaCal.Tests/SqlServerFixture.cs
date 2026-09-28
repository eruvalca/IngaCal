using Testcontainers.MsSql;

namespace IngaCal.Tests;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;
    public string ConnectionString => container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
                .WithEnvironment("MSSQL_PID", "EnterpriseDeveloper")
                .Build();
            await container.StartAsync();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Database tests require Docker running with Linux containers and access to the SQL Server 2025 image. No tests are skipped or redirected to the app database.", e);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}
