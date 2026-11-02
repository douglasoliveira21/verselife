using System.Text;
using System.Threading.RateLimiting;
using LifeVerse.Api.Data;
using LifeVerse.Api.Infrastructure;
using LifeVerse.Api.Modules.Identity;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
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
builder.Services.AddHostedService<WorldManagerHostedService>();
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
app.UseRateLimiter();
app.UseAuthentication();
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
app.MapBuildEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
