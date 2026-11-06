using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Files;
using Strategy.Platform.Files.Application;
using Strategy.Platform.Files.Domain;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Files;

/// <summary>
/// A real S3-compatible server and the real ClamAV daemon in containers (Phase 3 / 5 gaps, closed in Phase 18), with a host whose file store is
/// the <c>s3</c> provider. The S3 image is <c>STRATEGY_TEST_S3_IMAGE</c>: SeaweedFS by default (Apache-2.0; MinIO no longer publishes images to
/// Docker Hub), or any MinIO image (<c>…/minio/minio:&lt;tag&gt;</c>, detected by name). ClamAV ships its signature database in the image, so no
/// update server is needed; it takes ~20–60 s to load.
/// </summary>
public sealed class StorageContainers(PostgresFixture postgres) : IAsyncLifetime
{
    public const string Bucket = "strategy-files";
    public const string AccessKey = "strategytest";
    public const string SecretKey = "strategy-test-secret-key";
    public const string DefaultS3Image = "chrislusf/seaweedfs:3.97";

    private IContainer _s3 = null!;
    private IContainer _clamav = null!;
    private bool _minio;

    public ApiFactory Factory { get; private set; } = null!;

    public string S3Url => $"http://{_s3.Hostname}:{_s3.GetMappedPublicPort(_minio ? 9000 : 8333)}";

    public string ClamAvHost => _clamav.Hostname;

    public int ClamAvPort => _clamav.GetMappedPublicPort(3310);

    public AmazonS3Client S3Client() => new(new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonS3Config { ServiceURL = S3Url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    public async ValueTask InitializeAsync()
    {
        var image = Environment.GetEnvironmentVariable("STRATEGY_TEST_S3_IMAGE") is { Length: > 0 } configured ? configured : DefaultS3Image;
        _minio = image.Contains("minio", StringComparison.OrdinalIgnoreCase);
        var s3 = new ContainerBuilder(image);
        _s3 = (_minio
                ? s3.WithPortBinding(9000, true)
                    .WithEnvironment("MINIO_ROOT_USER", AccessKey)
                    .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
                    .WithCommand("server", "/data")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/live")))
                : s3.WithPortBinding(8333, true)
                    // The test volume may sit on an almost full disk: do not stop accepting writes at the default 1 % free space.
                    .WithCommand("server", "-s3", "-dir=/data", "-volume.minFreeSpace=0", "-volume.max=8", "-master.volumeSizeLimitMB=64")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8333).ForPath("/").ForStatusCodeMatching(c => (int)c < 500))))
            .Build();
        _clamav = new ContainerBuilder("clamav/clamav:1.5")
            .WithPortBinding(3310, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("clamd started"))
            .Build();
        await Task.WhenAll(_s3.StartAsync(), _clamav.StartAsync());
        await CreateBucketAsync();

        Factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?>
        {
            ["Files:Provider"] = "s3",
            ["Files:S3ServiceUrl"] = S3Url,
            ["Files:S3Bucket"] = Bucket,
            ["Files:S3AccessKey"] = AccessKey,
            ["Files:S3SecretKey"] = SecretKey,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Task.WhenAll(_s3.DisposeAsync().AsTask(), _clamav.DisposeAsync().AsTask());
    }

    /// <summary>SeaweedFS answers before its volume server is ready: retry until the bucket exists.</summary>
    private async Task CreateBucketAsync()
    {
        using var client = S3Client();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                await client.PutBucketAsync(Bucket);
                return;
            }
            catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);
            }
        }
    }
}

