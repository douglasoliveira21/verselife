using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using LifeVerse.Shared.Items;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Giving items from a script: always a copy of an item the OWNER of the object holds and carries, only with the right to copy and transfer, only to a real avatar, only for scripts of that island, and with a limit.</summary>
public sealed class ScriptGiveItemTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = Key }).Build();
    private readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner, _guest, _stranger, _guestAvatar = Guid.NewGuid(), _object = Guid.NewGuid(), _script = Guid.NewGuid(), _foreignScript = Guid.NewGuid(), _asset = Guid.NewGuid(), _noCopyAsset = Guid.NewGuid();
    private readonly ServerInstance _server;
    private readonly InventoryItem _gift;

    public ScriptGiveItemTests()
    {
        User Make(string name) { var user = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }; _db.Users.Add(user); return user; }
        (_owner, _guest, _stranger) = (Make("dono").Id, Make("visita").Id, Make("outro").Id);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 }; var other = new Island { Id = Guid.NewGuid(), OwnerUserId = _stranger, Name = "Outra", Slug = "outra", IsPublic = true, Size = 400 };
        _server = new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = _now, LastHeartbeatAt = _now }; var foreign = Guid.NewGuid();
        _db.Islands.AddRange(island, other); _db.ServerInstances.Add(_server);
        var content = JsonSerializer.Serialize(new[] { new { assetId = _asset, name = "Brinde", category = "objects" }, new { assetId = _noCopyAsset, name = "Raro", category = "objects" } });
        _db.BuildObjects.AddRange(new BuildObject { Id = _object, IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Máquina", PrimitiveType = "cube", ContentJson = content }, new BuildObject { Id = foreign, IslandId = other.Id, CreatorUserId = _stranger, OwnerUserId = _stranger, Name = "Alheio", PrimitiveType = "cube", ContentJson = content });
        _db.ObjectScripts.AddRange(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "maquina", Source = "x", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = _foreignScript, ObjectId = foreign, OwnerUserId = _stranger, Name = "alheio", Source = "x", CreatedAt = _now, UpdatedAt = _now });
        _gift = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _owner, AssetId = _asset, DisplayName = "Brinde", OwnerRights = ItemRights.All, NextOwnerRights = ItemRights.Copy | ItemRights.Transfer, CreatedAt = _now, UpdatedAt = _now };
        _db.InventoryItems.AddRange(_gift, new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _owner, AssetId = _noCopyAsset, DisplayName = "Raro", OwnerRights = (int)Right.Transfer, NextOwnerRights = (int)Right.Transfer, CreatedAt = _now, UpdatedAt = _now });
        _db.Avatars.Add(new Avatar { Id = _guestAvatar, UserId = _guest, DisplayName = "Visita", BaseModel = "default", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext Island(string key = Key) { var context = new DefaultHttpContext(); context.Request.Headers["X-LifeVerse-Service-Key"] = key; return context; }
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    private static ScriptGiveItemResult Body(IResult result) => ((result as IValueHttpResult)!.Value as ScriptGiveItemResult)!;
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private Task<IResult> Give(Guid script, Guid asset, Guid? user = null, Guid? avatar = null, string key = Key) => ScriptingEndpoints.ScriptGiveItemAsync(_server.Id, new ScriptGiveItemCall(script, user ?? _guest, avatar ?? _guestAvatar, asset), Island(key), _config, _db, new FixedClock(_now), default);

    [Fact]
    public async Task TheAvatarGetsACopyWithTheRightsOfTheNextOwner_AndTheOwnerKeepsTheOriginal()
    {
        var done = await Give(_script, _asset); Assert.Equal(200, Status(done)); Assert.Equal(("completed", "Brinde"), (Body(done).Status, Body(done).Name));
        var copy = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == _guest);
        Assert.Equal((_asset, _gift.Id, (int)(Right.Copy | Right.Transfer)), (copy.AssetId, copy.CopiedFromItemId, copy.OwnerRights & (int)(Right.Copy | Right.Transfer)));
        Assert.Equal(_owner, (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == _gift.Id)).OwnerUserId);                                      // the original stays with the owner
        Assert.Single(await _db.ScriptLogs.ToListAsync());
    }

    [Fact]
    public async Task OnlyWhatTheOwnerMayCopyAndTransfer_AndTheObjectStillCarries_IsGiven()
    {
        var rare = await Give(_script, _noCopyAsset); Assert.Equal(409, Status(rare)); Assert.Equal("not_copyable", Body(rare).Error);
        var unknown = await Give(_script, Guid.NewGuid()); Assert.Equal(409, Status(unknown)); Assert.Equal("item_unavailable", Body(unknown).Error);       // not in the contents of the object
        var gone = await _db.BuildObjects.SingleAsync(o => o.Id == _object); gone.ContentJson = "[]"; await _db.SaveChangesAsync();
        Assert.Equal("item_unavailable", Body(await Give(_script, _asset)).Error);                                                                              // taken out of the object meanwhile
        Assert.Empty(await _db.InventoryItems.Where(i => i.OwnerUserId == _guest).ToListAsync());
    }

    [Fact]
    public async Task OnlyTheIslandServerGives_ToRealAvatars_FromItsOwnScripts()
    {
        Assert.Equal(401, Status(await Give(_script, _asset, key: "x")));
        Assert.Equal(404, Status(await Give(_foreignScript, _asset)));                                                                                           // another island's script
        Assert.Equal(404, Status(await Give(Guid.NewGuid(), _asset)));
        Assert.Equal(404, Status(await Give(_script, _asset, avatar: Guid.NewGuid())));                                                                          // an avatar nobody has
        Assert.Equal(404, Status(await Give(_script, _asset, user: _stranger)));                                                                                 // an avatar that is not the user's
        Assert.Empty(await _db.InventoryItems.Where(i => i.OwnerUserId == _guest || i.OwnerUserId == _stranger).ToListAsync());
    }

    [Fact]
    public async Task AScriptCannotFloodSomebodyWithCopies()
    {
        for (var i = 0; i < ScriptingEndpoints.MaxGivenPerHour; i++) Assert.Equal(200, Status(await Give(_script, _asset)));
        var more = await Give(_script, _asset); Assert.Equal(429, Status(more)); Assert.Equal("too_many", Body(more).Error);
    }
}
