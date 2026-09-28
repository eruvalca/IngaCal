using IngaCal.Data;

namespace IngaCal.Tests.Data;

public sealed class SqlServerConnectionStringTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Data Source=Data/app.db")]
    [InlineData("Server=localhost;Password=DoNotExpose123!")]
    [InlineData("Database=IngaCalDev")]
    [InlineData("Server=localhost;Database=IngaCalDev;UnknownOption=DoNotExpose123!")]
    public void Validate_InvalidConfiguration_RejectsWithoutLeakingCredentials(string? connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SqlServerConnectionString.Validate(connection));
        Assert.Contains("ConnectionStrings:DefaultConnection", error.Message);
        Assert.DoesNotContain("DoNotExpose123!", error.ToString());
    }
}
