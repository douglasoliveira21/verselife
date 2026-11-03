using System.Net;
using LifeVerse.Api.Modules.Assets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ObjectStorageTests
{
    [Fact]
    public void SigV4_MatchesTheDocumentedAwsGetObjectExample()
    {
        // https://docs.aws.amazon.com/AmazonS3/latest/API/sig-v4-header-based-auth.html (GET Object with a Range header)
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com", ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ["x-amz-date"] = "20130524T000000Z"
        };
        var authorization = SigV4.Authorization("GET", "/test.txt", "", headers, headers["x-amz-content-sha256"], "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1", "s3", new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", authorization);
    }

    [Fact]
    public void EmptyPayloadHashIsTheKnownConstant() =>
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", SigV4.Sha256Hex([]));

    private sealed class FakeS3 : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];
        public HttpStatusCode Force { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Force != default) return new HttpResponseMessage(Force) { Content = new StringContent("<Error>nope</Error>") };
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Count(c => c == '/') == 1) return new HttpResponseMessage(HttpStatusCode.OK);
            if (request.Method == HttpMethod.Put) { Objects[path] = await request.Content!.ReadAsByteArrayAsync(ct); return new HttpResponseMessage(HttpStatusCode.OK); }
            return Objects.TryGetValue(path, out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static (S3ObjectStore Store, FakeS3 Server) Create()
    {
        var server = new FakeS3();
        var options = new AssetStorageOptions { Endpoint = "http://minio:9000/", AccessKey = "key", SecretKey = "secret", Bucket = "bucket" };
        return (new S3ObjectStore(options, new HttpClient(server), TimeProvider.System), server);
    }

    [Fact]
    public async Task PutThenReadRoundTripsThroughSignedRequests()
    {
        var (store, server) = Create();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await store.PutAsync("owner/abc", bytes, default);
        var put = server.Requests.Single();
        Assert.Equal("minio:9000", put.RequestUri!.Authority);
        Assert.Equal("/bucket/owner/abc", put.RequestUri.AbsolutePath);
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=key/", put.Headers.GetValues("Authorization").Single());
        Assert.Equal(SigV4.Sha256Hex(bytes), put.Headers.GetValues("x-amz-content-sha256").Single());
        await using var stream = await store.OpenReadAsync("owner/abc", default);
        using var copy = new MemoryStream(); await stream!.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task AMissingObjectIsNullAndAServerErrorIsAnException()
    {
        var (store, server) = Create();
        Assert.Null(await store.OpenReadAsync("owner/none", default));
        server.Force = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<IOException>(() => store.PutAsync("owner/x", [1], default));
        await Assert.ThrowsAsync<IOException>(() => store.OpenReadAsync("owner/x", default));
    }

    [Fact]
    public async Task AnExistingBucketIsNotAnError()
    {
        var (store, server) = Create();
        await store.EnsureBucketAsync(default);
        server.Force = HttpStatusCode.Conflict;
        await store.EnsureBucketAsync(default);
        server.Force = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<IOException>(() => store.EnsureBucketAsync(default));
    }

    [Fact]
    public void S3IsUsedOnlyWhenEndpointAndKeysAreAllConfigured()
    {
        Assert.False(new AssetStorageOptions().UsesS3);
        Assert.False(new AssetStorageOptions { Endpoint = "http://minio:9000" }.UsesS3);
        Assert.True(new AssetStorageOptions { Endpoint = "http://minio:9000", AccessKey = "a", SecretKey = "b" }.UsesS3);
    }

    [Fact]
    public async Task LocalStoreRoundTripsAndRefusesKeysOutsideItsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lv-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AssetStorageService(new LocalObjectStore(root), NullLogger<AssetStorageService>.Instance);
            var owner = Guid.NewGuid();
            var key = await service.PutAsync(owner, "hash1", [9, 8, 7], default);
            await using (var stream = await service.OpenReadAsync(key, default)) { using var copy = new MemoryStream(); await stream!.CopyToAsync(copy); Assert.Equal(new byte[] { 9, 8, 7 }, copy.ToArray()); }
            Assert.Null(await service.OpenReadAsync($"{owner:N}/missing", default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenReadAsync("../../etc/passwd", default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
