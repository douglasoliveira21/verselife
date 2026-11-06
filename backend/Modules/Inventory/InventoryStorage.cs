using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record StorageCategory(string Category, long Bytes, int Assets);
public sealed record StorageReport(long UsedBytes, long QuotaBytes, long InBinBytes, long OlderVersionsBytes, int Items, int Assets, List<StorageCategory> ByCategory)
{
    public double Fraction => QuotaBytes <= 0 ? 1 : Math.Min(1.0, (double)UsedBytes / QuotaBytes);
}

/// <summary>
/// How much of the inventory's storage a person uses. What counts: every <b>distinct</b> asset they hold an item of (alive or still in the bin; an item they hold twice counts once),
/// at the size of its version in use, plus the older versions of the assets they created. Items given away, purged or turned into objects stop counting. The limit applies when
/// something new is stored (an upload, an unzip, a new version), never to what is already there, so nobody is locked out of their own things.
/// </summary>
public static class InventoryStorage
{
    public const long DefaultQuotaBytes = 100L * 1024 * 1024 * 1024;

    public static long QuotaOf(IConfiguration? configuration) => long.TryParse(configuration?["Inventory:QuotaBytes"], out var configured) && configured > 0 ? configured : DefaultQuotaBytes;

    public static async Task<StorageReport> ReportAsync(LifeVerseDbContext db, Guid user, long quota, CancellationToken ct)
    {
        var held = await (from i in db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == user && i.Status != "consumed" && i.Status != "purged")
                          join a in db.Assets.AsNoTracking() on i.AssetId equals a.Id
                          join v in db.AssetVersions.AsNoTracking() on new { Id = a.Id, Version = a.CurrentVersion } equals new { Id = v.AssetId, v.Version }
                          select new { a.Id, a.Category, v.ByteSize, Binned = i.DeletedAt != null }).ToListAsync(ct);
        var assets = held.GroupBy(h => h.Id).Select(g => new { g.Key, g.First().Category, g.First().ByteSize, InBin = g.All(x => x.Binned) }).ToList();
        var older = await (from a in db.Assets.AsNoTracking().Where(a => a.OwnerUserId == user && a.DeletedAt == null)
                           join v in db.AssetVersions.AsNoTracking() on a.Id equals v.AssetId where v.Version != a.CurrentVersion select (long?)v.ByteSize).SumAsync(ct) ?? 0;
        var used = assets.Sum(a => a.ByteSize) + older;
        return new StorageReport(used, quota, assets.Where(a => a.InBin).Sum(a => a.ByteSize), older, held.Count, assets.Count,
            [.. assets.GroupBy(a => a.Category).Select(g => new StorageCategory(g.Key, g.Sum(a => a.ByteSize), g.Count())).OrderByDescending(c => c.Bytes).ThenBy(c => c.Category)]);
    }

    /// <summary>Whether storing <paramref name="extraBytes"/> more still fits in the person's storage.</summary>
    public static async Task<bool> FitsAsync(LifeVerseDbContext db, Guid user, long extraBytes, long quota, CancellationToken ct) =>
        (await ReportAsync(db, user, quota, ct)).UsedBytes + Math.Max(0, extraBytes) <= quota;
}

/// <summary>
/// Keeps several sessions of the same person in step: after something in the inventory changed (a move, a deletion, an upload, a purchase…), the person's other open sessions are
/// told with a small live event and read the inventory again. The event says which session did it (the header <c>X-Client-Session</c>) so that session can ignore its own.
/// </summary>
public static class InventorySync
{
    public const string Header = "X-Client-Session";

    /// <summary>Whether a request, once answered with success, changes what an inventory shows.</summary>
    public static bool Changes(string method, string path)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return false;
        path = path.ToLowerInvariant();
        return path.StartsWith("/api/v1/inventory/", StringComparison.Ordinal) || path.StartsWith("/api/v1/assets/", StringComparison.Ordinal) || (path.StartsWith("/api/v1/economy/products", StringComparison.Ordinal) && path.EndsWith("/purchase", StringComparison.Ordinal));
    }

    public static IApplicationBuilder UseInventorySync(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        await next();
        if (context.Response.StatusCode is < 200 or >= 300 || !Changes(context.Request.Method, context.Request.Path.Value ?? string.Empty)) return;
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var user)) return;
        var source = context.Request.Headers[Header].FirstOrDefault(); if (source is { Length: > 64 }) source = source[..64];
        try { await context.RequestServices.GetRequiredService<NotificationHub>().NotifyAsync(user, "inventory_changed", new { source }, context.RequestAborted); } catch (Exception exception) when (exception is not OperationCanceledException) { }
    });
}
