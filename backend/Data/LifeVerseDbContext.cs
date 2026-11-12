using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Data;

public sealed class LifeVerseDbContext(DbContextOptions<LifeVerseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<AvatarAppearance> AvatarAppearances => Set<AvatarAppearance>();
    public DbSet<AvatarLook> AvatarLooks => Set<AvatarLook>();
    public DbSet<AvatarModel> AvatarModels => Set<AvatarModel>();
    public DbSet<AvatarWearable> AvatarWearables => Set<AvatarWearable>();
    public DbSet<AvatarAnimationFile> AvatarAnimationFiles => Set<AvatarAnimationFile>();
    public DbSet<EquippedItem> EquippedItems => Set<EquippedItem>();
    public DbSet<Island> Islands => Set<Island>();
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<TeleportTransfer> TeleportTransfers => Set<TeleportTransfer>();
    public DbSet<IslandPermission> IslandPermissions => Set<IslandPermission>();
    public DbSet<IslandBan> IslandBans => Set<IslandBan>();
    public DbSet<IslandAuditEntry> IslandAudit => Set<IslandAuditEntry>();
    public DbSet<IslandServerCommand> IslandServerCommands => Set<IslandServerCommand>();
    public DbSet<IslandMetric> IslandMetrics => Set<IslandMetric>();
    public DbSet<IslandRestart> IslandRestarts => Set<IslandRestart>();
    public DbSet<IslandSnapshot> IslandSnapshots => Set<IslandSnapshot>();
    public DbSet<IslandChange> IslandChanges => Set<IslandChange>();
    public DbSet<IslandParcel> IslandParcels => Set<IslandParcel>();
    public DbSet<PlayerLocation> PlayerLocations => Set<PlayerLocation>();
    public DbSet<ParcelChange> ParcelChanges => Set<ParcelChange>();
    public DbSet<ParcelCell> ParcelCells => Set<ParcelCell>();
    public DbSet<ParcelRental> ParcelRentals => Set<ParcelRental>();
    public DbSet<IslandLease> IslandLeases => Set<IslandLease>();
    public DbSet<IslandTransaction> IslandTransactions => Set<IslandTransaction>();
    public DbSet<ParcelAccess> ParcelAccess => Set<ParcelAccess>();
    public DbSet<ParcelGroupAccess> ParcelGroupAccess => Set<ParcelGroupAccess>();
    public DbSet<ParcelTransaction> ParcelTransactions => Set<ParcelTransaction>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleSeat> VehicleSeats => Set<VehicleSeat>();
    public DbSet<BuildObject> BuildObjects => Set<BuildObject>();
    public DbSet<BuildOperation> BuildOperations => Set<BuildOperation>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetVersion> AssetVersions => Set<AssetVersion>();
    public DbSet<InventoryFolder> InventoryFolders => Set<InventoryFolder>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<ItemOwnerRecord> ItemOwnerRecords => Set<ItemOwnerRecord>();
    public DbSet<ItemAuditEntry> ItemAudit => Set<ItemAuditEntry>();
    public DbSet<ItemOffer> ItemOffers => Set<ItemOffer>();
    public DbSet<AssetPermission> AssetPermissions => Set<AssetPermission>();
    public DbSet<WorldAssetInstance> WorldAssetInstances => Set<WorldAssetInstance>();
    public DbSet<ObjectScript> ObjectScripts => Set<ObjectScript>();
    public DbSet<ScriptState> ScriptStates => Set<ScriptState>();
    public DbSet<ScriptExecution> ScriptExecutions => Set<ScriptExecution>();
    public DbSet<HudDefinition> HudDefinitions => Set<HudDefinition>();
    public DbSet<IslandFavorite> IslandFavorites => Set<IslandFavorite>();
    public DbSet<Landmark> Landmarks => Set<Landmark>();
    public DbSet<SearchEntry> SearchEntries => Set<SearchEntry>();
    public DbSet<SearchState> SearchStates => Set<SearchState>();
    public DbSet<SearchFavorite> SearchFavorites => Set<SearchFavorite>();
    public DbSet<SearchInterest> SearchInterests => Set<SearchInterest>();
    public DbSet<SearchReport> SearchReports => Set<SearchReport>();
    public DbSet<SearchStat> SearchStats => Set<SearchStat>();
    public DbSet<DiscoveryEvent> DiscoveryEvents => Set<DiscoveryEvent>();
    public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();
    public DbSet<PhotoSnapshot> PhotoSnapshots => Set<PhotoSnapshot>();
    public DbSet<TeleportReservation> TeleportReservations => Set<TeleportReservation>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<MarketplaceProduct> MarketplaceProducts => Set<MarketplaceProduct>();
    public DbSet<MarketplaceOrder> MarketplaceOrders => Set<MarketplaceOrder>();
    public DbSet<IslandRentalPlan> IslandRentalPlans => Set<IslandRentalPlan>();
    public DbSet<IslandRental> IslandRentals => Set<IslandRental>();
    public DbSet<WorldStore> WorldStores => Set<WorldStore>();
    public DbSet<AdminAudit> AdminAudits => Set<AdminAudit>();
    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<SocialMessage> SocialMessages => Set<SocialMessage>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<SocialGroup> SocialGroups => Set<SocialGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Marriage> Marriages => Set<Marriage>();
    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();
    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    public DbSet<GroupBan> GroupBans => Set<GroupBan>();
    public DbSet<GroupJoinRequest> GroupJoinRequests => Set<GroupJoinRequest>();
    public DbSet<GroupAuditEntry> GroupAuditEntries => Set<GroupAuditEntry>();
    public DbSet<GroupAnnouncement> GroupAnnouncements => Set<GroupAnnouncement>();
    public DbSet<GroupNotification> GroupNotifications => Set<GroupNotification>();
    public DbSet<GroupTreasury> GroupTreasuries => Set<GroupTreasury>();
    public DbSet<GroupLedgerEntry> GroupLedger => Set<GroupLedgerEntry>();
    public DbSet<SocialReport> SocialReports => Set<SocialReport>();
    public DbSet<RadioStation> RadioStations => Set<RadioStation>();

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
            b.Property(x => x.Status).HasMaxLength(24);
            b.Property(x => x.PresenceStatus).HasMaxLength(16); b.Property(x => x.PrivacyJson).HasColumnType("jsonb"); b.Property(x => x.VoiceSettingsJson).HasColumnType("jsonb"); b.Property(x => x.Bio).HasMaxLength(2000); b.Property(x => x.ProfilePhotoUrl).HasMaxLength(1024); b.HasIndex(x => x.PresenceStatus);
            b.Property(x => x.AccountType).HasMaxLength(16); b.Property(x => x.FirstLifeBio).HasMaxLength(2000); b.Property(x => x.Interests).HasMaxLength(500);
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
            b.Property(x => x.EyeColor).HasMaxLength(9);
            b.Property(x => x.BeardStyle).HasMaxLength(32);
            b.Property(x => x.BodySettingsJson).HasColumnType("jsonb");
            b.Property(x => x.FaceSettingsJson).HasColumnType("jsonb");
            b.Property(x => x.OutfitsJson).HasColumnType("jsonb");
            b.Property(x => x.AttachmentsJson).HasColumnType("jsonb");
            b.Property(x => x.AnimationOverridesJson).HasColumnType("jsonb").HasDefaultValue("{}");
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
            b.Property(x => x.ContentRating).HasMaxLength(12).HasDefaultValue("livre"); b.Property(x => x.Language).HasMaxLength(12).HasDefaultValue("pt-BR"); b.Property(x => x.ShowInSearch).HasDefaultValue(true); b.Property(x => x.ShowOnMap).HasDefaultValue(true); b.Property(x => x.AccessClosed).HasDefaultValue(false); b.Property(x => x.LookJson).HasColumnType("jsonb").HasDefaultValue("{}"); b.Property(x => x.LookPresetsJson).HasColumnType("jsonb").HasDefaultValue("[]"); b.Property(x => x.MediaJson).HasColumnType("jsonb").HasDefaultValue("{}");
            b.Property(x => x.EnvironmentJson).HasColumnType("jsonb");
            b.Property(x => x.ResourceLimitsJson).HasColumnType("jsonb");
            b.Property(x => x.RuntimeState).HasMaxLength(24);
            b.Property(x => x.SaleDescription).HasMaxLength(2000);
            b.HasIndex(x => new { x.IsPublic, x.Category, x.DeletedAt });
            b.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(x => x.IsWelcome).IsUnique().HasFilter("is_welcome = true AND deleted_at IS NULL");
            b.HasIndex(x => x.OwnerUserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlayerLocation>(b => { b.ToTable("player_locations", "world"); b.HasKey(x => x.UserId); b.HasIndex(x => x.IslandId); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<IslandRestart>(b =>
        {
            b.ToTable("island_restarts", "world"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(200); b.Property(x => x.Status).HasMaxLength(16); b.Property(x => x.Result).HasMaxLength(200); b.Property(x => x.WarnedJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.IslandId, x.Status }); b.HasIndex(x => new { x.IslandId, x.CreatedAt });
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandMetric>(b =>
        {
            b.ToTable("island_metrics", "world"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.IslandId, x.At });
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandServerCommand>(b =>
        {
            b.ToTable("island_commands", "world"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(16); b.Property(x => x.Text).HasMaxLength(400); b.HasIndex(x => new { x.IslandId, x.DeliveredAt });
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandAuditEntry>(b =>
        {
            b.ToTable("island_audit", "world"); b.HasKey(x => x.Id); b.Property(x => x.Category).HasMaxLength(16); b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.Summary).HasMaxLength(400); b.Property(x => x.Resource).HasMaxLength(200);
            b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.IslandId, x.CreatedAt }); b.HasIndex(x => new { x.IslandId, x.ActorUserId });
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandBan>(b =>
        {
            b.ToTable("island_bans", "world"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(200);
            b.HasIndex(x => new { x.IslandId, x.UserId, x.RevokedAt }); b.HasIndex(x => new { x.IslandId, x.CreatedAt });
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
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
            b.ToTable("snapshots", "world"); b.HasKey(x => x.Id); b.Property(x => x.BackupName).HasMaxLength(80); b.Property(x => x.TerrainJson).HasColumnType("jsonb"); b.Property(x => x.EnvironmentJson).HasColumnType("jsonb"); b.Property(x => x.IntegrityHash).HasMaxLength(64);
            b.HasIndex(x => new { x.IslandId, x.Version }).IsUnique(); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandChange>(b =>
        {
            b.ToTable("changes", "world"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(48); b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.Property(x => x.IntegrityHash).HasMaxLength(64);
            b.HasIndex(x => new { x.IslandId, x.Version }).IsUnique(); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IslandParcel>(b => { b.ToTable("parcels", "world"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.BoundsJson).HasColumnType("jsonb"); b.Property(x => x.PermissionsJson).HasColumnType("jsonb"); b.Property(x => x.EnvironmentJson).HasColumnType("jsonb"); b.Property(x => x.Revision).IsConcurrencyToken(); b.Property(x => x.SaleDescription).HasMaxLength(2000); b.Property(x => x.AccessMode).HasMaxLength(8).HasDefaultValue("public"); b.Property(x => x.BuildMode).HasMaxLength(8).HasDefaultValue("owner"); b.Property(x => x.TeleportMode).HasMaxLength(8).HasDefaultValue("free"); b.Property(x => x.MediaJson).HasColumnType("jsonb").HasDefaultValue("{}"); b.Property(x => x.Category).HasMaxLength(16).HasDefaultValue("residencial"); b.Property(x => x.Code).HasMaxLength(16).HasDefaultValue(""); b.Property(x => x.ImageUrl).HasMaxLength(1024).HasDefaultValue(""); b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasIndex(x => new { x.IslandId, x.Name }).IsUnique().HasFilter("deleted_at IS NULL"); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ParcelGroupAccess>(b => { b.ToTable("parcel_group_access", "world"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(8); b.HasIndex(x => new { x.ParcelId, x.GroupId, x.Kind }); b.HasIndex(x => x.GroupId); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ParcelAccess>(b => { b.ToTable("parcel_access", "world"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(8); b.Property(x => x.Reason).HasMaxLength(200); b.HasIndex(x => new { x.ParcelId, x.Kind }); b.HasIndex(x => x.UserId); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<IslandLease>(b => { b.ToTable("island_leases", "world"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(16); b.Property(x => x.EndReason).HasMaxLength(32); b.HasIndex(x => new { x.IslandId, x.Status }); b.HasIndex(x => new { x.TenantUserId, x.Status }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<IslandTransaction>(b => { b.ToTable("island_transactions", "world"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(32); b.Property(x => x.DataJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.IslandId, x.CreatedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ParcelRental>(b => { b.ToTable("parcel_rentals", "world"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(16); b.Property(x => x.EndReason).HasMaxLength(32); b.HasIndex(x => new { x.ParcelId, x.Status }); b.HasIndex(x => new { x.TenantUserId, x.Status }); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ParcelTransaction>(b => { b.ToTable("parcel_transactions", "world"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(32); b.Property(x => x.DataJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.ParcelId, x.CreatedAt }); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ParcelCell>(b => { b.ToTable("parcel_cells", "world"); b.HasKey(x => new { x.IslandId, x.Cx, x.Cz }); b.HasIndex(x => x.ParcelId); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<AvatarAnimationFile>(b => { b.ToTable("avatar_animation_files", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(60); b.Property(x => x.ContentHash).HasMaxLength(64); b.Property(x => x.StorageKey).HasMaxLength(200); b.Property(x => x.ReportJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.AvatarId, x.ContentHash }).IsUnique(); b.HasOne<Avatar>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<AvatarWearable>(b => { b.ToTable("avatar_wearables", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(60); b.Property(x => x.Slot).HasMaxLength(16); b.Property(x => x.Layer).HasMaxLength(8); b.Property(x => x.Kind).HasMaxLength(12); b.HasIndex(x => x.AssetId); b.Property(x => x.ContentHash).HasMaxLength(64); b.Property(x => x.StorageKey).HasMaxLength(200); b.Property(x => x.ReportJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.AvatarId, x.ContentHash }).IsUnique(); b.HasOne<Avatar>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<AvatarModel>(b => { b.ToTable("avatar_models", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(60); b.Property(x => x.ContentHash).HasMaxLength(64); b.Property(x => x.StorageKey).HasMaxLength(200); b.Property(x => x.ReportJson).HasColumnType("jsonb"); b.Property(x => x.BoneMapJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.AvatarId, x.ContentHash }).IsUnique(); b.HasOne<Avatar>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<AvatarLook>(b => { b.ToTable("avatar_looks", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(40); b.Property(x => x.SnapshotJson).HasColumnType("jsonb"); b.Property(x => x.ShareCode).HasMaxLength(16); b.HasIndex(x => new { x.AvatarId, x.Name }).IsUnique(); b.HasIndex(x => x.ShareCode).IsUnique(); b.HasOne<Avatar>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ParcelChange>(b => { b.ToTable("parcel_changes", "world"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(48); b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.ParcelId, x.CreatedAt }); b.HasOne<IslandParcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<Vehicle>(b => { b.ToTable("vehicles", "world"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.VehicleType).HasMaxLength(32); b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<VehicleSeat>(b => { b.ToTable("vehicle_seats", "world"); b.HasKey(x => x.Id); b.Property(x => x.SeatType).HasMaxLength(16); b.HasIndex(x => new { x.VehicleId, x.SeatType }).IsUnique(); b.HasIndex(x => x.OccupantUserId); b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<BuildObject>(b =>
        {
            b.ToTable("objects", "world"); b.HasKey(x => x.Id); b.Property(x => x.CreatorUserId).IsRequired(); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.PrimitiveType).HasMaxLength(24); b.Property(x => x.MaterialJson).HasColumnType("jsonb"); b.Property(x => x.GeometryJson).HasColumnType("jsonb"); b.Property(x => x.PhysicsJson).HasColumnType("jsonb"); b.Property(x => x.PermissionsJson).HasColumnType("jsonb"); b.Property(x => x.ContentJson).HasColumnType("jsonb"); b.Property(x => x.OwnerHistoryJson).HasColumnType("jsonb"); b.Property(x => x.ClickAction).HasMaxLength(16).HasDefaultValue("touch"); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasIndex(x => new { x.IslandId, x.OwnerUserId }); b.HasIndex(x => x.GroupId);
            b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatorUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<BuildObject>().WithMany().HasForeignKey(x => x.ParentObjectId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BuildOperation>(b =>
        {
            b.ToTable("build_operations", "world"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(24); b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.IslandId, x.CreatedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Asset>(b => { b.ToTable("assets", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Category).HasMaxLength(32); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.LicenseJson).HasColumnType("jsonb"); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.OwnerUserId, x.DeletedAt }); b.HasIndex(x => x.CreatorUserId); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatorUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<AssetVersion>(b => { b.ToTable("asset_versions", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.ContentHash).HasMaxLength(128); b.Property(x => x.StorageKey).HasMaxLength(512); b.Property(x => x.ThumbnailKey).HasMaxLength(512); b.Property(x => x.MimeType).HasMaxLength(128); b.Property(x => x.Notes).HasMaxLength(400); b.Property(x => x.ValidationJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.AssetId, x.Version }).IsUnique(); b.HasIndex(x => x.ContentHash); b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<InventoryFolder>(b => { b.ToTable("folders", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.HasIndex(x => new { x.OwnerUserId, x.ParentId, x.Name }).IsUnique().HasFilter("deleted_at IS NULL"); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<InventoryItem>(b => { b.ToTable("items", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.DisplayName).HasMaxLength(160); b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("active"); b.Property(x => x.OwnerRights).HasDefaultValue(15).HasSentinel(-1); b.Property(x => x.NextOwnerRights).HasDefaultValue(15).HasSentinel(-1); b.Property(x => x.Revision).IsConcurrencyToken(); b.HasIndex(x => x.AssetId); b.HasIndex(x => x.GroupId); b.HasIndex(x => new { x.OwnerUserId, x.FolderId, x.DeletedAt }); b.HasIndex(x => new { x.OwnerUserId, x.IsFavorite, x.CreatedAt }); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ItemOffer>(b => { b.ToTable("item_offers", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(12); b.Property(x => x.Status).HasMaxLength(12); b.Property(x => x.Message).HasMaxLength(200); b.Property(x => x.IdempotencyKey).HasMaxLength(128); b.Property(x => x.Revision).IsConcurrencyToken(); b.HasIndex(x => new { x.FromUserId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => x.ItemId).IsUnique().HasFilter("status = 'pending' AND keep_copy = false"); b.HasIndex(x => new { x.ToUserId, x.Status }); b.HasIndex(x => new { x.FromUserId, x.Status }); b.HasIndex(x => x.ExpiresAt); });
        modelBuilder.Entity<ItemOwnerRecord>(b => { b.ToTable("item_owners", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(16); b.HasIndex(x => new { x.ItemId, x.At }); b.HasIndex(x => new { x.UserId, x.At }); });
        modelBuilder.Entity<ItemAuditEntry>(b => { b.ToTable("item_audit", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(24); b.Property(x => x.Outcome).HasMaxLength(8); b.Property(x => x.Detail).HasMaxLength(1000); b.HasIndex(x => new { x.ItemId, x.At }); b.HasIndex(x => new { x.ActorUserId, x.At }); b.HasIndex(x => x.At); });
        modelBuilder.Entity<AssetPermission>(b => { b.ToTable("asset_permissions", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.PrincipalType).HasMaxLength(16); b.Property(x => x.Flags).HasMaxLength(256); b.HasIndex(x => new { x.AssetId, x.UserId, x.RevokedAt }); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<WorldAssetInstance>(b => { b.ToTable("placed_assets", "world"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ObjectScript>(b => { b.ToTable("object_scripts", "world"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Source).HasColumnType("text"); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.ObjectId, x.Version }).IsUnique(); b.HasOne<BuildObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ScriptState>(b => { b.ToTable("script_state", "world"); b.HasKey(x => x.Id); b.Property(x => x.Key).HasMaxLength(64); b.Property(x => x.ValueJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.ScriptId, x.Key }).IsUnique(); b.HasOne<ObjectScript>().WithMany().HasForeignKey(x => x.ScriptId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ScriptExecution>(b => { b.ToTable("script_executions", "world"); b.HasKey(x => x.Id); b.Property(x => x.EventName).HasMaxLength(32); b.Property(x => x.Status).HasMaxLength(24); b.Property(x => x.Error).HasMaxLength(2000); b.HasIndex(x => new { x.ScriptId, x.CreatedAt }); b.HasOne<ObjectScript>().WithMany().HasForeignKey(x => x.ScriptId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<HudDefinition>(b => { b.ToTable("hud_definitions", "inventory"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.LayoutJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.OwnerUserId, x.DeletedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SocialMessage>(b => { b.ToTable("messages", "social"); b.HasKey(x => x.Id); b.Property(x => x.Channel).HasMaxLength(16); b.Property(x => x.Content).HasMaxLength(2000); b.Property(x => x.ClientMessageId).HasMaxLength(96); b.HasIndex(x => new { x.Channel, x.CreatedAt }); b.HasIndex(x => new { x.RecipientUserId, x.CreatedAt }); b.HasIndex(x => new { x.GroupId, x.CreatedAt }); b.HasIndex(x => new { x.SenderUserId, x.ClientMessageId }).IsUnique().HasFilter("client_message_id IS NOT NULL"); b.HasOne<User>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<Friendship>(b => { b.ToTable("friendships", "social"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(16); b.HasIndex(x => new { x.UserId, x.FriendUserId }).IsUnique(); b.HasIndex(x => new { x.FriendUserId, x.Status }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.FriendUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<UserBlock>(b => { b.ToTable("blocks", "social"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.BlockedUserId }).IsUnique(); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<Marriage>(b => { b.ToTable("marriages", "social"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(16); b.Property(x => x.Message).HasMaxLength(300); b.HasIndex(x => new { x.ProposerUserId, x.Status }); b.HasIndex(x => new { x.TargetUserId, x.Status }); b.HasOne<User>().WithMany().HasForeignKey(x => x.ProposerUserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SocialGroup>(b => { b.ToTable("groups", "social"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.ImageUrl).HasMaxLength(1024); b.Property(x => x.NameKey).HasMaxLength(64); b.Property(x => x.Tag).HasMaxLength(8); b.Property(x => x.Privacy).HasMaxLength(8).HasDefaultValue("private"); b.Property(x => x.JoinPolicy).HasMaxLength(8).HasDefaultValue("invite"); b.Property(x => x.AccentColor).HasMaxLength(7).HasDefaultValue("#12F2E0"); b.Property(x => x.Emblem).HasMaxLength(16).HasDefaultValue("people"); b.Property(x => x.MemberLimit).HasDefaultValue(500); b.HasIndex(x => x.NameKey); b.HasIndex(x => x.Tag); b.HasIndex(x => new { x.OwnerUserId, x.DeletedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<GroupMember>(b => { b.ToTable("group_members", "social"); b.HasKey(x => x.Id); b.Property(x => x.Role).HasMaxLength(32); b.HasIndex(x => x.RoleId); b.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique(); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupRole>(b => { b.ToTable("group_roles", "social"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(32); b.Property(x => x.SystemKey).HasMaxLength(16); b.Property(x => x.Permissions).HasMaxLength(400); b.Property(x => x.Color).HasMaxLength(7); b.HasIndex(x => new { x.GroupId, x.Name }).IsUnique(); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupBan>(b => { b.ToTable("group_bans", "social"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(300); b.HasIndex(x => new { x.GroupId, x.UserId }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupJoinRequest>(b => { b.ToTable("group_join_requests", "social"); b.HasKey(x => x.Id); b.Property(x => x.Message).HasMaxLength(300); b.Property(x => x.Status).HasMaxLength(12); b.HasIndex(x => new { x.GroupId, x.UserId, x.Status }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupAuditEntry>(b => { b.ToTable("group_audit", "social"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.DetailJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.GroupId, x.CreatedAt }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupAnnouncement>(b => { b.ToTable("group_announcements", "social"); b.HasKey(x => x.Id); b.Property(x => x.Title).HasMaxLength(80); b.Property(x => x.Body).HasMaxLength(2000); b.HasIndex(x => new { x.GroupId, x.CreatedAt }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupNotification>(b => { b.ToTable("group_notifications", "social"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(32); b.Property(x => x.Title).HasMaxLength(120); b.Property(x => x.Body).HasMaxLength(300); b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupTreasury>(b => { b.ToTable("group_treasuries", "social"); b.HasKey(x => x.GroupId); b.Property(x => x.Balance).IsConcurrencyToken(); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupLedgerEntry>(b => { b.ToTable("group_ledger", "social"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasMaxLength(16); b.Property(x => x.Note).HasMaxLength(200); b.HasIndex(x => new { x.GroupId, x.CreatedAt }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<GroupInvite>(b => { b.ToTable("group_invites", "social"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(16); b.HasIndex(x => new { x.GroupId, x.InvitedUserId, x.Status }); b.HasOne<SocialGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SocialReport>(b => { b.ToTable("reports", "social"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Status).HasMaxLength(16); b.HasIndex(x => new { x.Status, x.CreatedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<RadioStation>(b => { b.ToTable("radio_stations", "social"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.StreamUrl).HasMaxLength(2048); b.HasIndex(x => x.IslandId); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<IslandFavorite>(b => { b.ToTable("island_favorites", "discovery"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.IslandId }).IsUnique(); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SearchEntry>(b => { b.ToTable("search_index", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(16); b.Property(x => x.Title).HasMaxLength(160); b.Property(x => x.Subtitle).HasMaxLength(200); b.Property(x => x.Description).HasMaxLength(400); b.Property(x => x.Category).HasMaxLength(48); b.Property(x => x.ThumbnailUrl).HasMaxLength(1024); b.HasIndex(x => new { x.Kind, x.RefId }).IsUnique(); b.HasIndex(x => new { x.Kind, x.Popularity }); b.HasIndex(x => new { x.Kind, x.SourceUpdatedAt }); b.HasIndex(x => x.IslandId); b.HasIndex(x => x.ExpiresAt); });
        modelBuilder.Entity<SearchFavorite>(b => { b.ToTable("search_favorites", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(16); b.HasIndex(x => new { x.UserId, x.Kind, x.RefId }).IsUnique(); b.HasIndex(x => new { x.UserId, x.CreatedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SearchReport>(b => { b.ToTable("search_reports", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(16); b.Property(x => x.Title).HasMaxLength(200); b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.Status).HasMaxLength(16); b.HasIndex(x => new { x.ReporterUserId, x.Kind, x.RefId }).IsUnique(); b.HasIndex(x => new { x.Kind, x.RefId, x.Status }); b.HasIndex(x => new { x.ReporterUserId, x.CreatedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SearchStat>(b => { b.ToTable("search_stats", "discovery"); b.HasKey(x => new { x.Day, x.Query }); b.Property(x => x.Query).HasMaxLength(100); });
        modelBuilder.Entity<SearchInterest>(b => { b.ToTable("search_interests", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Term).HasMaxLength(64); b.HasIndex(x => new { x.UserId, x.Term }).IsUnique(); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<SearchState>(b => { b.ToTable("search_state", "discovery"); b.HasKey(x => x.Kind); b.Property(x => x.Kind).HasMaxLength(16); });
        modelBuilder.Entity<Landmark>(b => { b.ToTable("landmarks", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.HasIndex(x => new { x.OwnerUserId, x.DeletedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<DiscoveryEvent>(b => { b.ToTable("events", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Description).HasMaxLength(4000); b.HasIndex(x => new { x.IslandId, x.StartsAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.OrganizerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<EventAttendee>(b => { b.ToTable("event_attendees", "discovery"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.EventId, x.UserId }).IsUnique(); b.HasOne<DiscoveryEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<PhotoSnapshot>(b => { b.ToTable("photos", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.StorageKey).HasMaxLength(512); b.Property(x => x.MetadataJson).HasColumnType("jsonb"); b.HasIndex(x => new { x.OwnerUserId, x.CreatedAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.SetNull); });
        modelBuilder.Entity<TeleportReservation>(b => { b.ToTable("teleport_reservations", "discovery"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.UserId, x.ExpiresAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<Wallet>(b => { b.ToTable("wallets", "economy"); b.HasKey(x => x.Id); b.Property(x => x.AvailableCoins).IsConcurrencyToken(); b.HasIndex(x => x.UserId).IsUnique(); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<LedgerAccount>(b => { b.ToTable("accounts", "economy"); b.HasKey(x => x.Id); b.Property(x => x.AccountType).HasMaxLength(32); b.HasIndex(x => new { x.UserId, x.AccountType }).IsUnique(); b.HasIndex(x => x.GroupId); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<LedgerTransaction>(b => { b.ToTable("transactions", "economy"); b.HasKey(x => x.Id); b.Property(x => x.IdempotencyKey).HasMaxLength(128); b.Property(x => x.Type).HasMaxLength(32); b.Property(x => x.Status).HasMaxLength(24); b.Property(x => x.MetadataJson).HasColumnType("jsonb"); b.HasIndex(x => x.IdempotencyKey).IsUnique(); b.HasIndex(x => new { x.InitiatorUserId, x.CreatedAt }); });
        modelBuilder.Entity<LedgerEntry>(b => { b.ToTable("ledger_entries", "economy"); b.HasKey(x => x.Id); b.HasIndex(x => x.TransactionId); b.HasOne<LedgerTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade); b.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<MarketplaceProduct>(b => { b.ToTable("products", "marketplace"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.Category).HasMaxLength(48); b.Property(x => x.PermissionsJson).HasColumnType("jsonb"); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.Status, x.Category, x.UpdatedAt }); b.HasIndex(x => x.SourceItemId); b.HasOne<User>().WithMany().HasForeignKey(x => x.SellerUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<MarketplaceOrder>(b => { b.ToTable("orders", "marketplace"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(24); b.Property(x => x.IdempotencyKey).HasMaxLength(128); b.HasIndex(x => x.IdempotencyKey).IsUnique(); b.HasIndex(x => new { x.BuyerUserId, x.CreatedAt }); b.HasOne<MarketplaceProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<WorldStore>(b => { b.ToTable("world_stores", "marketplace"); b.HasKey(x => x.Id); b.Property(x => x.DisplayName).HasMaxLength(160); b.HasIndex(x => new { x.IslandId, x.DeletedAt }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<MarketplaceProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<IslandRentalPlan>(b => { b.ToTable("rental_plans", "economy"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(80); b.HasIndex(x => x.IsActive); });
        modelBuilder.Entity<IslandRental>(b => { b.ToTable("rentals", "economy"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(24); b.HasIndex(x => new { x.IslandId, x.Status }); b.HasOne<Island>().WithMany().HasForeignKey(x => x.IslandId).OnDelete(DeleteBehavior.Cascade); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<IslandRentalPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<AdminAudit>(b => { b.ToTable("admin_audits", "platform"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(64); b.Property(x => x.TargetType).HasMaxLength(32); b.Property(x => x.MetadataJson).HasColumnType("jsonb"); b.HasIndex(x => x.CreatedAt); b.HasOne<User>().WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ModerationCase>(b => { b.ToTable("moderation_cases", "platform"); b.HasKey(x => x.Id); b.Property(x => x.Decision).HasMaxLength(32); b.Property(x => x.EvidenceJson).HasColumnType("jsonb"); b.Property(x => x.Notes).HasMaxLength(4000); b.HasIndex(x => x.CreatedAt); b.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<TeleportTransfer>(b =>
        {
            b.ToTable("teleport_transfers", "world"); b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasMaxLength(16).IsRequired(); b.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired(); b.Property(x => x.FailureCode).HasMaxLength(48);
            b.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.DestinationIslandId, x.Status });
            b.HasIndex(x => x.SourceIslandId);
            // a person has at most one trip going on: the database refuses a second active one even if two requests race
            b.HasIndex(x => x.UserId).IsUnique().HasFilter("status IN ('prepared', 'connecting')").HasDatabaseName("ux_teleport_transfers_one_active");
        });
        modelBuilder.Entity<ServerInstance>(b =>
        {
            b.ToTable("server_instances", "platform");
            b.HasKey(x => x.Id);
            b.Property(x => x.Hostname).HasMaxLength(255); b.Property(x => x.ReportJson).HasColumnType("jsonb").HasDefaultValue("{}");
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
