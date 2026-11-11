using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Files;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

public sealed record ScheduleRecipient(string Kind, string Ref);

public sealed record ScheduleInput(
    string Name, string TargetKind, Guid TargetId, string Frequency, string Time, int Day, string PeriodRule, string Format, IReadOnlyList<ScheduleRecipient> Recipients,
    bool Active, DossierScheduleOptions? Dossier = null);

public sealed record ScheduleDto(
    Guid Id, string Name, string TargetKind, Guid TargetId, string Frequency, string Time, int Day, string PeriodRule, string Format,
    IReadOnlyList<ScheduleRecipient> Recipients, bool Active, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, Guid OwnerUserId, uint Version,
    DossierScheduleOptions? Dossier = null);

/// <summary>
/// Dossier schedules (ADR 0186): the records (filters on the template's record type), the file (<c>pdf</c> merged, <c>zip-pdf</c>, <c>zip-docx</c>),
/// the language (null = each recipient's) and optionally <see cref="RecipientField"/> — an employee field of the record (e.g. <c>a.owner</c>):
/// each record's person receives the dossiers of their records.
/// </summary>
public sealed record DossierScheduleOptions(List<ReportFilter> Filters, string? Language, string? RecipientField);

public sealed record ScheduleDeliveryDto(Guid Id, Guid UserId, string PeriodKey, string Status, string? Format, int Rows, string? Detail, Guid? FileId, DateTimeOffset At);

public static class PeriodRules
{
    public static readonly IReadOnlyList<string> All =
        ["previous-month", "current-month", "previous-quarter", "current-quarter", "previous-year", "current-year"];

    public static ReportPeriod Resolve(string rule, DateOnly today, int fiscalStartMonth)
    {
        var parts = rule.Split('-', 2);
        var kind = parts[1] switch
        {
            "quarter" => PeriodKinds.Quarter,
            "year" => PeriodKinds.Year,
            _ => PeriodKinds.Month,
        };
        var current = ReportPeriod.Current(kind, today, fiscalStartMonth);
        return parts[0] == "previous" ? current.Previous() : current;
    }
}

/// <summary>Next run of a schedule in the installation's time zone (pure; unit-tested).</summary>
public static class ScheduleClock
{
    public static DateTimeOffset Next(string frequency, TimeOnly time, int day, DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        for (var i = 0; i < 400; i++)
        {
            var candidateDate = date.AddDays(i);
            var matches = frequency switch
            {
                ScheduleFrequencies.Daily => true,
                ScheduleFrequencies.Weekly => (int)candidateDate.DayOfWeek == Math.Clamp(day, 0, 6),
                _ => candidateDate.Day == Math.Clamp(day, 1, 28),
            };
            if (!matches)
            {
                continue;
            }

            var localDateTime = candidateDate.ToDateTime(time, DateTimeKind.Unspecified);
            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDateTime, zone), TimeSpan.Zero);
            if (utc > after)
            {
                return utc;
            }
        }

        return after.AddDays(1);
    }

    public static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>Schedules (ADR 0145): owners schedule what they may open; administrators any.</summary>
