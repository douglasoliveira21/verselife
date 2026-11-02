using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Data;

public sealed class LifeVerseDbContext(DbContextOptions<LifeVerseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<Island> Islands => Set<Island>();
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users", "identity");
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).HasMaxLength(32);
            b.Property(x => x.NormalizedUsername).HasMaxLength(32);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.NormalizedEmail).HasMaxLength(254);
            b.Property(x => x.PasswordHash).HasMaxLength(512);
            b.HasIndex(x => x.NormalizedUsername).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("deleted_at IS NULL");
        });
        modelBuilder.Entity<Session>(b =>
        {
            b.ToTable("sessions", "identity");
            b.HasKey(x => x.Id);
            b.Property(x => x.RefreshTokenHash).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(512);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => x.RefreshTokenHash).IsUnique();
            b.HasIndex(x => new { x.UserId, x.ExpiresAt });
            b.HasIndex(x => x.FamilyId);
            b.HasOne(x => x.User).WithMany(x => x.Sessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Avatar>(b =>
        {
            b.ToTable("avatars", "identity");
            b.HasKey(x => x.Id);
            b.Property(x => x.DisplayName).HasMaxLength(64);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasOne(x => x.User).WithOne(x => x.Avatar).HasForeignKey<Avatar>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Island>(b =>
        {
            b.ToTable("islands", "world");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.Slug).HasMaxLength(80);
            b.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(x => x.OwnerUserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ServerInstance>(b =>
        {
            b.ToTable("server_instances", "platform");
            b.HasKey(x => x.Id);
            b.Property(x => x.Hostname).HasMaxLength(255);
            b.Property(x => x.Status).HasMaxLength(24);
            b.HasIndex(x => new { x.IslandId, x.Status });
            b.HasIndex(x => x.LastHeartbeatAt);
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value) => string.Concat(value.Select((c, i) =>
        char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
