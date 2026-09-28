using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IngaCal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ActivityTag> ActivityTags => Set<ActivityTag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // WebAuthn credential IDs can exceed SQL Server's 900-byte clustered key limit.
        builder.Entity<IdentityUserPasskey<string>>().HasKey(x => x.CredentialId).IsClustered(false);
        builder.Entity<Activity>(entity =>
        {
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.StartUtc).HasColumnType("datetime2(7)").HasConversion(x => x.ToUniversalTime(), x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
            entity.Property(x => x.EndUtc).HasColumnType("datetime2(7)").HasConversion(x => x.ToUniversalTime(), x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
            entity.HasIndex(x => new { x.UserId, x.StartUtc });
            entity.ToTable(t => t.HasCheckConstraint("CK_Activity_PositiveDuration", "[EndUtc] > [StartUtc]"));
        });
        builder.Entity<Tag>(entity =>
        {
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.Name).HasMaxLength(60);
            entity.Property(x => x.NormalizedName).HasMaxLength(60).UseCollation("Latin1_General_100_BIN2");
            entity.Property(x => x.Color).HasMaxLength(7);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.NormalizedName }).IsUnique();
        });
        builder.Entity<ActivityTag>().HasKey(x => new { x.ActivityId, x.TagId });
        builder.Entity<ActivityTag>().HasOne(x => x.Activity).WithMany(x => x.ActivityTags).HasForeignKey(x => x.ActivityId);
        builder.Entity<ActivityTag>().HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.NoAction);
    }
}
