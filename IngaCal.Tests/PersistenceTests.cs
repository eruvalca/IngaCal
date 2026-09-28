using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IngaCal.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Migrate_FreshDatabase_CreatesJournalAndSurvivesReopening()
    {
        await using var journal = new SqliteJournal();
        await using (var db = journal.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new ApplicationUser { Id = SqliteJournal.Alice, UserName = "Alice" });
            await db.SaveChangesAsync();
        }
        var tag = await journal.Tags().SaveAsync(new(null, "Persistent", "#123456"));
        var activity = await journal.Activities().SaveAsync(SqliteJournal.Edit(tags: [tag.Id], title: "Saved before restart"));

        // Independent connections and contexts approximate an application restart.
        await using (var reopened = journal.CreateDbContext())
        {
            await reopened.Database.MigrateAsync();
            Assert.Empty(await reopened.Database.GetPendingMigrationsAsync());
            Assert.True((await reopened.Database.GetAppliedMigrationsAsync()).Count() >= 2);
            var persisted = await reopened.Activities.Include(x => x.ActivityTags).SingleAsync();
            Assert.Equal(activity.Id, persisted.Id);
            Assert.Equal("Saved before restart", persisted.Title);
            Assert.Equal(tag.Id, Assert.Single(persisted.ActivityTags).TagId);
            Assert.Equal(activity.Start.UtcDateTime, persisted.StartUtc);
            Assert.Equal(DateTimeKind.Utc, persisted.EndUtc.Kind);
        }
    }

    [Fact]
    public async Task Migrate_ExistingIdentityDatabase_PreservesAccountCredentialsClaimsAndTokens()
    {
        await using var journal = new SqliteJournal();
        byte[] credentialId = [1, 2, 3, 4];
        const string passkeyData = "{\"Name\":\"Existing passkey\"}";
        await using (var legacy = journal.CreateDbContext())
        {
            await legacy.GetService<IMigrator>().MigrateAsync("00000000000000_CreateIdentitySchema");
            legacy.Users.Add(new ApplicationUser
            {
                Id = SqliteJournal.Alice,
                UserName = "existing@example.test",
                Email = "existing@example.test",
                PasswordHash = "existing-password-hash",
                SecurityStamp = "existing-security-stamp",
                TwoFactorEnabled = true
            });
            legacy.UserClaims.Add(new IdentityUserClaim<string> { UserId = SqliteJournal.Alice, ClaimType = "custom", ClaimValue = "retained" });
            legacy.UserTokens.Add(new IdentityUserToken<string> { UserId = SqliteJournal.Alice, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "existing-authenticator-key" });
            await legacy.SaveChangesAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUserPasskeys (CredentialId, UserId, Data) VALUES ({credentialId}, {SqliteJournal.Alice}, {passkeyData})");
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
        var saved = await journal.Activities().SaveAsync(SqliteJournal.Edit());
        Assert.Equal(saved.Id, Assert.Single(await journal.Activities().ListAsync(saved.Start, saved.End)).Id);
    }

    [Fact]
    public async Task DeleteAccount_CascadesOwnedJournalDataAndKeepsOtherAccount()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var aliceTag = await journal.Tags().SaveAsync(new(null, "Alice", "#123456"));
        var bobTag = await journal.Tags(SqliteJournal.Bob).SaveAsync(new(null, "Bob", "#abcdef"));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(tags: [aliceTag.Id]));
        var other = await journal.Activities(SqliteJournal.Bob).SaveAsync(SqliteJournal.Edit(tags: [bobTag.Id]));
        await using (var db = journal.CreateDbContext())
        {
            await db.Users.Where(x => x.Id == SqliteJournal.Alice).ExecuteDeleteAsync();
        }

        await using var verify = journal.CreateDbContext();
        Assert.Equal(SqliteJournal.Bob, (await verify.Users.SingleAsync()).Id);
        Assert.Equal(other.Id, (await verify.Activities.SingleAsync()).Id);
        Assert.Equal(bobTag.Id, (await verify.Tags.SingleAsync()).Id);
        Assert.Equal(bobTag.Id, (await verify.ActivityTags.SingleAsync()).TagId);
    }
}
