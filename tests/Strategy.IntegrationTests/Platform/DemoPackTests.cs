using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Host.Demo;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.IntegrationTests.Platform;

/// <summary>
/// ADR 0153: the optional demo pack loads records in every system through the modules' own commands (audited, channel <c>import</c>), does
/// not duplicate on a second load, and is removed by deactivating / cancelling — demo users can no longer sign in.
/// </summary>
public sealed class DemoPackTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string DemoPassword = "Demo#Pack2026!";
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Only_set_up_administrators_may_load_or_remove_the_demo_pack()
    {
        var user = await _factory.CreateSignedInClientAsync();
        (await user.GetAsync("/api/v1/setup/demo-pack")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync("/api/v1/setup/demo-pack", new { password = DemoPassword })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.DeleteAsync("/api/v1/setup/demo-pack")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await _factory.CreateSignedInClientAsync(admin: true);
        (await admin.GetAsync("/api/v1/setup/demo-pack")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var weak = await admin.PostAsJsonAsync("/api/v1/setup/demo-pack", new { password = "short" });
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync()).ShouldContain("identity.passwordTooShort");
    }

    [Fact]
    public async Task The_demo_pack_loads_once_fills_every_system_and_is_removed_by_deactivation()
    {
        var admin = await _factory.CreateSignedInClientAsync(admin: true);
        var load = await admin.PostAsJsonAsync("/api/v1/setup/demo-pack", new { password = DemoPassword });
        var body = await load.Content.ReadAsStringAsync();
        load.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var status = JsonSerializer.Deserialize<DemoPackStatusDto>(body, JsonSerializerOptions.Web)!;
        status.Contributors.Where(c => c.Status != DemoPackService.Loaded).ShouldBeEmpty(body);
        status.Contributors.ShouldAllBe(c => c.Records > 0);

        int tracked;
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            tracked = await scope.ServiceProvider.GetRequiredService<OrgDbContext>().DemoRecords.CountAsync();
        }

        // A second load creates nothing new.
        var again = JsonSerializer.Deserialize<DemoPackStatusDto>(
            await (await admin.PostAsJsonAsync("/api/v1/setup/demo-pack", new { password = DemoPassword })).Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        again.Contributors.ShouldAllBe(c => c.Status == DemoPackService.Skipped);
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            (await scope.ServiceProvider.GetRequiredService<OrgDbContext>().DemoRecords.CountAsync()).ShouldBe(tracked);
            (await scope.ServiceProvider.GetRequiredService<OrgDbContext>().Units.CountAsync(u => u.Code.StartsWith("DEMO-"))).ShouldBe(6);
        }

        // The demo Director General signs in (two-step verification is set up at first sign-in: office roles require it).
        var director = await _factory.CreateSpaClientAsync();
        (await director.LoginAsync(PlatformDemoPack.Email("director"), DemoPassword)).ShouldNotBeNull();

        // Explicit audit event and channel "import" on the created records.
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            (await audit.Entries.CountAsync(e => e.Action == "demo.pack.load")).ShouldBe(2);
            var org = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
            var demoIds = await org.DemoRecords.Select(r => r.ResourceId.ToString()).ToListAsync();
            var audited = await audit.Entries.Where(e => e.Channel == "import" && e.RecordId != null && e.Reason == "demo pack")
                .Select(e => e.RecordId!).Distinct().ToListAsync();
            demoIds.Except(audited, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty("every demo record is in the audit trail, channel import");
        }

        var removed = JsonSerializer.Deserialize<DemoPackStatusDto>(
            await (await admin.DeleteAsync("/api/v1/setup/demo-pack")).Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        removed.Contributors.Where(c => c.Status is not (DemoPackService.Removed or DemoPackService.Skipped)).ShouldBeEmpty();
        (await (await _factory.CreateSpaClientAsync()).LoginAsync(PlatformDemoPack.Email("director"), DemoPassword)).ShouldBeNull();
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var org = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
            (await org.Units.CountAsync(u => u.Code.StartsWith("DEMO-") && u.Active)).ShouldBe(0);
            (await org.Units.CountAsync(u => u.Code.StartsWith("DEMO-"))).ShouldBe(6, "deactivated, never deleted");
            (await org.Employees.CountAsync(e => e.EmployeeNo.StartsWith("DEMO-") && e.Status != "left")).ShouldBe(0);
        }

        // Removing twice changes nothing.
        var twice = JsonSerializer.Deserialize<DemoPackStatusDto>(
            await (await admin.DeleteAsync("/api/v1/setup/demo-pack")).Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        twice.Contributors.ShouldAllBe(c => c.Status == DemoPackService.Skipped);
    }
}
