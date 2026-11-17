using System.Net;
using System.Net.Http.Headers;
using LifeVerse.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The big JSON lists are sent gzipped to who asks for it, and other kinds of answer are left alone.</summary>
public sealed class ResponseCompressionTests
{
    private static async Task<(WebApplication App, string Url)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddJsonCompression();
        var app = builder.Build(); app.UseResponseCompression();
        var big = "[" + string.Join(",", Enumerable.Range(0, 3000).Select(i => $"{{\"id\":{i},\"name\":\"objeto {i}\",\"materialJson\":\"{{}}\"}}")) + "]";
        app.MapGet("/json", () => Results.Content(big, "application/json")); app.MapGet("/text", () => Results.Content(big, "text/plain"));
        await app.StartAsync(); return (app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    [Fact]
    public async Task JsonIsGzippedForWhoAsksAndMuchSmaller()
    {
        var (app, url) = await StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });
            var plain = await client.GetByteArrayAsync(url + "/json");
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            using var response = await client.GetAsync(url + "/json"); var zipped = await response.Content.ReadAsByteArrayAsync();
            Assert.Contains("gzip", response.Content.Headers.ContentEncoding); Assert.True(zipped.Length * 5 < plain.Length, $"{zipped.Length} against {plain.Length}");
            using var decoder = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip }); Assert.Equal(plain.Length, (await decoder.GetByteArrayAsync(url + "/json")).Length);       // what the game does: the same bytes after gunzip
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task OnlyJsonIsCompressed()
    {
        var (app, url) = await StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None }); client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            using var response = await client.GetAsync(url + "/text"); Assert.Empty(response.Content.Headers.ContentEncoding);
        }
        finally { await app.StopAsync(); }
    }
}
