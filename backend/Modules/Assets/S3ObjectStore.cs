using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LifeVerse.Api.Modules.Assets;

/// AWS Signature Version 4 for S3-compatible servers (MinIO, S3, R2). Written by hand so the API needs no SDK.
public static class SigV4
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// Builds the Authorization header value. `headers` must hold every header that is signed, lower-case names, trimmed values.
    public static string Authorization(string method, string path, string query, IReadOnlyDictionary<string, string> headers, string payloadHash, string accessKey, string secretKey, string region, string service, DateTimeOffset now)
    {
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = amzDate[..8];
        var ordered = headers.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var canonicalHeaders = string.Concat(ordered.Select(pair => $"{pair.Key}:{pair.Value}\n"));
        var signedHeaders = string.Join(';', ordered.Select(pair => pair.Key));
        var canonicalRequest = $"{method}\n{path}\n{query}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest))}";
        var key = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date), region), service), "aws4_request");
        var signature = Convert.ToHexString(Hmac(key, stringToSign)).ToLowerInvariant();
        return $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    /// Percent-encodes each path segment (the slashes stay), as S3 expects in the canonical URI.
    public static string EncodePath(string path) => string.Join('/', path.Split('/').Select(segment => Uri.EscapeDataString(segment)));

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}

public sealed class S3ObjectStore(AssetStorageOptions options, HttpClient http, TimeProvider clock) : IObjectStore
{
    private readonly Uri _endpoint = new(options.Endpoint.TrimEnd('/'));

    public async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var request = Build(HttpMethod.Put, key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        request.Content = new ByteArrayContent(bytes);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new IOException($"Object storage refused the upload ({(int)response.StatusCode}): {await Truncated(response, ct)}");
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        using var request = Build(HttpMethod.Get, key, SigV4.Sha256Hex([]));
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.NotFound) { response.Dispose(); return null; }
        if (!response.IsSuccessStatusCode)
        {
            var detail = await Truncated(response, ct); response.Dispose();
            throw new IOException($"Object storage refused the download ({(int)response.StatusCode}): {detail}");
        }
        var content = await response.Content.ReadAsStreamAsync(ct);
        return new DisposingStream(content, response);
    }

    /// Creates the bucket when it does not exist yet (an existing bucket is fine).
    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        using var request = Build(HttpMethod.Put, null, SigV4.Sha256Hex([]));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict) throw new IOException($"Could not create the bucket ({(int)response.StatusCode}): {await Truncated(response, ct)}");
    }

    private HttpRequestMessage Build(HttpMethod method, string? key, string payloadHash)
    {
        var path = "/" + SigV4.EncodePath(options.Bucket) + (key is null ? string.Empty : "/" + SigV4.EncodePath(key));
        var host = _endpoint.IsDefaultPort ? _endpoint.Host : $"{_endpoint.Host}:{_endpoint.Port}";
        var now = clock.GetUtcNow();
        var headers = new Dictionary<string, string> { ["host"] = host, ["x-amz-content-sha256"] = payloadHash, ["x-amz-date"] = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) };
        var request = new HttpRequestMessage(method, new Uri(_endpoint, path));
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", headers["x-amz-date"]);
        request.Headers.TryAddWithoutValidation("Authorization", SigV4.Authorization(method.Method, path, string.Empty, headers, payloadHash, options.AccessKey, options.SecretKey, options.Region, "s3", now));
        return request;
    }

    private static async Task<string> Truncated(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        return text.Length > 300 ? text[..300] : text;
    }

    /// Keeps the HTTP response alive while the caller reads the body.
    private sealed class DisposingStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); owner.Dispose(); } base.Dispose(disposing); }
    }
}

public interface IObjectStore
{
    Task PutAsync(string key, byte[] bytes, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
}

public sealed class LocalObjectStore(string root) : IObjectStore
{
    public async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    /// Keys come from the database, but a key that climbs out of the root is refused anyway.
    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal)) throw new InvalidOperationException("Invalid storage key.");
        return full;
    }
}
