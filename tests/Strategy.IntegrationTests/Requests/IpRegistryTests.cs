using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Teams;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Modules.IpRegistry.Jobs;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Requests;

/// <summary>Captures every log event of the host (Serilog reads sinks from DI) to prove secrets never reach the logs.</summary>
public sealed class CapturingLogSink : ILogEventSink
{
    public ConcurrentQueue<string> Events { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        Events.Enqueue(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", logEvent.Properties.Select(p => p.Value.ToString())) + " " + logEvent.Exception);
    }
}

/// <summary>A requests host that also captures logs.</summary>
public sealed class IpFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public CapturingPdfRenderer Pdf { get; } = new();

    public CapturingLogSink Logs { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(Pdf);
            services.AddSingleton<ILogEventSink>(Logs);
        });
        foreach (var system in new[] { "requests", "docreg", "ip" })
        {
            await Factory.GrantSystemAccessAsync(system, AccessGrants.Everyone, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>IP Registry on the Request Framework (SPEC §7, docs/design/requests.md §4, ADR 0086–0087).</summary>
public sealed class IpRegistryTests(IpFixture fixture) : IClassFixture<IpFixture>
{
    private const string Ip = "/api/v1/ip";
    private const string EmiratesId = "784-1985-7654321-9";

    private async Task<(RequestWorld World, TestUser IpAdmin)> WorldAsync()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory, "docreg.office", "ip.office");
        var admin = await fixture.Factory.CreateUserAsync(withMfa: true, unitId: w.OrganisationId, roles: "ip.administrator");
        w.Clients[admin.Id] = await fixture.Factory.SignInAsync(admin);
        return (w, admin);
    }

    private static object Form(RequestWorld w, object[] contributors, bool declaration = true) => new
    {
        nameAr = "منهجية قياس رضا المتعاملين",
        nameEn = "Customer satisfaction measurement methodology",
        description = "A methodology to measure customer satisfaction across service channels.",
        contributors,
        declaration,
    };

    [Fact]
    public async Task Request_review_filing_registration_certificate_with_the_emirates_id_never_unmasked()
    {
        var (w, admin) = await WorldAsync();
        var draft = await w.DraftAsync(w.EmployeeA, "ip.registration", new { nameEn = "draft" });

        // Server-side validation: Emirates ID format, contribution total 100, declaration ticked.
        var bad = await w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = Form(w, [new { employee = w.EmployeeA.EmployeeId, emiratesId = "784-12", role = "author", share = 100 }]),
        });
        (await bad.Content.ReadAsStringAsync()).ShouldContain("requests.errors.emiratesId");

        var saved = await RequestWorld.OkAsync<RequestDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = Form(w,
            [
                new { employee = w.EmployeeA.EmployeeId, emiratesId = EmiratesId, role = "author", share = 60 },
                new { externalName = "Dr. Sara Ali", externalOrganisation = "University", role = "contributor", share = 30 },
            ]),
        }));
        var savedJson = JsonSerializer.Serialize(saved);
        savedJson.ShouldNotContain(EmiratesId);
        savedJson.ShouldContain("784-****-*******-9");

        var refused = await w.C(w.EmployeeA).PostAsJsonAsync($"{RequestWorld.Base}/{draft.Id}/submit", new { version = saved.Version });
        (await refused.Content.ReadAsStringAsync()).ShouldContain("ip.errors.sharesTotal");

        // Sending the mask back keeps the stored value; the shares are corrected.
        var masked = saved.Data["contributors"]![0]!["emiratesId"]!.GetValue<string>();
        saved = await RequestWorld.OkAsync<RequestDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = saved.Version,
            data = Form(w,
            [
                new { employee = w.EmployeeA.EmployeeId, emiratesId = masked, role = "author", share = 70 },
                new { externalName = "Dr. Sara Ali", externalOrganisation = "University", role = "contributor", share = 30 },
            ]),
        }));
        var submitted = await w.SubmitAsync(w.EmployeeA, saved);
        submitted.SubjectType.ShouldBe("ip.record");
        var recordId = submitted.SubjectId!.Value;

        // Office review with property / protection type.
        var approved = await w.DecideAsync(w.Office, draft.Id, "approve", new { propertyType = "methodology", protectionType = "copyright", notes = "Original" });
        approved.Status.ShouldBe("completed");
        var record = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).GetAsync($"{Ip}/records/{recordId}"));
        record.Number.ShouldStartWith("IP-Registry-");
        (record.Status, record.PropertyType, record.ProtectionType).ShouldBe(("approved_to_file", "methodology", "copyright"));
        record.Contributors.Select(c => c.SharePercent).ShouldBe([70m, 30m]);
        record.Contributors[0].EmiratesIdMasked.ShouldBe("784-****-*******-9");
        record.Can.File.ShouldBeTrue();
        record.Can.Sensitive.ShouldBeFalse();

        // Only IP administrators see the clear value (reveal, MOET form); others 403, outsiders 404.
        (await RequestWorld.StatusAsync(w.C(w.Outsider).GetAsync($"{Ip}/records/{recordId}"))).ShouldBe(HttpStatusCode.NotFound);
        (await RequestWorld.StatusAsync(w.C(w.EmployeeA).GetAsync($"{Ip}/records/{recordId}"))).ShouldBe(HttpStatusCode.OK); // contributor / requester
        (await RequestWorld.StatusAsync(w.C(w.Office).GetAsync($"{Ip}/records/{recordId}/moet-form"))).ShouldBe(HttpStatusCode.Forbidden);
        (await RequestWorld.StatusAsync(w.C(w.Office).GetAsync($"{Ip}/records/{recordId}/contributors/{record.Contributors[0].Id}/emirates-id")))
            .ShouldBe(HttpStatusCode.Forbidden);
        var reveal = await RequestWorld.OkAsync<RevealDto>(w.C(admin).GetAsync($"{Ip}/records/{recordId}/contributors/{record.Contributors[0].Id}/emirates-id"));
        reveal.EmiratesId.ShouldBe(EmiratesId);
        var form = await w.C(admin).GetAsync($"{Ip}/records/{recordId}/moet-form");
        form.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = fixture.Pdf.Html.Last();
        html.ShouldContain(EmiratesId);
        html.ShouldContain("منهجية قياس رضا المتعاملين");
        html.ShouldContain("dir=\"rtl\"");

        // Filing with MOET, then registration: expiry proposed at registration + 20 years, certificate notice to the employee contributor.
        record = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{recordId}/filing",
            new { filingDate = "2026-02-01", applicationNo = "MOET-2026-555", version = record.Version }));
        record.Status.ShouldBe("filed");
        record = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{recordId}/registration",
            new { registrationNo = "REG-77", registrationDate = "2026-03-01", version = record.Version }));
        (record.Status, record.ExpiryDate).ShouldBe(("registered", (DateOnly?)new DateOnly(2046, 3, 1)));
        record.Contributors[0].CertificateSentAt.ShouldNotBeNull();
        record.Periods.Single().RegistrationNo.ShouldBe("REG-77");

        // The clear value is nowhere else: request / IP responses, audit values, outbox payloads, logs.
        foreach (var json in new[]
                 {
                     JsonSerializer.Serialize(await w.GetAsync(w.EmployeeA, draft.Id)), JsonSerializer.Serialize(await w.GetAsync(w.Office, draft.Id)),
                     JsonSerializer.Serialize(record), await (await w.C(w.Office).GetAsync($"{RequestWorld.Base}/{draft.Id}/audit")).Content.ReadAsStringAsync(),
                     await (await w.C(w.Office).GetAsync($"{Ip}/records/{recordId}/audit")).Content.ReadAsStringAsync(),
                 })
        {
            json.ShouldNotContain(EmiratesId);
            json.ShouldNotContain("7654321");
        }

        await using (var scope = TestAccess.SystemScope(w.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<IpDbContext>();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            foreach (var sql in new[]
                     {
                         "select count(*) from audit.entry where coalesce(old_value,'') like '%7654321%' or coalesce(new_value,'') like '%7654321%' or coalesce(data,'') like '%7654321%'",
                         "select count(*) from scheduling.outbox_message where payload::text like '%7654321%'",
                         "select count(*) from requests.request where data::text like '%7654321%' or process_data::text like '%7654321%'",
                         "select count(*) from ip.contributor where coalesce(emirates_id_encrypted,'') like '%7654321%'",
                     })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(0, sql);
            }
        }

        fixture.Logs.Events.ShouldNotContain(e => e.Contains("7654321", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publishing_a_document_of_a_flagged_type_creates_an_ip_record_linked_to_it()
    {
        var (w, _) = await WorldAsync();
        await using (var scope = TestAccess.SystemScope(w.Factory))
        {
            var editor = scope.ServiceProvider.GetRequiredService<ISettingsEditor>();
            var current = await editor.GetDocumentAsync<DocRegSettings>();
            var types = current.Value.Types.Select(t => t.Code == "manual" ? t with { CreatesIp = true } : t).ToList();
            (await editor.SaveAsync(current.Value with { Types = types }, current.Version, DocRegSettings.Validate)).IsSuccess.ShouldBeTrue();
        }

        // A manual (flagged) creates an IP record; a policy (not flagged) does not.
        var (manual, _) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeA, "Service design manual", "internal", type: "manual");
        var (policy, _) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeA, "Leave policy", "internal", type: "policy");

        IpRecordDto? created = null;
        await TeamsWorld.WaitAsync(async () =>
        {
            var page = await RequestWorld.OkAsync<JsonElement>(w.C(w.Office).GetAsync($"{Ip}/records?search=Service%20design%20manual"));
            var item = page.GetProperty("items").EnumerateArray().FirstOrDefault();
            if (item.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            created = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).GetAsync($"{Ip}/records/{item.GetProperty("id").GetGuid()}"));
            return true;
        }, "IP record from the published manual");

        (created!.Source, created.Status, created.DocumentId, created.PropertyType).ShouldBe(("document", "approved_to_file", (Guid?)manual, "document"));
        created.Contributors.Single().Employee!.Id.ShouldBe(w.EmployeeA.EmployeeId!.Value);
        (await RequestWorld.OkAsync<JsonElement>(w.C(w.Office).GetAsync($"{Ip}/records?search=Leave%20policy"))).GetProperty("totalCount").GetInt32().ShouldBe(0);

        // The document shows the IP record as a linked record.
        var document = await RequestWorld.OkAsync<DocumentDto>(w.C(w.Office).GetAsync($"/api/v1/docreg/documents/{manual}"));
        document.Linked.ShouldContain(l => l.Type == "ip.record" && l.Number == created.Number);
        _ = policy;
    }

    [Fact]
    public async Task Internal_only_protection_registers_internally_and_renewal_reminders_then_expiry()
    {
        var (w, _) = await WorldAsync();
        async Task<IpRecordDto> ApprovedAsync(string protection)
        {
            var draft = await w.DraftAsync(w.EmployeeB, "ip.registration", Form(w, [new { employee = w.EmployeeB.EmployeeId, role = "developer", share = 100 }]));
            var submitted = await w.SubmitAsync(w.EmployeeB, draft);
            await w.DecideAsync(w.Office, draft.Id, "approve", new { propertyType = "software", protectionType = protection });
            return await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).GetAsync($"{Ip}/records/{submitted.SubjectId}"));
        }

        var secret = await ApprovedAsync("trade-secret");
        (secret.Can.File, secret.Can.RegisterInternally).ShouldBe((false, true));
        (await RequestWorld.StatusAsync(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{secret.Id}/filing",
            new { filingDate = "2026-02-01", applicationNo = "X", version = secret.Version }))).ShouldBe(HttpStatusCode.UnprocessableEntity);
        var internalRecord = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{secret.Id}/register-internally",
            new { version = secret.Version }));
        (internalRecord.Status, internalRecord.Internal, internalRecord.ExpiryDate).ShouldBe(("registered", true, (DateOnly?)null));
        internalRecord.RegistrationNo.ShouldStartWith("IP-INT-");

        // Copyright registered with an expiry 7 months away: reminders at 6 and 3 months, then expired.
        var record = await ApprovedAsync("copyright");
        record = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{record.Id}/filing",
            new { filingDate = "2026-01-10", applicationNo = "A-1", version = record.Version }));
        var today = DateOnly.FromDateTime(fixture.Clock.UtcNow.UtcDateTime);
        record = await RequestWorld.OkAsync<IpRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Ip}/records/{record.Id}/registration",
            new { registrationNo = "R-1", registrationDate = today.AddYears(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                expiryDate = today.AddMonths(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), version = record.Version }));

        async Task<(int, int)> RunAsync()
        {
            await using var scope = w.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
            return await scope.ServiceProvider.GetRequiredService<RenewalRunner>().RunAsync(CancellationToken.None);
        }

        async Task<int[]> RemindersAsync()
        {
            await using var scope = TestAccess.SystemScope(w.Factory);
            var db = scope.ServiceProvider.GetRequiredService<IpDbContext>();
            return [.. db.Reminders.Where(r => r.IpRecordId == record.Id && r.Kind == "reminder").Select(r => r.Months!.Value).OrderBy(m => m)];
        }

        try
        {
            await RunAsync();
            (await RemindersAsync()).ShouldBeEmpty();
            fixture.Clock.Offset = TimeSpan.FromDays(35); // < 6 months left
            await RunAsync();
            await RunAsync();
            (await RemindersAsync()).ShouldBe([6]);
            fixture.Clock.Offset = TimeSpan.FromDays(125); // < 3 months left
            await RunAsync();
            (await RemindersAsync()).ShouldBe([3, 6]);
            fixture.Clock.Offset = TimeSpan.FromDays(220); // past expiry
            await RunAsync();
            await using var scope = TestAccess.SystemScope(w.Factory);
            (await scope.ServiceProvider.GetRequiredService<IpDbContext>().Records.SingleAsync(r => r.Id == record.Id)).Status.ShouldBe("expired");
        }
        finally
        {
            fixture.Clock.Offset = TimeSpan.Zero;
        }
    }
}
