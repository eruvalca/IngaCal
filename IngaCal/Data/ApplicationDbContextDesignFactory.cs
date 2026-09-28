using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IngaCal.Data;

public sealed class ApplicationDbContextDesignFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Identity's model reads store options from the application's service provider.
    private static readonly ServiceProvider IdentityServices = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityConfiguration.Configure).BuildServiceProvider();

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ApplicationDbContextDesignFactory>()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var connection = SqlServerConnectionString.Validate(configuration.GetConnectionString("DefaultConnection"));
        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).UseApplicationServiceProvider(IdentityServices).Options);
    }
}
