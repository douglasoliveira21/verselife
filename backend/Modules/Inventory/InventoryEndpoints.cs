using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Social;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record FolderCreateRequest(string Name, Guid? ParentId);
public sealed record FolderPatchRequest(string? Name, Guid? ParentId, bool? ToTop);
public sealed record MoveRequest(Guid[]? ItemIds, Guid[]? FolderIds, Guid? TargetFolderId);

/// <summary>What the inventory list is asked for: where, what kind, how to order and how much at a time.</summary>
public sealed record InventoryQuery(
    string? Search = null, string? Category = null, string? Types = null, Guid? FolderId = null, bool IncludeSubfolders = false, bool Unfiled = false, bool IncludeDeleted = false, bool FavoritesOnly = false,
    string? View = null, string? Sort = null, string? Dir = null, string? Rights = null, long? MinSize = null, long? MaxSize = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);

public static class InventoryEndpoints
{
    public const int MaxPageSize = 100, MaxTerms = 6, MaxTermLength = 60;

    public static IEndpointRouteBuilder MapInventoryOrganizerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var inventory = endpoints.MapGroup("/api/v1/inventory").WithTags("Inventory").RequireAuthorization().AddEndpointFilter(StaleToConflict);
        inventory.MapGet("/items", ListAsync);
        inventory.MapGet("/summary", SummaryAsync);
        inventory.MapGet("/storage", async (HttpContext context, LifeVerseDbContext db, IConfiguration configuration, CancellationToken ct) =>
            !TryUser(context, out var me) ? Results.Unauthorized() : Results.Ok(await InventoryStorage.ReportAsync(db, me, InventoryStorage.QuotaOf(configuration), ct)));
        inventory.MapGet("/folders", ListFoldersAsync);
        inventory.MapPost("/folders", CreateFolderAsync);
        inventory.MapPatch("/folders/{folderId:guid}", UpdateFolderAsync);
        inventory.MapDelete("/folders/{folderId:guid}", DeleteFolderAsync);
        inventory.MapPost("/folders/{folderId:guid}/restore", RestoreFolderAsync);
        inventory.MapDelete("/folders/{folderId:guid}/purge", PurgeFolderAsync);
        inventory.MapPost("/move", MoveAsync);
        inventory.MapGet("/trash", TrashAsync);
        inventory.MapPost("/trash/empty", EmptyTrashAsync);
        inventory.MapDelete("/items/{itemId:guid}/purge", PurgeItemAsync);
        inventory.MapPost("/items/{itemId:guid}/used", UsedAsync);
        inventory.MapPost("/items/{itemId:guid}/ack-update", AcknowledgeUpdateAsync);
        var assets = endpoints.MapGroup("/api/v1/assets").WithTags("Asset versions").RequireAuthorization().AddEndpointFilter(StaleToConflict);
        assets.MapGet("/{assetId:guid}/versions", VersionsAsync);
        assets.MapPost("/{assetId:guid}/versions/{version:int}/activate", ActivateVersionAsync);
        assets.MapPost("/{assetId:guid}/versions", PublishVersionAsync).DisableAntiforgery();
        inventory.MapGet("/lost", LostAsync);
        inventory.MapPost("/lost/recover", RecoverAsync);
        inventory.MapGet("/items/{itemId:guid}/contents", ContentsAsync);
        inventory.MapPost("/packs/unpack", UnpackAsync).DisableAntiforgery();
        return endpoints;
    }

    /// <summary>A change that raced with another one (a revision moved on, a row went away) is a "try again" for the person, never a server error.</summary>
    public static async ValueTask<object?> StaleToConflict(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || RaceFailures.IsRace(exception)) { return Results.Conflict(new { error = "stale" }); }
    }

    private static bool TryUser(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static IResult Respond(InventoryOutcome outcome, Func<object?, IResult>? ok = null) => outcome.Ok ? (ok?.Invoke(outcome.Data) ?? Results.Ok(outcome.Data)) : outcome.Error switch
    {
        "not_found" => Results.NotFound(new { error = outcome.Error }),
        "name_taken" or "folder_limit" or "item_busy" or "already_recovered" or "inventory_full" or "storage_full" or "stale" or "unchanged" or "version_limit" => Results.Conflict(new { error = outcome.Error }),
        _ => Results.BadRequest(new { error = outcome.Error })
    };

    // ---- folders ---------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> ListFoldersAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var folders = await db.InventoryFolders.AsNoTracking().Where(f => f.OwnerUserId == owner && f.DeletedAt == null).OrderBy(f => f.Name).ToListAsync(ct);
        var counts = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == owner && i.FolderId != null && i.DeletedAt == null && i.Status != "consumed" && i.Status != "purged").GroupBy(i => i.FolderId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        return Results.Ok(folders.Select(f => new { f.Id, f.OwnerUserId, f.ParentId, f.Name, f.CreatedAt, f.UpdatedAt, itemCount = counts.GetValueOrDefault(f.Id) }));
    }

    private static async Task<IResult> CreateFolderAsync(FolderCreateRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.CreateFolderAsync(db, owner, request.Name, request.ParentId, clock.GetUtcNow(), ct), data => Results.Created($"/api/v1/inventory/folders/{((InventoryFolder)data!).Id}", data));

    private static async Task<IResult> UpdateFolderAsync(Guid folderId, FolderPatchRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.UpdateFolderAsync(db, owner, folderId, request.Name, request.ParentId, request.ToTop == true, clock.GetUtcNow(), ct));

    private static async Task<IResult> DeleteFolderAsync(Guid folderId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.DeleteFolderAsync(db, owner, folderId, clock.GetUtcNow(), ct));

    private static async Task<IResult> RestoreFolderAsync(Guid folderId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.RestoreFolderAsync(db, owner, folderId, clock.GetUtcNow(), ct));

    private static async Task<IResult> PurgeFolderAsync(Guid folderId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.PurgeFolderAsync(db, owner, folderId, clock.GetUtcNow(), ct));

    private static async Task<IResult> MoveAsync(MoveRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.MoveAsync(db, owner, request.ItemIds, request.FolderIds, request.TargetFolderId, clock.GetUtcNow(), ct));

    // ---- the bin and recents ---------------------------------------------------------------------------------------------------

    /// <summary>Folders that went to the bin (the ones deleted themselves, not the ones inside them) and how long until they go for good.</summary>
    private static async Task<IResult> TrashAsync(HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var binned = await db.InventoryFolders.AsNoTracking().Where(f => f.OwnerUserId == owner && f.DeletedAt != null).ToListAsync(ct);
        var roots = binned.Where(f => f.ParentId is not { } p || !binned.Any(o => o.Id == p && o.DeletedAt == f.DeletedAt)).ToList();
        var counts = await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == owner && i.DeletedWithFolderId != null && i.Status == "active").GroupBy(i => i.DeletedWithFolderId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var now = clock.GetUtcNow();
        return Results.Ok(new { retentionDays = InventoryOrganizer.TrashDays, folders = roots.Select(f => new { f.Id, f.Name, f.ParentId, deletedAt = f.DeletedAt, items = counts.GetValueOrDefault(f.Id), purgeAt = f.DeletedAt!.Value.AddDays(InventoryOrganizer.TrashDays) }) });
    }

    private static async Task<IResult> EmptyTrashAsync(HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var (items, folders) = await InventoryOrganizer.EmptyTrashAsync(db, owner, clock.GetUtcNow(), null, ct); return Results.Ok(new { items, folders });
    }

    private static async Task<IResult> PurgeItemAsync(Guid itemId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryOrganizer.PurgeItemAsync(db, owner, itemId, clock.GetUtcNow(), ct), _ => Results.NoContent());

    private static async Task<IResult> UsedAsync(Guid itemId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        await InventoryOrganizer.TouchAsync(db, owner, itemId, clock.GetUtcNow(), ct); return Results.NoContent();
    }

    // ---- versions of an asset ---------------------------------------------------------------------------------------------------

    private static async Task<IResult> AcknowledgeUpdateAsync(Guid itemId, HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct) =>
        !TryUser(context, out var me) ? Results.Unauthorized() : Respond(await AssetVersioning.AcknowledgeAsync(db, me, itemId, clock.GetUtcNow(), ct), _ => Results.NoContent());

    private static async Task<IResult> VersionsAsync(Guid assetId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
        !TryUser(context, out var me) ? Results.Unauthorized() : Respond(await AssetVersioning.HistoryAsync(db, me, assetId, ct));

    /// <summary>The creator goes back to (or forward to) one of the versions; the people who hold the item are told when that changed what they get.</summary>
    private static async Task<IResult> ActivateVersionAsync(Guid assetId, int version, HttpContext context, TimeProvider clock, LifeVerseDbContext db, NotificationHub hub, CancellationToken ct)
    {
        if (!TryUser(context, out var me)) return Results.Unauthorized();
        var result = await AssetVersioning.ActivateAsync(db, me, assetId, version, clock.GetUtcNow(), ct);
        if (result.Ok && result.Data is VersionResult { Changed: true }) await AssetVersioning.NotifyHoldersAsync(hub, db, assetId, version, ct);
        return Respond(result);
    }

    /// <summary>The creator uploads a new version of an asset (multipart: "file" and an optional "notes").</summary>
    private static async Task<IResult> PublishVersionAsync(Guid assetId, HttpRequest request, HttpContext context, TimeProvider clock, LifeVerseDbContext db, AssetStorageService storage, NotificationHub hub, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 101L * 1024 * 1024;
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "invalid_file" });
        IFormCollection form; try { form = await request.ReadFormAsync(ct); } catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException) { return Results.BadRequest(new { error = "invalid_file" }); }
        var file = form.Files.GetFile("file"); if (file is null || file.Length == 0 || file.Length > 100 * 1024 * 1024) return Results.BadRequest(new { error = "invalid_file" });
        using var memory = new MemoryStream(); await using (var input = file.OpenReadStream()) await input.CopyToAsync(memory, ct);
        var result = await AssetVersioning.PublishAsync(db, storage, owner, assetId, file.FileName, memory.ToArray(), form["notes"].ToString(), clock.GetUtcNow(), ct, InventoryStorage.QuotaOf(context.RequestServices.GetService<IConfiguration>()));
        if (result.Ok && result.Data is VersionResult published) await AssetVersioning.NotifyHoldersAsync(hub, db, assetId, published.Version, ct);
        return Respond(result);
    }

    // ---- packs, contents and recovery ------------------------------------------------------------------------------------------

    public sealed record RecoverRequest(Guid[]? ObjectIds);

    private static async Task<IResult> LostAsync(HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Results.Ok(new { retentionDays = InventoryRecovery.RetentionDays, objects = await InventoryRecovery.ListAsync(db, owner, clock.GetUtcNow(), ct) });

    private static async Task<IResult> RecoverAsync(RecoverRequest request, HttpContext context, TimeProvider clock, LifeVerseDbContext db, LifeVerse.Api.Modules.Assets.AssetStorageService storage, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await InventoryRecovery.RecoverAsync(db, storage, owner, request.ObjectIds, clock.GetUtcNow(), ct));

    private static async Task<IResult> ContentsAsync(Guid itemId, HttpContext context, LifeVerseDbContext db, LifeVerse.Api.Modules.Assets.AssetStorageService storage, CancellationToken ct) =>
        !TryUser(context, out var owner) ? Results.Unauthorized() : Respond(await PackContents.ReadAsync(db, storage, owner, itemId, ct));

    /// <summary>Unzips a pack of files into the inventory (all of it or nothing, see <see cref="PackUnzip"/>).</summary>
    private static async Task<IResult> UnpackAsync(HttpRequest request, HttpContext context, TimeProvider clock, LifeVerseDbContext db, LifeVerse.Api.Modules.Assets.AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = PackUnzip.MaxZipBytes + 1024 * 1024;
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "invalid_zip" });
        IFormCollection form; try { form = await request.ReadFormAsync(ct); } catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException) { return Results.BadRequest(new { error = "invalid_zip" }); }
        var file = form.Files.GetFile("file"); if (file is null || file.Length == 0 || file.Length > PackUnzip.MaxZipBytes || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "invalid_zip" });
        using var memory = new MemoryStream(); await using (var input = file.OpenReadStream()) await input.CopyToAsync(memory, ct);
        var (ok, error, report) = await PackUnzip.UnpackAsync(db, storage, owner, file.FileName, memory.ToArray(), clock.GetUtcNow(), ct, InventoryStorage.QuotaOf(context.RequestServices.GetService<IConfiguration>()));
        return ok ? Results.Ok(report) : error is "inventory_full" or "folder_limit" or "storage_full" ? Results.Conflict(new { error, report }) : Results.UnprocessableEntity(new { error, report });
    }

    /// <summary>The numbers shown beside the sections of the inventory.</summary>
    private static async Task<IResult> SummaryAsync(HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var mine = db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == owner && i.Status != "consumed" && i.Status != "purged"); var since = clock.GetUtcNow().AddDays(-InventoryOrganizer.RecentDays);
        return Results.Ok(new
        {
            total = await mine.CountAsync(i => i.DeletedAt == null, ct), favorites = await mine.CountAsync(i => i.DeletedAt == null && i.IsFavorite, ct), unfiled = await mine.CountAsync(i => i.DeletedAt == null && i.FolderId == null, ct),
            recent = await mine.CountAsync(i => i.DeletedAt == null && (i.LastUsedAt != null || i.CreatedAt >= since), ct), trash = await mine.CountAsync(i => i.DeletedAt != null && i.DeletedWithFolderId == null, ct),
            trashFolders = await db.InventoryFolders.CountAsync(f => f.OwnerUserId == owner && f.DeletedAt != null, ct)
        });
    }

    // ---- the list ----------------------------------------------------------------------------------------------------------------

    private static List<string> Terms(string? search) => (search ?? string.Empty).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Length > MaxTermLength ? t[..MaxTermLength] : t).Take(MaxTerms).Select(t => t.ToLowerInvariant()).ToList();

    private static int RightsMask(string? rights)
    {
        var mask = 0;
        foreach (var name in (rights ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            mask |= name.ToLowerInvariant() switch { "copy" => LifeVerse.Shared.Items.ItemRights.Copy, "modify" => LifeVerse.Shared.Items.ItemRights.Modify, "transfer" => LifeVerse.Shared.Items.ItemRights.Transfer, "resell" => LifeVerse.Shared.Items.ItemRights.Resell, _ => 0 };
        return mask;
    }

    public static async Task<IResult> ListAsync([AsParameters] InventoryQuery q, HttpContext context, TimeProvider clock, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var page = Math.Max(1, q.Page); var pageSize = Math.Clamp(q.PageSize <= 0 ? 50 : q.PageSize, 1, MaxPageSize); var now = clock.GetUtcNow(); var view = (q.View ?? "all").ToLowerInvariant();
        var rows = db.InventoryItems.AsNoTracking().Where(x => x.OwnerUserId == owner && x.Status != "consumed" && x.Status != "purged")
            .Join(db.Assets, item => item.AssetId, asset => asset.Id, (item, asset) => new
            {
                item, asset,
                update = asset.OwnerUserId != item.OwnerUserId && item.DeletedAt == null && db.AssetVersions.Any(v => v.AssetId == asset.Id && v.CreatedAt > (item.UpdateAckAt ?? item.CreatedAt)), // a version came out after they got the item (or after they last looked) and it is not their own
                size = db.AssetVersions.Where(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion).Select(v => v.ByteSize).FirstOrDefault(),
                mime = db.AssetVersions.Where(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion).Select(v => v.MimeType).FirstOrDefault(),
                hash = db.AssetVersions.Where(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion).Select(v => v.ContentHash).FirstOrDefault()
            });

        rows = view switch
        {
            "trash" => rows.Where(x => x.item.DeletedAt != null && x.item.DeletedWithFolderId == null),
            "favorites" => rows.Where(x => x.item.DeletedAt == null && x.asset.DeletedAt == null && x.item.IsFavorite),
            "updated" => rows.Where(x => x.update),
            "returned" => rows.Where(x => x.item.DeletedAt == null && (x.item.Origin == "returned" || x.item.Origin == "recovered")),
            "recent" => rows.Where(x => x.item.DeletedAt == null && x.asset.DeletedAt == null && (x.item.LastUsedAt != null || x.item.CreatedAt >= now.AddDays(-InventoryOrganizer.RecentDays))),
            _ => q.IncludeDeleted ? rows : rows.Where(x => x.item.DeletedAt == null && x.asset.DeletedAt == null)
        };
        if (q.FavoritesOnly) rows = rows.Where(x => x.item.IsFavorite);
        if (q.Unfiled) rows = rows.Where(x => x.item.FolderId == null);
        else if (q.FolderId is { } folder)
        {
            if (q.IncludeSubfolders)
            {
                var live = await db.InventoryFolders.AsNoTracking().Where(f => f.OwnerUserId == owner && f.DeletedAt == null).Select(f => new FolderRules.Node(f.Id, f.ParentId)).ToListAsync(ct); var ids = FolderRules.Subtree(live, folder).ToList();
                rows = rows.Where(x => x.item.FolderId != null && ids.Contains(x.item.FolderId.Value));
            }
            else rows = rows.Where(x => x.item.FolderId == folder);
        }
        foreach (var term in Terms(q.Search)) rows = rows.Where(x => x.item.DisplayName.ToLower().Contains(term) || x.asset.Name.ToLower().Contains(term) || x.asset.Description.ToLower().Contains(term) || x.asset.Category.Contains(term));
        var types = (q.Types ?? q.Category ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.ToLowerInvariant()).Take(12).ToList();
        if (types.Count > 0) rows = rows.Where(x => types.Contains(x.asset.Category));
        if (RightsMask(q.Rights) is var mask and not 0) rows = rows.Where(x => (x.item.OwnerRights & mask) == mask);
        if (q.MinSize is { } min) rows = rows.Where(x => x.size >= min);
        if (q.MaxSize is { } max) rows = rows.Where(x => x.size <= max);
        if (q.From is { } from) rows = rows.Where(x => x.item.CreatedAt >= from);
        if (q.To is { } to) rows = rows.Where(x => x.item.CreatedAt <= to);

        var total = await rows.CountAsync(ct); var descending = string.Equals(q.Dir, "desc", StringComparison.OrdinalIgnoreCase); var ascending = string.Equals(q.Dir, "asc", StringComparison.OrdinalIgnoreCase);
        var sort = (q.Sort ?? (view == "recent" ? "recent" : string.Empty)).ToLowerInvariant();
        IOrderedQueryable<T> By<T>(IQueryable<T> source, System.Linq.Expressions.Expression<Func<T, object>> key, bool defaultDescending) => (ascending || (!descending && !defaultDescending)) ? source.OrderBy(key) : source.OrderByDescending(key);
        var ordered = sort switch
        {
            "name" => By(rows, x => x.item.DisplayName.ToLower(), false),
            "date" or "created" => By(rows, x => x.item.CreatedAt, true),
            "updated" => By(rows, x => x.item.UpdatedAt, true),
            "size" => By(rows, x => x.size, true),
            "type" => By(rows, x => x.asset.Category, false),
            "recent" => By(rows, x => x.item.LastUsedAt ?? x.item.CreatedAt, true),
            _ => rows.OrderByDescending(x => x.item.IsFavorite).ThenByDescending(x => x.item.CreatedAt) // the order it always had: favourites first, newest next
        };
        var items = await ordered.ThenBy(x => x.item.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
        {
            itemId = x.item.Id, assetId = x.asset.Id, name = x.item.DisplayName, description = x.asset.Description, category = x.asset.Category, version = x.asset.CurrentVersion, size = x.size, folderId = x.item.FolderId, contentHash = x.hash, mimeType = x.mime,
            creatorUserId = x.asset.CreatorUserId, ownerUserId = x.item.OwnerUserId, ownerRights = x.item.OwnerRights, groupRights = x.item.GroupRights, nextOwnerRights = x.item.NextOwnerRights, status = x.item.Status, favorite = x.item.IsFavorite, origin = x.item.Origin, hasUpdate = x.update, isCreator = x.asset.OwnerUserId == x.item.OwnerUserId,
            deleted = x.item.DeletedAt != null, deletedAt = x.item.DeletedAt, lastUsedAt = x.item.LastUsedAt, createdAt = x.item.CreatedAt, updatedAt = x.item.UpdatedAt
        }).ToListAsync(ct);
        return Results.Ok(new { page, pageSize, total, hasMore = (long)page * pageSize < total, items });
    }
}
