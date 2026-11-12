using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Settings.Application;

namespace Strategy.IntegrationTests.Platform;

/// <summary>ADR 0151: the administrator Health page.</summary>
public class AdminHealthTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Url = "/api/v1/settings/health";

    [Fact]
    public async Task Administrators_see_every_check_with_message_keys_and_no_secrets()
    {
        var admin = await api.AdminClientAsync();
        var response = await admin.GetAsync(Url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var report = (await response.Content.ReadFromJsonAsync<AdminHealthReport>())!;

        var codes = report.Checks.Select(c => c.Code).ToList();
        codes.ShouldBe(
            ["version", "dataProtection", "certificates", "database", "migrations", "scheduler", "outbox", "email", "pdf", "ai", "ocr", "storage", "disk",
             "antivirus", "auditChain", "salesforceSync"],
            ignoreOrder: true);
        report.Checks.ShouldAllBe(c => c.MessageKey.StartsWith("health.", StringComparison.Ordinal));
        report.Checks.ShouldAllBe(c => new[] { "ok", "warning", "error", "notConfigured" }.Contains(c.Status));

        Check(report, "database").Status.ShouldBe(AdminHealthStatuses.Ok);
        Check(report, "database").Params!["version"].ShouldNotBeNullOrEmpty();
        var migrations = Check(report, "migrations");
        migrations.Status.ShouldBe(AdminHealthStatuses.Ok);
        migrations.Items!.Select(i => i.Code).ShouldContain("audit");
        migrations.Items!.Select(i => i.Code).ShouldContain("portfolio");

        // Test hosts switch schedule-fired runs off (Scheduling:RunSchedules=false): the page says so, and lists every job.
        var scheduler = Check(report, "scheduler");
        scheduler.Status.ShouldBe(AdminHealthStatuses.Warning);
        scheduler.MessageKey.ShouldBe("health.scheduler.schedulesOff");
        scheduler.Items!.Select(i => i.Code).ShouldContain("audit.verify-chain");
        scheduler.Items!.ShouldAllBe(i => i.LabelAr!.Length > 0 && i.LabelEn!.Length > 0 && i.Params!.ContainsKey("cron"));

        Check(report, "storage").Status.ShouldBe(AdminHealthStatuses.Ok);
        Check(report, "disk").Items!.Select(i => i.Code).ShouldBe(["files", "temp"]);
        Check(report, "pdf").Status.ShouldBe(AdminHealthStatuses.NotConfigured); // Gotenberg:Url is empty in test hosts
        Check(report, "antivirus").Status.ShouldBe(AdminHealthStatuses.NotConfigured);
        Check(report, "ai").Status.ShouldBe(AdminHealthStatuses.NotConfigured);
        Check(report, "certificates").Status.ShouldBe(AdminHealthStatuses.NotConfigured);
        Check(report, "dataProtection").Status.ShouldBe(AdminHealthStatuses.Ok);
        Check(report, "version").Params!["version"].ShouldNotBeNullOrEmpty();
        report.Status.ShouldBe(AdminHealthStatuses.Worst(report.Checks.Select(c => c.Status)));

        // Nothing secret leaves the server: no connection string, password or key material in any value.
        var json = await response.Content.ReadAsStringAsync();
        json.ShouldNotContain("Password=", Case.Insensitive);
        json.ShouldNotContain(api.Factory.ConnectionString, Case.Insensitive);
        json.ShouldNotContain("BEGIN", Case.Sensitive);
    }

    [Fact]
    public async Task The_audit_chain_check_reports_the_last_verification()
    {
        var admin = await api.AdminClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/audit/verification", new { mode = "full" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = (await admin.GetFromJsonAsync<AdminHealthReport>(Url))!;
        var chain = Check(report, "auditChain");
        chain.Status.ShouldBe(AdminHealthStatuses.Ok);
        chain.Params!["mode"].ShouldBe("full");
    }

    [Fact]
    public async Task Only_holders_of_the_health_permission_may_open_the_page()
    {
        (await api.Factory.CreateClient().GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var user = await api.UserClientAsync();
        await (await user.GetAsync(Url)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        var jobsViewer = await api.ClientWithOnlyAsync("settings.job.view");
        (await jobsViewer.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var viewer = await api.ClientWithOnlyAsync("settings.health.view");
        (await viewer.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unreachable_services_expiring_certificates_and_failing_checks_are_reported_as_errors()
    {
        var folder = Directory.CreateTempSubdirectory("strategy-health-");
        var soon = WriteCertificate(folder.FullName, "soon.pem", DateTimeOffset.UtcNow.AddDays(10));
        var expired = WriteCertificate(folder.FullName, "expired.pem", DateTimeOffset.UtcNow.AddDays(-1));
        var valid = WriteCertificate(folder.FullName, "valid.pem", DateTimeOffset.UtcNow.AddDays(200));
        await using var factory = await ApiFactory.CreateAsync(api.Postgres, settings: new Dictionary<string, string?>
        {
            ["Gotenberg:Url"] = "http://127.0.0.1:1",
            ["Health:CertificatePaths:0"] = valid,
            ["Health:CertificatePaths:1"] = soon,
            ["Health:CertificatePaths:2"] = expired,
            ["Health:CertificatePaths:3"] = Path.Combine(folder.FullName, "missing.pem"),
        }, configureServices: services => services.AddAdminHealthContributor<ThrowingContributor>());
        var admin = await factory.CreateSignedInClientAsync(admin: true);

        var report = (await admin.GetFromJsonAsync<AdminHealthReport>(Url))!;

        report.Status.ShouldBe(AdminHealthStatuses.Error);
        var pdf = Check(report, "pdf");
        pdf.Status.ShouldBe(AdminHealthStatuses.Error);
        pdf.MessageKey.ShouldBe("health.pdf.unreachable");
        var certificates = Check(report, "certificates");
        certificates.Status.ShouldBe(AdminHealthStatuses.Error);
        certificates.Items!.Select(i => (i.Status, i.MessageKey)).ShouldBe(
        [
            ("ok", "health.certificates.valid"), ("warning", "health.certificates.expiresSoon"), ("error", "health.certificates.expired"),
            ("error", "health.certificates.unreadable"),
        ]);
        certificates.Items![1].Params!["subject"].ShouldBe("soon.pem");
        var failing = Check(report, "tests.throwing");
        failing.Status.ShouldBe(AdminHealthStatuses.Error);
        failing.MessageKey.ShouldBe("health.common.failed");
        failing.Params!["error"].ShouldBe(nameof(InvalidOperationException));
        Check(report, "database").Status.ShouldBe(AdminHealthStatuses.Ok); // one failing contributor never hides the others
    }

    private static AdminHealthCheck Check(AdminHealthReport report, string code) => report.Checks.Single(c => c.Code == code);

    private static string WriteCertificate(string folder, string name, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notAfter.AddDays(-400), notAfter);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    private sealed class ThrowingContributor : IAdminHealthContributor
    {
        public string Code => "tests.throwing";

        public string Group => AdminHealthGroups.Platform;

        public int Order => 99;

        public Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("contributor failed (secret details must not be shown)");
    }
}
