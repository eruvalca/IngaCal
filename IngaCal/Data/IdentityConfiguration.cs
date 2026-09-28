using Microsoft.AspNetCore.Identity;

namespace IngaCal.Data;

public static class IdentityConfiguration
{
    public static void Configure(IdentityOptions options)
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    }
}