public sealed class ScheduleService(
    ReportingDbContext db, IAccessService access, DashboardService dashboards, ReportService reports, ISettingsStore settings, ReportingClock clock,
    Dossiers.DossierTemplateService dossiers, ReportTypes.ReportTypeEngine engine)
{
    private static ScheduleDto ToDto(Schedule s) => new(
        s.Id, s.Name, s.TargetKind, s.TargetId, s.Frequency, s.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture), s.Day, s.PeriodRule, s.Format,
        JsonSerializer.Deserialize<List<ScheduleRecipient>>(s.RecipientsJson, DashboardService.Json) ?? [], s.Active, s.NextRunAt, s.LastRunAt, s.OwnerUserId, s.Version,
        s.OptionsJson is null ? null : JsonSerializer.Deserialize<DossierScheduleOptions>(s.OptionsJson, DashboardService.Json));

    private async Task<bool> AdministerAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await access.CanAsync(user, ReportingPermissions.ScheduleAdminister, cancellationToken: ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ScheduleDto>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var admin = await AdministerAsync(user, ct).ConfigureAwait(false);
        var userId = user.UserId();
        return (await db.Schedules.AsNoTracking().Where(s => admin || s.OwnerUserId == userId).OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false))
            .Select(ToDto).ToList();
    }

    public async Task<Result<IReadOnlyList<ScheduleDeliveryDto>>> DeliveriesAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var schedule = await db.Schedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (schedule is null || (schedule.OwnerUserId != user.UserId() && !await AdministerAsync(user, ct).ConfigureAwait(false)))
        {
            return Error.NotFound();
        }

        return await db.Deliveries.AsNoTracking().Where(d => d.ScheduleId == id).OrderByDescending(d => d.CreatedAt).Take(200)
            .Select(d => new ScheduleDeliveryDto(d.Id, d.UserId, d.PeriodKey, d.Status, d.Format, d.Rows, d.Detail, d.FileId, d.CreatedAt)).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<ScheduleDto>> SaveAsync(Guid? id, ScheduleInput input, uint? version, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        if (input is null)
        {
            return Error.Validation([new FieldError("schedule", "validation.required", "validation.required")]);
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200)
        {
            errors.Add(new FieldError("name", "reporting.errors.name", "reporting.errors.name"));
        }

        if (!ScheduleFrequencies.All.Contains(input.Frequency, StringComparer.Ordinal))
        {
            errors.Add(new FieldError("frequency", "reporting.errors.frequency", "reporting.errors.frequency"));
        }

        if (!TimeOnly.TryParseExact(input.Time ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            errors.Add(new FieldError("time", "reporting.errors.time", "reporting.errors.time"));
        }

        if ((input.Frequency == ScheduleFrequencies.Weekly && input.Day is < 0 or > 6) || (input.Frequency == ScheduleFrequencies.Monthly && input.Day is < 1 or > 28))
        {
            errors.Add(new FieldError("day", "reporting.errors.day", "reporting.errors.day"));
        }

        if (!PeriodRules.All.Contains(input.PeriodRule, StringComparer.Ordinal))
        {
            errors.Add(new FieldError("periodRule", "reporting.errors.periodRule", "reporting.errors.periodRule"));
        }

        var dossier = input.TargetKind == "dossier";
        if (dossier
                ? input.Format is not (Dossiers.DossierFormats.Pdf or Dossiers.DossierFormats.ZipPdf or Dossiers.DossierFormats.ZipWord) || input.Dossier is null
                : input.Format is not ("pdf" or "xlsx" or "csv" or "link"))
        {
            errors.Add(new FieldError("format", "reporting.errors.format", "reporting.errors.format"));
        }

        var byField = dossier && input.Dossier?.RecipientField is not null;
        if (input.Recipients is null || (input.Recipients.Count == 0 && !byField) || input.Recipients.Count > 200
            || input.Recipients.Any(r => !SharePrincipalKinds.All.Contains(r.Kind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(r.Ref) || r.Ref.Length > 120))
        {
            errors.Add(new FieldError("recipients", "reporting.errors.recipients", "reporting.errors.recipients"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        // The target must be something the owner may open.
        var visible = input.TargetKind switch
        {
            "dashboard" => (await dashboards.GetAsync(input.TargetId.ToString("D"), user, ct).ConfigureAwait(false)).IsSuccess,
            "report" => (await reports.GetAsync(input.TargetId, user, ct).ConfigureAwait(false)).IsSuccess,
            "dossier" => true,
            _ => false,
        };
        if (!visible)
        {
            return Error.NotFound();
        }

        if (dossier)
        {
            // The template is runnable by the owner; the filters and the recipient field are fields of its record type they may see.
            var template = (await dossiers.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == input.TargetId && t.Active);
            if (template is null || !await access.CanAsync(user, ReportingPermissions.DossierRun, cancellationToken: ct).ConfigureAwait(false))
            {
                return Error.NotFound();
            }

            var type = await engine.ResolveAsync(ReportTypes.ReportTypeRules.ImplicitPrefix + template.Dataset, user, ct).ConfigureAwait(false);
            if (type is null)
            {
                return Error.NotFound();
            }

            var check = await engine.PrepareAsync(new ReportDefinition
            {
                ReportType = ReportTypes.ReportTypeRules.ImplicitPrefix + template.Dataset,
                Columns = [input.Dossier!.RecipientField ?? template.Definition.NumberField ?? type.Nodes[0].Offered.Keys.Select(k => "a." + k).First()],
                Filters = input.Dossier.Filters ?? [],
            }, user, ct).ConfigureAwait(false);
            if (check.IsFailure)
            {
                return check.Error;
            }

            if ((input.Dossier.Language is { } l && !Dossiers.DossierLanguages.All.Contains(l, StringComparer.Ordinal))
                || (input.Dossier.RecipientField is { } field && type.Offered(field)?.Type != DatasetFieldType.Employee))
            {
                return Error.Validation([new FieldError("dossier", "reporting.errors.dossierSchedule", "reporting.errors.dossierSchedule")]);
            }
        }

        Schedule schedule;
        if (id is { } existingId)
        {
            schedule = (await db.Schedules.FirstOrDefaultAsync(s => s.Id == existingId, ct).ConfigureAwait(false))!;
            if (schedule is null || (schedule.OwnerUserId != userId && !await AdministerAsync(user, ct).ConfigureAwait(false)))
            {
                return Error.NotFound();
            }

            if (version is { } v && schedule.Version != v)
            {
                return Error.Conflict();
            }
        }
        else
        {
            schedule = new Schedule(userId, input.TargetKind, input.TargetId, input.Name.Trim());
            db.Schedules.Add(schedule);
        }

        schedule.Configure(input.Name.Trim(), input.Frequency, time, input.Day, input.PeriodRule, input.Format,
            JsonSerializer.Serialize(input.Recipients, DashboardService.Json), input.Active);
        schedule.SetOptions(dossier ? JsonSerializer.Serialize(input.Dossier, DashboardService.Json) : null);
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        schedule.Planned(input.Active ? ScheduleClock.Next(input.Frequency, time, input.Day, clock.UtcNow, ScheduleClock.Zone(regional.TimeZone)) : null);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(schedule);
    }
}

/// <summary>
/// Sends due schedules (job <c>reporting.deliver</c>, ADR 0145): for each recipient the content is built in a scope whose principal is that
/// recipient (their roles, scope and system access now), in their language; nothing visible → no e-mail. Idempotent per (schedule, recipient, period).
/// </summary>
public sealed partial class DeliveryRunner(
    ReportingDbContext db, IServiceScopeFactory scopes, IRoleHolderQuery roleHolders, IOrgDirectory org, IEmployeeDirectory employees, IUserDirectory users,
    INotificationRecipientDirectory recipients, INotificationService notifications, IFileService files, ISettingsStore settings, ReportingClock clock,
    IRequestContext context, IAuditLog audit, ILogger<DeliveryRunner> logger)
{
    public const string FilePurposeKey = "reporting.delivery";

    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Schedules.Where(s => s.Active && s.NextRunAt != null && s.NextRunAt <= now).ToListAsync(ct).ConfigureAwait(false);
        var sent = 0;
        foreach (var schedule in due)
        {
            sent += await RunAsync(schedule, ct).ConfigureAwait(false);
        }

        return sent;
    }

    public async Task<int> RunAsync(Schedule schedule, CancellationToken ct)
    {
        var config = await settings.GetAsync<ReportingSettings>(ct).ConfigureAwait(false);
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var period = PeriodRules.Resolve(schedule.PeriodRule, today, config.FiscalStartMonth);
        var periodKey = schedule.PeriodRule + ":" + period.From.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var targets = (await ExpandAsync(JsonSerializer.Deserialize<List<ScheduleRecipient>>(schedule.RecipientsJson, DashboardService.Json) ?? [], ct).ConfigureAwait(false))
            .Concat(await FieldRecipientsAsync(schedule, ct).ConfigureAwait(false)).Distinct().ToList();
        var done = await db.Deliveries.AsNoTracking().Where(d => d.ScheduleId == schedule.Id && d.PeriodKey == periodKey).Select(d => d.UserId).ToListAsync(ct)
            .ConfigureAwait(false);
        var resolved = (await recipients.ResolveAsync(targets.Except(done).ToList(), ct).ConfigureAwait(false)).ToDictionary(r => r.UserId);
        var sent = 0;
        foreach (var userId in targets.Except(done))
        {
            if (!resolved.TryGetValue(userId, out var recipient) || !recipient.Active)
            {
                continue;
            }

            sent += await DeliverAsync(schedule, userId, recipient.Language, period, periodKey, config, ct).ConfigureAwait(false);
        }

        schedule.Ran(clock.UtcNow, ScheduleClock.Next(schedule.Frequency, schedule.TimeOfDay, schedule.Day, clock.UtcNow, ScheduleClock.Zone(regional.TimeZone)));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sent;
    }

    private async Task<int> DeliverAsync(Schedule schedule, Guid userId, string language, ReportPeriod period, string periodKey, ReportingSettings config, CancellationToken ct)
    {
        var lang = language == "en" ? "en" : "ar";
        Content? content;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var principal = await scope.ServiceProvider.GetRequiredService<TokenPrincipalFactory>()
                .CreateAsync(userId, "reporting-schedule", "schedule", [], null).ConfigureAwait(false);
            if (principal is null)
            {
                await RecordAsync(schedule, userId, periodKey, "skipped", null, 0, "inactive", null, ct).ConfigureAwait(false);
                return 0;
            }

            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(context.CorrelationId, Channels.Job, principal);
            content = await BuildAsync(scope.ServiceProvider, schedule, principal, lang, period, ct).ConfigureAwait(false);
        }

        if (content is null)
        {
            await RecordAsync(schedule, userId, periodKey, "skipped", null, 0, "nothing-visible", null, ct).ConfigureAwait(false);
            return 0;
        }

        Guid? fileId = null;
        var attach = schedule.Format != "link" && config.DeliveryMode == "attachment" && content.File is not null
            && content.File.Content.Length <= config.AttachmentLimitMb * 1024L * 1024L;
        var delivery = new Delivery(schedule.Id, userId, periodKey, "pending", null, 0, null, null);
        if (attach)
        {
            await using var stream = new MemoryStream(content.File!.Content);
            var uploaded = await files.UploadAsync(new FileUpload(FilePurposeKey, delivery.Id.ToString("D"), content.File.FileName, stream), ct).ConfigureAwait(false);
            if (uploaded.IsSuccess)
            {
                fileId = uploaded.Value.Id;
            }
        }

        var values = new Dictionary<string, string>
        {
            ["schedule"] = schedule.Name,
            ["titleAr"] = content.TitleAr,
            ["titleEn"] = content.TitleEn,
            ["period"] = $"{period.From:yyyy-MM} – {period.To:yyyy-MM}",
            ["rows"] = content.Rows.ToString(CultureInfo.InvariantCulture),
        };
        var result = await notifications.SendAsync(new NotificationRequest(
            ReportingNotifications.Scheduled.Key, [NotificationRecipient.User(userId)], values, content.Link, NotificationChannels.Email,
            AttachmentFileIds: fileId is { } f ? [f] : null), cancellationToken: ct).ConfigureAwait(false);
        var status = result.IsSuccess ? "sent" : "failed";
        delivery.Complete(status, content.File is null ? "link" : schedule.Format, content.Rows, result.IsSuccess ? null : result.Error.Code, fileId);
        await SaveAsync(delivery, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("reporting.delivery." + status, result.IsSuccess, ReportingPermissions.SystemCode, "reporting.schedule", schedule.Id.ToString("D"),
            new { recipient = userId, format = schedule.Format, rows = content.Rows, period = periodKey, language = lang }), ct).ConfigureAwait(false);
        return result.IsSuccess ? 1 : 0;
    }

    private sealed record Content(string TitleAr, string TitleEn, ExportedFile? File, int Rows, string Link);

    private static async Task<Content?> BuildAsync(IServiceProvider services, Schedule schedule, ClaimsPrincipal principal, string lang, ReportPeriod period, CancellationToken ct)
    {
        var from = period.From.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var to = period.To.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (schedule.TargetKind == "dashboard")
        {
            var dashboards = services.GetRequiredService<DashboardService>();
            var data = await dashboards.DataAsync(schedule.TargetId.ToString("D"), principal, period.Kind, from, to, true, null, false, ct).ConfigureAwait(false);
            if (data.IsFailure || data.Value.Data.All(d => d.State == "unavailable"))
            {
                return null;
            }

            var link = $"/dashboards/{data.Value.Code}?kind={period.Kind}&from={from}&to={to}";
            if (schedule.Format == "link")
            {
                return new Content(data.Value.TitleAr, data.Value.TitleEn, null, data.Value.Data.Count, link);
            }

            var names = await services.GetRequiredService<UnitNames>().ForAsync(data.Value, lang, ct).ConfigureAwait(false);
            var file = schedule.Format == "xlsx"
                ? DashboardRenderer.Excel(data.Value, lang, names)
                : (await services.GetRequiredService<DashboardRenderer>().PdfAsync(data.Value, lang, names, ct).ConfigureAwait(false)) is { IsSuccess: true } pdf ? pdf.Value : null;
            return file is null ? null : new Content(data.Value.TitleAr, data.Value.TitleEn, file, data.Value.Data.Count, link);
        }

        if (schedule.TargetKind == "dossier")
        {
            return await DossierContentAsync(services, schedule, principal, lang, ct).ConfigureAwait(false);
        }

        var reports = services.GetRequiredService<ReportService>();
        var report = await reports.GetAsync(schedule.TargetId, principal, ct).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return null;
        }

        var reportLink = $"/reports/{schedule.TargetId:D}";
        if (schedule.Format == "link")
        {
            var count = await reports.CountDefinitionAsync(report.Value.Definition, lang, principal, ct).ConfigureAwait(false);
            return count.IsFailure ? null : new Content(report.Value.Name, report.Value.Name, null, count.Value, reportLink);
        }

        var rendered = await reports.RenderDefinitionAsync(report.Value.Definition, schedule.Format, lang, report.Value.Name, principal, ct).ConfigureAwait(false);
        return rendered.IsFailure ? null : new Content(report.Value.Name, report.Value.Name, rendered.Value.File, rendered.Value.Rows, reportLink);
    }

    /// <summary>
    /// The dossiers of a dossier schedule for one recipient (ADR 0186), built as them: the schedule's records they may see — only their own
    /// records when the schedule addresses each record's person — merged into one PDF or a ZIP. Nothing visible → null (no e-mail).
    /// </summary>
    private static async Task<Content?> DossierContentAsync(IServiceProvider services, Schedule schedule, ClaimsPrincipal principal, string lang, CancellationToken ct)
    {
        var dossiers = services.GetRequiredService<Dossiers.DossierTemplateService>();
        var template = (await dossiers.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == schedule.TargetId && t.Active);
        var options = schedule.OptionsJson is null ? null : JsonSerializer.Deserialize<DossierScheduleOptions>(schedule.OptionsJson, DashboardService.Json);
        if (template is null || options is null || !await services.GetRequiredService<IAccessService>().CanAsync(principal, ReportingPermissions.DossierRun, cancellationToken: ct)
                .ConfigureAwait(false))
        {
            return null;
        }

        var filters = new List<ReportFilter>(options.Filters ?? []);
        if (options.RecipientField is { } field)
        {
            if (principal.EmployeeIdOf() is not { } me)
            {
                return null;
            }

            filters.Add(new ReportFilter(field, ReportOperators.Equal, [me.ToString("D")]));
        }

        var selected = await dossiers.SelectAsync(template.Code, filters, principal, ct).ConfigureAwait(false);
        if (selected.IsFailure || selected.Value.Ids.Count == 0)
        {
            return null;
        }

        var docs = new List<Dossiers.DossierDocument>();
        foreach (var id in selected.Value.Ids)
        {
            if (await dossiers.BuildAsync(template, id, principal, ct).ConfigureAwait(false) is { } doc)
            {
                docs.Add(doc);
            }
        }

        if (docs.Count == 0)
        {
            return null;
        }

        var language = options.Language ?? lang;
        var file = await dossiers.FileAsync(docs, schedule.Format, language, ct).ConfigureAwait(false);
        return file.IsFailure ? null : new Content(template.LabelAr, template.LabelEn, file.Value, docs.Count, "/reports/dossiers");
    }

    /// <summary>Recipients by record field (ADR 0186): the people named in that field of the schedule's records, found as the schedule's owner.</summary>
    private async Task<IReadOnlyList<Guid>> FieldRecipientsAsync(Schedule schedule, CancellationToken ct)
    {
        if (schedule.TargetKind != "dossier" || schedule.OptionsJson is null
            || JsonSerializer.Deserialize<DossierScheduleOptions>(schedule.OptionsJson, DashboardService.Json) is not { RecipientField: { } field } options)
        {
            return [];
        }

        await using var scope = scopes.CreateAsyncScope();
        var owner = await scope.ServiceProvider.GetRequiredService<TokenPrincipalFactory>().CreateAsync(schedule.OwnerUserId, "reporting-schedule", "schedule", [], null)
            .ConfigureAwait(false);
        if (owner is null)
        {
            return [];
        }

        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(context.CorrelationId, Channels.Job, owner);
        var dossiers = scope.ServiceProvider.GetRequiredService<Dossiers.DossierTemplateService>();
        var engine = scope.ServiceProvider.GetRequiredService<ReportTypes.ReportTypeEngine>();
        var template = (await dossiers.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Id == schedule.TargetId && t.Active);
        if (template is null)
        {
            return [];
        }

        var definition = new ReportDefinition
        {
            ReportType = ReportTypes.ReportTypeRules.ImplicitPrefix + template.Dataset,
            Columns = [field],
            Filters = options.Filters ?? [],
        };
        var prepared = await engine.PrepareAsync(definition, owner, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return [];
        }

        var (rows, _) = await engine.ComposeAsync(prepared.Value.Type, prepared.Value.Effective, owner, ct).ConfigureAwait(false);
        var employeeIds = rows.Select(r => r.Value(field)).OfType<Guid>().Distinct().ToList();
        return employeeIds.Count == 0 ? [] : (await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id).ToList();
    }

    private Task RecordAsync(Schedule schedule, Guid userId, string periodKey, string status, string? format, int rows, string? detail, Guid? fileId, CancellationToken ct) =>
        SaveAsync(new Delivery(schedule.Id, userId, periodKey, status, format, rows, detail, fileId), ct);

    private async Task SaveAsync(Delivery delivery, CancellationToken ct)
    {
        db.Deliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Unique (schedule, recipient, period): another run delivered it already.
            LogDuplicate(logger, ex, delivery.ScheduleId);
            db.Entry(delivery).State = EntityState.Detached;
        }
    }

    /// <summary>Users behind the recipients at send time: users, role holders (direct, unit and everyone assignments), units and below.</summary>
    private async Task<IReadOnlyList<Guid>> ExpandAsync(IReadOnlyList<ScheduleRecipient> list, CancellationToken ct)
    {
        var userIds = new HashSet<Guid>();
        var unitIds = new HashSet<Guid>();
        var everyone = false;
        foreach (var r in list)
        {
            switch (r.Kind)
            {
                case SharePrincipalKinds.User when Guid.TryParse(r.Ref, out var u):
                    userIds.Add(u);
                    break;
                case SharePrincipalKinds.Unit when Guid.TryParse(r.Ref, out var unit):
                    unitIds.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, ct).ConfigureAwait(false));
                    break;
                case SharePrincipalKinds.Role:
                    var holders = await roleHolders.GetHoldersAsync(r.Ref, ct).ConfigureAwait(false);
                    userIds.UnionWith(holders.UserIds);
                    foreach (var unit in holders.UnitIds)
                    {
                        unitIds.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, ct).ConfigureAwait(false));
                    }

                    everyone |= holders.Everyone;
                    break;
            }
        }

        if (everyone)
        {
            userIds.UnionWith((await users.ListAllAsync(ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id));
        }
        else if (unitIds.Count > 0)
        {
            var employeeIds = await employees.GetEmployeeIdsInUnitsAsync(unitIds, ct).ConfigureAwait(false);
            userIds.UnionWith((await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id));
        }

        return userIds.ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery of schedule {Schedule} already recorded")]
    private static partial void LogDuplicate(ILogger logger, Exception exception, Guid schedule);
}

