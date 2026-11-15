using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Account preferences: only known keys, checked type and range, one row per person, and a revision so two devices never overwrite each other unseen.</summary>
public sealed class AccountPreferencesTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _other = Guid.NewGuid();
    public void Dispose() => _db.Dispose();

    private static PreferencePatch Patch(int revision, string json) => new(revision, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json));
    private Task<PreferenceOutcome> Put(Guid user, int revision, string json) => AccountPreferences.PutAsync(_db, user, Patch(revision, json), _now, default);

    [Fact]
    public async Task A_new_account_starts_empty_at_revision_zero()
    {
        var result = await AccountPreferences.GetAsync(_db, _me, default);
        Assert.Equal(0, result.Revision); Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Saving_stores_values_and_raises_the_revision()
    {
        var first = await Put(_me, 0, """{"chat.font_size":18,"accessibility.reduce_motion":true}""");
        Assert.Equal(200, first.Status); Assert.Equal(1, first.Revision);
        var second = await Put(_me, 1, """{"chat.font_size":20}""");
        Assert.Equal(2, second.Revision);
        var read = await AccountPreferences.GetAsync(_db, _me, default);
        Assert.Equal(2, read.Values.Count); Assert.Equal(20L, Convert.ToInt64(read.Values["chat.font_size"])); Assert.Equal(true, read.Values["accessibility.reduce_motion"]);
    }

    [Fact]
    public async Task A_null_forgets_a_key()
    {
        await Put(_me, 0, """{"chat.font_size":18,"accessibility.reduce_motion":true}""");
        var result = await Put(_me, 1, """{"chat.font_size":null}""");
        Assert.Equal(200, result.Status); Assert.DoesNotContain("chat.font_size", result.Values.Keys); Assert.Contains("accessibility.reduce_motion", result.Values.Keys);
    }

    [Fact]
    public async Task A_stale_revision_is_refused_with_what_the_account_holds()
    {
        await Put(_me, 0, """{"chat.font_size":18}""");
        var stale = await Put(_me, 0, """{"chat.font_size":14}""");
        Assert.Equal(409, stale.Status); Assert.Equal(1, stale.Revision); Assert.Equal(18L, Convert.ToInt64(stale.Values["chat.font_size"]));
        Assert.Equal(1, (await AccountPreferences.GetAsync(_db, _me, default)).Revision);
    }

    [Theory]
    [InlineData("""{"graphics.quality":"ultra"}""")]          // device setting, not synced
    [InlineData("""{"auth.token":"abc"}""")]                  // never anything outside the list
    [InlineData("""{"chat.font_size":"big"}""")]               // wrong type
    [InlineData("""{"chat.font_size":9999}""")]                // out of range
    [InlineData("""{"accessibility.reduce_motion":1}""")]      // wrong type
    public async Task Unknown_or_invalid_values_are_refused_and_nothing_is_saved(string json)
    {
        var result = await Put(_me, 0, json);
        Assert.Equal(400, result.Status); Assert.NotEmpty(result.Errors!);
        Assert.Equal(0, (await AccountPreferences.GetAsync(_db, _me, default)).Revision);
    }

    [Fact]
    public async Task An_empty_patch_is_refused()
    {
        Assert.Equal(400, (await Put(_me, 0, "{}")).Status);
        Assert.Equal(400, (await AccountPreferences.PutAsync(_db, _me, new PreferencePatch(0, null), _now, default)).Status);
    }

    [Fact]
    public async Task People_never_see_each_others_preferences()
    {
        await Put(_me, 0, """{"chat.font_size":18}""");
        var other = await AccountPreferences.GetAsync(_db, _other, default);
        Assert.Empty(other.Values); Assert.Equal(0, other.Revision);
        await Put(_other, 0, """{"chat.font_size":12}""");
        Assert.Equal(18L, Convert.ToInt64((await AccountPreferences.GetAsync(_db, _me, default)).Values["chat.font_size"]));
    }

    [Fact]
    public async Task Resetting_clears_values_but_the_revision_keeps_growing()
    {
        await Put(_me, 0, """{"chat.font_size":18}""");
        var reset = await AccountPreferences.ResetAsync(_db, _me, default);
        Assert.Empty(reset.Values); Assert.Equal(2, reset.Revision);
        Assert.Equal(409, (await Put(_me, 1, """{"chat.font_size":14}""")).Status);
    }

    [Fact]
    public void Every_rule_has_unique_id_and_sane_bounds()
    {
        Assert.Equal(AccountPreferenceRules.All.Count, AccountPreferenceRules.All.Select(r => r.Id).Distinct().Count());
        Assert.All(AccountPreferenceRules.All, r => { Assert.True(r.Id.Length <= AccountPreferenceRules.MaxKeyLength + 16); if (r.Kind is PreferenceKind.Int or PreferenceKind.Float) Assert.True(r.Min < r.Max); if (r.Kind == PreferenceKind.Choice) Assert.NotEmpty(r.Choices!); });
        Assert.DoesNotContain(AccountPreferenceRules.All, r => r.Id.StartsWith("graphics.") || r.Id.StartsWith("display.") || r.Id.StartsWith("perf.") || r.Id.Contains("token") || r.Id.Contains("password"));
    }

    [Fact]
    public void Key_names_accept_real_keys_only()
    {
        Assert.True(AccountPreferenceRules.ValidKeyName("W")); Assert.False(AccountPreferenceRules.ValidKeyName("<script>"));
    }
}
