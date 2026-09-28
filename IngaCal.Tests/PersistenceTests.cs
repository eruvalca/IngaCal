using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IngaCal.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Database")]
public sealed class PersistenceTests(SqlServerFixture server)
{
    [Theory]
    [InlineData(32)]
    [InlineData(1023)]
    public async Task Save_Passkey_RoundTripsCredentialAndJsonData(int credentialLength)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var credential = Enumerable.Repeat((byte)42, credentialLength).ToArray();
        await using (var db = journal.CreateDbContext())
        {
            db.UserPasskeys.Add(new IdentityUserPasskey<string>
            {
                UserId = SqlServerJournal.Alice,
                CredentialId = credential,
                Data = new IdentityPasskeyData
                {
                    Name = "Security key", PublicKey = [3, 4], CreatedAt = SqlServerJournal.Morning,
                    SignCount = 12, Transports = ["usb"], IsUserVerified = true,
                    AttestationObject = [5, 6], ClientDataJson = [7, 8]
                }
            });
            await db.SaveChangesAsync();
        }
        await using var reopened = journal.CreateDbContext();
        var passkey = await reopened.UserPasskeys.SingleAsync();
        Assert.Equal(credential, passkey.CredentialId);
        Assert.Equal(SqlServerJournal.Alice, passkey.UserId);
        Assert.Equal("Security key", passkey.Data.Name);
        Assert.Equal(new byte[] { 3, 4 }, passkey.Data.PublicKey);
        Assert.Equal(12u, passkey.Data.SignCount);
        Assert.Equal(SqlServerJournal.Morning, passkey.Data.CreatedAt);
        Assert.Equal("usb", Assert.Single(passkey.Data.Transports!));
        Assert.True(passkey.Data.IsUserVerified);
    }

    [Fact]
    public async Task Save_InvalidDuration_DatabaseConstraintRejectsDirectWrite()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        await using var db = journal.CreateDbContext();
        db.Activities.Add(new Activity { UserId = SqlServerJournal.Alice, StartUtc = SqlServerJournal.Morning.UtcDateTime, EndUtc = SqlServerJournal.Morning.UtcDateTime });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
    }

    [Fact]
    public async Task Migrate_FreshDatabase_CreatesJournalAndSurvivesReopening()
    {
        await using var journal = new SqlServerJournal(server);
        await using (var db = journal.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new ApplicationUser { Id = SqlServerJournal.Alice, UserName = "Alice" });
            await db.SaveChangesAsync();
        }
        var tag = await journal.Tags().SaveAsync(new(null, "Persistent", "#123456"));
        var activity = await journal.Activities().SaveAsync(SqlServerJournal.Edit(tags: [tag.Id], title: "Saved before restart"));

        // Independent connections and contexts approximate an application restart.
        await using (var reopened = journal.CreateDbContext())
        {
            await reopened.Database.MigrateAsync();
            Assert.Empty(await reopened.Database.GetPendingMigrationsAsync());
            Assert.Single(await reopened.Database.GetAppliedMigrationsAsync());
            Assert.False(reopened.Database.HasPendingModelChanges());
            var persisted = await reopened.Activities.Include(x => x.ActivityTags).SingleAsync();
            Assert.Equal(activity.Id, persisted.Id);
            Assert.Equal("Saved before restart", persisted.Title);
            Assert.Equal(tag.Id, Assert.Single(persisted.ActivityTags).TagId);
            Assert.Equal(activity.Start.UtcDateTime, persisted.StartUtc);
            Assert.Equal(DateTimeKind.Utc, persisted.EndUtc.Kind);
        }
    }

    [Fact]
    public async Task Migrate_RepeatedMigration_PreservesAccountCredentialsClaimsTokensAndPasskeys()
    {
        await using var journal = new SqlServerJournal(server);
        byte[] credentialId = [1, 2, 3, 4];
        const string passkeyData = "{\"Name\":\"Existing passkey\"}";
        await using (var legacy = journal.CreateDbContext())
        {
            await legacy.Database.MigrateAsync();
            legacy.Users.Add(new ApplicationUser
            {
                Id = SqlServerJournal.Alice,
                UserName = "existing@example.test",
                Email = "existing@example.test",
                PasswordHash = "existing-password-hash",
                SecurityStamp = "existing-security-stamp",
                TwoFactorEnabled = true
            });
            legacy.UserClaims.Add(new IdentityUserClaim<string> { UserId = SqlServerJournal.Alice, ClaimType = "custom", ClaimValue = "retained" });
            legacy.UserTokens.Add(new IdentityUserToken<string> { UserId = SqlServerJournal.Alice, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "existing-authenticator-key" });
            await legacy.SaveChangesAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUserPasskeys (CredentialId, UserId, Data) VALUES ({credentialId}, {SqlServerJournal.Alice}, {passkeyData})");
        }

        await using (var upgraded = journal.CreateDbContext())
        {
            await upgraded.Database.MigrateAsync();
            var account = await upgraded.Users.SingleAsync();
            Assert.Equal("existing@example.test", account.Email);
            Assert.Equal("existing-password-hash", account.PasswordHash);
            Assert.Equal("existing-security-stamp", account.SecurityStamp);
            Assert.True(account.TwoFactorEnabled);
            Assert.Equal("retained", (await upgraded.UserClaims.SingleAsync()).ClaimValue);
            Assert.Equal("existing-authenticator-key", (await upgraded.UserTokens.SingleAsync()).Value);
            Assert.Equal(passkeyData, await upgraded.Database.SqlQuery<string>($"SELECT Data AS Value FROM AspNetUserPasskeys WHERE CredentialId = {credentialId}").SingleAsync());
            Assert.Empty(await upgraded.Activities.ToListAsync());
            Assert.Empty(await upgraded.Tags.ToListAsync());
        }
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit());
        Assert.Equal(saved.Id, Assert.Single(await journal.Activities().ListAsync(saved.Start, saved.End)).Id);
    }

    [Fact]
    public async Task DeleteAccount_CascadesOwnedJournalDataAndKeepsOtherAccount()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var aliceTag = await journal.Tags().SaveAsync(new(null, "Alice", "#123456"));
        var bobTag = await journal.Tags(SqlServerJournal.Bob).SaveAsync(new(null, "Bob", "#abcdef"));
        await journal.Activities().SaveAsync(SqlServerJournal.Edit(tags: [aliceTag.Id]));
        var other = await journal.Activities(SqlServerJournal.Bob).SaveAsync(SqlServerJournal.Edit(tags: [bobTag.Id]));
        await using (var db = journal.CreateDbContext())
        {
            await db.Users.Where(x => x.Id == SqlServerJournal.Alice).ExecuteDeleteAsync();
        }

        await using var verify = journal.CreateDbContext();
        Assert.Equal(SqlServerJournal.Bob, (await verify.Users.SingleAsync()).Id);
        Assert.Equal(other.Id, (await verify.Activities.SingleAsync()).Id);
        Assert.Equal(bobTag.Id, (await verify.Tags.SingleAsync()).Id);
        Assert.Equal(bobTag.Id, (await verify.ActivityTags.SingleAsync()).TagId);
    }
}