/// <summary>Names of units used as the <c>unit</c> dimension, for exports.</summary>
public sealed class UnitNames(IOrgDirectory org)
{
    public async Task<DashboardRenderer.UnitName> ForAsync(DashboardDataDto data, string language, CancellationToken ct)
    {
        var ids = data.Data.SelectMany(d => d.Rows ?? []).SelectMany(r => r.Key.Where(k => k.Key == "unit").Select(k => k.Value))
            .Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                names[id.ToString("D")] = (language == "ar" ? unit.NameAr ?? unit.NameEn : unit.NameEn ?? unit.NameAr) ?? unit.Code;
            }
        }

        return id => names.TryGetValue(id, out var name) ? name : id;
    }

    public async Task<IReadOnlyList<UnitNameDto>> ListAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new List<UnitNameDto>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (Guid.TryParse(id, out var g) && await org.GetUnitAsync(g, ct).ConfigureAwait(false) is { } unit)
            {
                result.Add(new UnitNameDto(g, unit.Code, unit.NameAr, unit.NameEn));
            }
        }

        return result;
    }
}

public sealed record UnitNameDto(Guid Id, string Code, string? NameAr, string? NameEn);

/// <summary>Delivered files: only their recipient (and the system) may read them; nobody writes them through the files API.</summary>
internal sealed class DeliveryFiles(ReportingDbContext db) : IFileOwnerAccess
{
    public string OwnerType => "reporting.delivery";

    public string AttachmentPurpose => DeliveryRunner.FilePurposeKey;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        user.IsSystem() || (Guid.TryParse(ownerId, out var id) && user.UserId() is { } userId
            && await db.Deliveries.AnyAsync(d => d.Id == id && d.UserId == userId, cancellationToken).ConfigureAwait(false));

    public Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) => Task.FromResult(user.IsSystem());
}
