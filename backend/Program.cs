using System.Text;
using System.Threading.RateLimiting;
using LifeVerse.Api.Data;
using LifeVerse.Api.Infrastructure;
using LifeVerse.Api.Modules.Identity;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Scripting;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Api.Modules.Economy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgres must be supplied by the deployment environment.");
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");
jwt.Validate();
var islandServiceKey = builder.Configuration["IslandServer:SharedKey"] ?? string.Empty;
if (islandServiceKey.Length < 32 || islandServiceKey.Contains("CONFIGURE", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("IslandServer:SharedKey must be supplied securely and contain at least 32 characters.");
var islandEntry = builder.Configuration.GetSection(IslandEntryOptions.SectionName).Get<IslandEntryOptions>()
    ?? throw new InvalidOperationException("IslandEntry configuration is required.");
islandEntry.Validate();

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(islandEntry);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<LifeVerseDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IslandEntryTokenService>();
builder.Services.AddScoped<WorldManager>();
builder.Services.AddSingleton(builder.Configuration.GetSection("ObjectStorage").Get<AssetStorageOptions>() ?? new AssetStorageOptions());
builder.Services.AddSingleton<IObjectStore>(services =>
{
    var storage = services.GetRequiredService<AssetStorageOptions>();
    if (!storage.UsesS3) return new LocalObjectStore(storage.LocalRoot);
    var s3 = new S3ObjectStore(storage, new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, services.GetRequiredService<TimeProvider>());
    try { s3.EnsureBucketAsync(CancellationToken.None).GetAwaiter().GetResult(); }
    catch (Exception exception) { services.GetRequiredService<ILogger<S3ObjectStore>>().LogWarning(exception, "Object storage bucket check failed; uploads will fail until it is reachable"); }
    return s3;
});
builder.Services.AddSingleton<AssetStorageService>();
builder.Services.AddSingleton<NotificationHub>();
builder.Services.AddSingleton(builder.Configuration.GetSection("ClientRelease").Get<LifeVerse.Api.Modules.Platform.ClientReleaseOptions>() ?? new LifeVerse.Api.Modules.Platform.ClientReleaseOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("LuaRuntime").Get<LuaRuntimeOptions>() ?? new LuaRuntimeOptions());
builder.Services.AddSingleton<LuaSandboxService>();
builder.Services.AddSingleton<ScriptMessagingService>();
builder.Services.AddHostedService<WorldManagerHostedService>();
builder.Services.AddHostedService<LandRentalWorker>();
builder.Services.AddSingleton<LifeVerse.Api.Modules.Discovery.SearchMetrics>();
builder.Services.AddHostedService<LifeVerse.Api.Modules.Discovery.SearchIndexWorker>();
builder.Services.AddHostedService<LifeVerse.Api.Modules.Inventory.ItemOfferWorker>();
builder.Services.AddHostedService<LifeVerse.Api.Modules.Inventory.InventoryTrashWorker>();
builder.Services.AddHostedService<LifeVerse.Api.Modules.Inventory.InventoryIntegrityWorker>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!Guid.TryParse(correlationId, out _)) correlationId = Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
});
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var sub = context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var sid = context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sid)?.Value;
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(sid, out var sessionId)) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        await using var scope = context.RequestServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>();
        var valid = await db.Sessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow && x.User.Status == "active" && x.User.DeletedAt == null, context.RequestAborted);
        if (!valid) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
    }
    await next();
});
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/api/v1/version", () => Results.Ok(new
{
    product = "LifeVerse",
    version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
    apiVersion = "v1"
})).WithName("GetVersion").WithTags("Platform");
app.MapOpenApi("/openapi/{documentName}.json");
app.MapIdentityEndpoints();
app.MapUserEndpoints();
app.MapPlatformEndpoints();
app.MapWorldEndpoints();
app.MapWelcomeEndpoints();
app.MapGlobalSearchEndpoints();
app.MapSearchModerationEndpoints();
app.MapItemRightsEndpoints();
app.MapItemOfferEndpoints();
app.MapObjectRightsEndpoints();
app.MapParcelEndpoints();
app.MapLandEndpoints();
app.MapVehicleEndpoints();
app.MapBuildEndpoints();
app.MapAssetEndpoints();
app.MapBuildPackageEndpoints();
app.MapBuildLimitEndpoints();
app.MapScriptingEndpoints();
app.MapSocialEndpoints();
LifeVerse.Api.Modules.Groups.GroupEndpoints.MapGroupEndpoints(app);
app.MapPeopleEndpoints();
LifeVerse.Api.Modules.Platform.ClientReleaseEndpoints.MapClientReleaseEndpoints(app);
app.MapNotificationEndpoints();
app.MapDiscoveryEndpoints();
app.MapEconomyEndpoints();
app.MapAdminEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>();
    await db.Database.MigrateAsync();
    await BuildCostBackfill.RunAsync(db);
    await BuildCostBackfill.RepriceAsync(db);
    await LifeVerse.Api.Modules.Land.ParcelCellBackfill.RunAsync(db);
}

await app.RunAsync();

public partial class Program;
