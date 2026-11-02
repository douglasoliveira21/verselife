using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Data;

public sealed class LifeVerseDbContext(DbContextOptions<LifeVerseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<AvatarAppearance> AvatarAppearances => Set<AvatarAppearance>();
    public DbSet<EquippedItem> EquippedItems => Set<EquippedItem>();
    public DbSet<Island> Islands => Set<Island>();
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<IslandPermission> IslandPermissions => Set<IslandPermission>();
    public DbSet<IslandSnapshot> IslandSnapshots => Set<IslandSnapshot>();
    public DbSet<IslandChange> IslandChanges => Set<IslandChange>();
    public DbSet<BuildObject> BuildObjects => Set<BuildObject>();
    public DbSet<BuildOperation> BuildOperations => Set<BuildOperation>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetVersion> AssetVersions => Set<AssetVersion>();
    public DbSet<InventoryFolder> InventoryFolders => Set<InventoryFolder>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<AssetPermission> AssetPermissions => Set<AssetPermission>();
    public DbSet<WorldAssetInstance> WorldAssetInstances => Set<WorldAssetInstance>();

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
            b.Property(x => x.BaseModel).HasMaxLength(32);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasOne(x => x.User).WithOne(x => x.Avatar).HasForeignKey<Avatar>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AvatarAppearance>(b =>
        {
            b.ToTable("avatar_appearances", "identity");
            b.HasKey(x => x.Id);
            b.Property(x => x.SkinColor).HasMaxLength(9);
            b.Property(x => x.HairColor).HasMaxLength(9);
            b.Property(x => x.HairStyle).HasMaxLength(32);
            b.Property(x => x.BodySettingsJson).HasColumnType("jsonb");
            b.HasIndex(x => x.AvatarId).IsUnique();
            b.HasOne(x => x.Avatar).WithOne(x => x.Appearance).HasForeignKey<AvatarAppearance>(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EquippedItem>(b =>
        {
            b.ToTable("equipped_items", "inventory");
            b.HasKey(x => x.Id);
            b.Property(x => x.Slot).HasMaxLength(24);
            b.Property(x => x.ItemKey).HasMaxLength(64);
            b.Property(x => x.TintColor).HasMaxLength(9);
            b.HasIndex(x => new { x.AvatarId, x.Slot }).IsUnique();
            b.HasOne(x => x.Avatar).WithMany(x => x.EquippedItems).HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Island>(b =>
        {
            b.ToTable("islands", "world");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.Slug).HasMaxLength(80);
            b.Property(x => x.Description).HasMaxLength(4000);
            b.Property(x => x.CoverImageUrl).HasMaxLength(1024);
            b.Property(x => x.Category).HasMaxLength(32);
            b.Property(x => x.EnvironmentJson).HasColumnType("jsonb");
            b.Property(x => x.ResourceLimitsJson).HasColumnType("jsonb");
            b.Property(x => x.RuntimeState).HasMaxLength(24);
            b.HasIndex(x => new { x.IsPublic, x.Category, x.DeletedAt });
            b.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(x => x.OwnerUserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<IslandPermission>(b =>
        {
            b.ToTable("permissions", "world"); b.HasKey(x => x.Id);
            b.Property(x => x.Permission).HasMaxLength(32); b.Property(x => x.PrincipalType).HasMaxLength(16);
            b.HasIndex(x => new { x.IslandId, x.UserId, x.Permission }).IsUnique().HasFilter("revoked_at IS NULL AND user_id IS NOT NULL");
            b.HasIndex(x => new { x.IslandId, x.GroupId, x.Permission }).IsUnique().HasFilter("revoked_at IS NULL AND group_id IS NOT NULL");
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandSnapshot>(b =>
        {
            b.ToTable("snapshots", "world"); b.HasKey(x => x.Id); b.Property(x => x.TerrainJson).HasColumnType("jsonb"); b.Property(x => x.EnvironmentJson).HasColumnType("jsonb"); b.Property(x => x.IntegrityHash).HasMaxLength(64);
            b.HasIndex(x => new { x.IslandId, x.Version }).IsUnique(); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandChange>(b =>
        {
            b.ToTable("changes", "world"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(48); b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.Property(x => x.IntegrityHash).HasMaxLength(64);
            b.HasIndex(x => new { x.IslandId, x.Version }).IsUnique(); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BuildObject>(b =>
        {
            b.ToTable("objects", "world"); b.HasKey(x => x.Id); b.Property(x => x.PrimitiveType).HasMaxLength(24); b.Property(x => x.MaterialJson).HasColumnType("jsonb"); b.Property(x => x.PhysicsJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasIndex(x => new { x.IslandId, x.OwnerUserId }); b.HasIndex(x => x.GroupId);
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BuildOperation>(b =>
        {
            b.ToTable("build_operations", "world"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(24); b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.IslandId, x.CreatedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Asset>(b => { b.ToTable("assets", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Category).HasMaxLength(32); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.LicenseJson).HasColumnType("jsonb"); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.OwnerUserId, x.DeletedAt }); b.HasIndex(x => x.CreatorUserId); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatorUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<AssetVersion>(b => { b.ToTable("asset_versions", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.ContentHash).HasMaxLength(128); b.Property(x => x.StorageKey).HasMaxLength(512); b.Property(x => x.ThumbnailKey).HasMaxLength(512); b.Property(x => x.MimeType).HasMaxLength(128); b.Property(x => x.ValidationJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.AssetId, x.Version }).IsUnique(); b.HasIndex(x => x.ContentHash); b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<InventoryFolder>(b => { b.ToTable("folders", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.HasIndex(x => new { x.OwnerUserId, x.ParentId, x.Name }).IsUnique().HasFilter("deleted_at IS NULL"); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<InventoryItem>(b => { b.ToTable("items", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.DisplayName).HasMaxLength(160); b.HasIndex(x => new { x.OwnerUserId, x.FolderId, x.DeletedAt }); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<AssetPermission>(b => { b.ToTable("asset_permissions", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.PrincipalType).HasMaxLength(16); b.Property(x => x.Flags).HasMaxLength(256); b.HasIndex(x => new { x.AssetId, x.UserId, x.RevokedAt }); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<WorldAssetInstance>(b => { b.ToTable("placed_assets", "world"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
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
