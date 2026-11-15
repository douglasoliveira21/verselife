using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Settings;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>A change to the account preferences: the revision the client last saw and the values to set (a JSON null forgets one).</summary>
public sealed record PreferencePatch(int BaseRevision, Dictionary<string, JsonElement>? Values);

/// <summary>What the server answers: the revision and the values the account holds; on a refusal, why.</summary>
public sealed record PreferenceOutcome(int Status, int Revision, IReadOnlyDictionary<string, object?> Values, DateTimeOffset? UpdatedAt, IReadOnlyList<PreferenceError>? Errors = null, string? Message = null);

/// <summary>
/// The settings that follow a person to every device: <c>GET/PUT/DELETE /api/v1/users/me/preferences</c>. Only the keys of <see cref="AccountPreferenceRules"/> are accepted, each checked for type and range;
/// a PUT carries the revision it was based on and is refused with 409 (and the current values) if another device changed the preferences meanwhile, so nobody overwrites a change they never saw.
/// </summary>
public static class AccountPreferences
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAccountPreferenceEndpoints(this RouteGroupBuilder group)
    {
        var preferences = group.MapGroup("/preferences");
        preferences.MapGet("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? ToResult(await GetAsync(db, user, ct)) : Results.Unauthorized());
        preferences.MapPut("", async (PreferencePatch patch, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => User(c) is { } user ? ToResult(await PutAsync(db, user, patch, t.GetUtcNow(), ct)) : Results.Unauthorized());
        preferences.MapDelete("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? ToResult(await ResetAsync(db, user, ct)) : Results.Unauthorized());
        return group;
    }

    private static Guid? User(HttpContext context) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    private static IResult ToResult(PreferenceOutcome outcome) => outcome.Status switch
    {
        200 => Results.Ok(new { revision = outcome.Revision, updatedAt = outcome.UpdatedAt, values = outcome.Values }),
        409 => Results.Json(new { error = outcome.Message, revision = outcome.Revision, updatedAt = outcome.UpdatedAt, values = outcome.Values }, statusCode: 409),
        _ => Results.Json(new { error = outcome.Message, errors = outcome.Errors }, statusCode: outcome.Status)
    };

    private static Dictionary<string, object?> Parse(string json)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (AccountPreferenceRules.Find(property.Name) is not { } rule) continue;          // a key that stopped being synced is ignored, never sent back
                var (value, error) = AccountPreferenceRules.CheckValue(rule, property.Value);
                if (error is null) result[property.Name] = value;
            }
        }
        catch (JsonException) { }
        return result;
    }

    public static async Task<PreferenceOutcome> GetAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var row = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user, ct);
        return row is null ? new PreferenceOutcome(200, 0, new Dictionary<string, object?>(), null) : new PreferenceOutcome(200, row.Revision, Parse(row.ValuesJson), row.UpdatedAt);
    }

    public static async Task<PreferenceOutcome> PutAsync(LifeVerseDbContext db, Guid user, PreferencePatch patch, DateTimeOffset now, CancellationToken ct)
    {
        if (patch.Values is null || patch.Values.Count == 0) return new PreferenceOutcome(400, 0, new Dictionary<string, object?>(), null, Message: "Envie ao menos uma preferência.");
        var check = AccountPreferenceRules.Check(patch.Values);
        if (!check.Ok) return new PreferenceOutcome(400, 0, new Dictionary<string, object?>(), null, check.Errors, "Algumas preferências são inválidas.");

        var row = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == user, ct);
        var currentRevision = row?.Revision ?? 0;
        if (patch.BaseRevision != currentRevision)
        {
            var current = await GetAsync(db, user, ct);
            return current with { Status = 409, Message = "As preferências da conta mudaram em outro dispositivo. Baixe as atuais antes de enviar." };
        }
        var merged = row is null ? new Dictionary<string, object?>() : Parse(row.ValuesJson);
        foreach (var (id, value) in check.Values) { if (value is null) merged.Remove(id); else merged[id] = value; }
        var json = JsonSerializer.Serialize(merged, Json);
        if (merged.Count > AccountPreferenceRules.MaxKeys || json.Length > AccountPreferenceRules.MaxJsonBytes)
            return new PreferenceOutcome(400, currentRevision, new Dictionary<string, object?>(), null, Message: "As preferências passam do limite de tamanho.");

        if (row is null) { row = new UserPreference { UserId = user, ValuesJson = json, Revision = 1, UpdatedAt = now }; db.UserPreferences.Add(row); }
        else { row.ValuesJson = json; row.Revision++; row.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        return new PreferenceOutcome(200, row.Revision, merged, row.UpdatedAt);
    }

    /// <summary>Forgets everything the account holds. The revision goes on growing so a device that still has the old one is told it is out of date.</summary>
    public static async Task<PreferenceOutcome> ResetAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var row = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == user, ct);
        if (row is null) return new PreferenceOutcome(200, 0, new Dictionary<string, object?>(), null);
        row.ValuesJson = "{}"; row.Revision++; row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new PreferenceOutcome(200, row.Revision, new Dictionary<string, object?>(), row.UpdatedAt);
    }
}