[Trait("Category", "Containers")]
public sealed class RealStorageAndScanTests(StorageContainers storage) : IClassFixture<StorageContainers>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    /// <summary>The EICAR anti-virus test file (harmless by definition; every scanner reports it).</summary>
    private static readonly byte[] Eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

    [Fact]
    public async Task Files_are_stored_versioned_read_and_deleted_through_the_s3_provider_of_a_real_s3_server()
    {
        var (client, itemId) = await ItemAsync();
        var file = await UploadOkAsync(client, itemId, Pdf, "report.pdf");
        var second = Encoding.ASCII.GetBytes("%PDF-1.4\n% second version\n%%EOF\n");
        using (var form = Form(second, "report-v2.pdf"))
        {
            var version = await client.PostAsync($"/api/v1/files/{file.Id}/versions", form);
            version.StatusCode.ShouldBe(HttpStatusCode.OK, await version.Content.ReadAsStringAsync());
            (await version.Content.ReadFromJsonAsync<FileDto>())!.VersionNo.ShouldBe(2);
        }

        // Both versions are objects in the bucket (nothing on the local volume), each readable through the authorised endpoint.
        using var s3 = storage.S3Client();
        var objects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = StorageContainers.Bucket });
        var contents = new List<byte[]>();
        foreach (var o in objects.S3Objects ?? [])
        {
            using var response = await s3.GetObjectAsync(StorageContainers.Bucket, o.Key);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer);
            contents.Add(buffer.ToArray());
        }

        contents.ShouldContain(c => c.SequenceEqual(Pdf), string.Join(" | ", contents.Select(c => Encoding.ASCII.GetString(c))));
        contents.ShouldContain(c => c.SequenceEqual(second));
        Directory.Exists(storage.Factory.FilesPath).ShouldBeFalse("the s3 provider never writes to the local volume");

        var versions = (await client.GetFromJsonAsync<List<FileVersionDto>>($"/api/v1/files/{file.Id}/versions"))!;
        (await (await client.GetAsync($"/api/v1/files/{file.Id}/content")).Content.ReadAsByteArrayAsync()).ShouldBe(second);
        (await (await client.GetAsync($"/api/v1/files/{file.Id}/content?versionId={versions[1].Id}")).Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);

        // Delete is a recorded status change (no hard delete, CLAUDE.md §6): the file is gone for users, the objects stay for the audit trail.
        (await client.PostAsJsonAsync($"/api/v1/files/{file.Id}/delete", new { reason = "Superseded" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/files/{file.Id}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = StorageContainers.Bucket })).S3Objects!.Count.ShouldBe(objects.S3Objects!.Count);
    }

    [Fact]
    public async Task The_real_clamav_daemon_refuses_the_eicar_test_file_and_passes_a_clean_file()
    {
        // The scanner client alone, against clamd.
        await using (var scope = storage.Factory.Services.CreateAsyncScope())
        {
            var scanner = scope.ServiceProvider.GetRequiredService<IVirusScanner>();
            (await scanner.ScanAsync(new MemoryStream(Eicar), storage.ClamAvHost, storage.ClamAvPort, CancellationToken.None)).ShouldBe(ScanOutcome.Infected);
            (await scanner.ScanAsync(new MemoryStream(Pdf), storage.ClamAvHost, storage.ClamAvPort, CancellationToken.None)).ShouldBe(ScanOutcome.Clean);
            (await scanner.ScanAsync(new MemoryStream(Pdf), storage.ClamAvHost, 1, CancellationToken.None)).ShouldBe(ScanOutcome.Unavailable);
        }

        // Settings → Files: scanning on and required. The upload path scans before anything is stored.
        await SaveFilesSettingsAsync(s => s with { ScanEnabled = true, ScanRequired = true, ClamAvHost = storage.ClamAvHost, ClamAvPort = storage.ClamAvPort });
        try
        {
            var (client, itemId) = await ItemAsync();
            var refused = await UploadAsync(client, itemId, Eicar, "eicar.txt");
            var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
            problem.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("files.errors.infected");
            (await client.GetFromJsonAsync<List<FileDto>>($"/api/v1/files/owners/sample.item/{itemId}"))!.ShouldBeEmpty();

            var clean = await UploadOkAsync(client, itemId, Pdf, "clean.pdf");
            clean.ScanStatus.ShouldBe(ScanStatuses.Clean);
            (await client.GetAsync($"/api/v1/files/{clean.Id}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);

            var admin = await storage.Factory.CreateSignedInClientAsync(admin: true);
            var audit = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?recordId={itemId}&action=file.upload");
            audit!.Items.ShouldContain(e => e.Success == false && e.Data!.Contains("infected"));
        }
        finally
        {
            await SaveFilesSettingsAsync(s => s with { ScanEnabled = false, ScanRequired = false });
        }
    }

    private async Task SaveFilesSettingsAsync(Func<FilesSettings, FilesSettings> change)
    {
        await using var scope = TestAccess.SystemScope(storage.Factory);
        var editor = scope.ServiceProvider.GetRequiredService<ISettingsEditor>();
        var current = await editor.GetDocumentAsync<FilesSettings>();
        (await editor.SaveAsync(change(current.Value), current.Version, FilesSettings.Validate)).IsSuccess.ShouldBeTrue();
    }

    private async Task<(HttpClient Client, Guid ItemId)> ItemAsync()
    {
        var client = await storage.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var created = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "With documents in S3" });
        return (client, (await created.Content.ReadFromJsonAsync<SampleItemDto>())!.Id);
    }

    private static async Task<FileDto> UploadOkAsync(HttpClient client, Guid itemId, byte[] content, string name)
    {
        var response = await UploadAsync(client, itemId, content, name);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FileDto>())!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid itemId, byte[] content, string name)
    {
        using var form = Form(content, name);
        return await client.PostAsync($"/api/v1/files/owners/sample.item/{itemId}", form);
    }

    private static MultipartFormDataContent Form(byte[] content, string name)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", name } };
    }
}
