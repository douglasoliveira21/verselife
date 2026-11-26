using LifeVerse.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


public static class LiveHost { public static async Task Main(string[] args) {
// usage: ApiLiveHost [port]   (default 5099)   Ctrl+C stops it
var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5099;
var database = "live-" + Guid.NewGuid().ToString("N");
using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
{
    var root = AppContext.BaseDirectory; while (root != null && !File.Exists(Path.Combine(root, "backend", "LifeVerse.Api.csproj"))) root = Path.GetDirectoryName(root);
    if (root == null) throw new DirectoryNotFoundException("Run this from inside the LifeVerse repository (backend/LifeVerse.Api.csproj not found above " + AppContext.BaseDirectory + ").");
    builder.UseContentRoot(Path.Combine(root, "backend"));
    builder.UseUrls($"http://127.0.0.1:{port}");
    builder.UseEnvironment("Testing");   // the API skips its start-up migration and backfills in this environment
    builder.UseSetting("ConnectionStrings:Postgres", "Host=unused");
    builder.UseSetting("Jwt:SigningKey", "live-host-jwt-signing-key-0123456789-abcdefghijklmnop");
    builder.UseSetting("IslandServer:SharedKey", "live-host-island-service-key-0123456789-abcdef");
    builder.UseSetting("IslandEntry:SigningKey", "live-host-island-entry-key-0123456789-abcdefgh");
    // the test payment provider (no money moves; refused in production): the live check buys coins with it, signing the webhook with this made-up secret (the same one is in LiveApiCheck.cs)
    builder.UseSetting("Economy:Payments:Provider", "test");
    builder.UseSetting("Economy:Payments:WebhookSecret", "live-host-test-webhook-secret-0123456789");
    builder.UseSetting("ObjectStorage:LocalRoot", Path.Combine(Path.GetTempPath(), "lifeverse-live-assets-" + database));
    builder.ConfigureServices(services =>
    {
        services.RemoveAll<DbContextOptions<LifeVerseDbContext>>(); services.RemoveAll<DbContextOptions>(); foreach (var descriptor in services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(LifeVerseDbContext)) && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")).ToList()) services.Remove(descriptor); services.RemoveAll<LifeVerseDbContext>();
        services.AddDbContext<LifeVerseDbContext>(options => options.UseInMemoryDatabase(database).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
    });
});
factory.UseKestrel(port);
factory.StartServer();
Console.WriteLine($"event=api_live_host url=http://127.0.0.1:{port}");
await Task.Delay(Timeout.Infinite);
}}







