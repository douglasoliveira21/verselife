using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace LifeVerse.Api.Infrastructure;

/// <summary>
/// Gzip for the JSON the API sends. The list of the objects of an island (and other big lists) is JSON that shrinks about ten times; without it a person on a slow connection gave up on "Sincronizando objetos".
/// Only JSON is compressed (assets and media are already compressed), and the game and every browser understand gzip on their own.
/// </summary>
public static class ResponseCompressionSetup
{
    public static IServiceCollection AddJsonCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options => { options.EnableForHttps = true; options.Providers.Add<GzipCompressionProvider>(); options.MimeTypes = ["application/json"]; });
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }
}
