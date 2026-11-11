using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Strategy.Platform.Audit;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

public sealed record ExportScheduleDto(
    Guid Id, Guid ClientId, string Name, IReadOnlyList<string> RecordTypes, string Format, string Folder, int EveryHours, bool Active, DateTimeOffset? LastRunAt,
    string? LastRunStatus, uint Version);

public sealed record ExportScheduleInput(string? Name, IReadOnlyList<string>? RecordTypes, string? Format, string? Folder, int? EveryHours, bool? Active, uint Version = 0);

/// <summary>
/// Scheduled exports to a folder under the operator's <c>Integration:ExportRoot</c> (ADR 0193): each run writes the changes since the last
/// watermark per record type, read as the client (its scope). SFTP / S3 targets are deferred.
/// </summary>
public sealed class ScheduledExports(
    IntegrationDbContext db, FeedService feeds, FeedCatalog catalog, IntegrationClientAuthority authority, IConfiguration configuration, IAuditLog audit, IClock clock)
{
    public string? Root => configuration["Integration:ExportRoot"] is { Length: > 0 } root ? Path.GetFullPath(root) : null;

    public async Task<Result<IReadOnlyList<ExportScheduleDto>>> ListAsync(Guid clientId, CancellationToken ct)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.ExportSchedules.AsNoTracking().Where(s => s.ClientRef == clientId).OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<ExportScheduleDto>> SaveAsync(Guid clientId, Guid? id, ExportScheduleInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
        {
            errors.Add(Field("name", "validation.notEmpty"));
        }

        if (input.RecordTypes is not { Count: > 0 } types || types.Any(t => catalog.Find(t) is not { } type || !client.AllowedSystems.Contains(type.SystemCode)
            || (client.AllowedRecordTypes.Count > 0 && !client.AllowedRecordTypes.Contains(t))))
        {
            errors.Add(Field("recordTypes", "integration.errors.recordTypeOutsideSystems"));
        }

        if (input.Format is not ("json" or "csv"))
        {
            errors.Add(Field("format", "validation.unknownCode"));
        }

        if (Root is null)
        {
            errors.Add(Field("folder", "integration.errors.exportRootMissing"));
        }
        else if (FolderPath(input.Folder) is null)
        {
            errors.Add(Field("folder", "integration.errors.exportFolder"));
        }

        if (input.EveryHours is not (>= 1 and <= 168))
        {
            errors.Add(Field("everyHours", "validation.range"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        ExportSchedule schedule;
        if (id is { } existing)
        {
            var found = await db.ExportSchedules.FirstOrDefaultAsync(s => s.Id == existing && s.ClientRef == clientId, ct).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            schedule = found;
            db.Entry(schedule).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            schedule = ExportSchedule.Create(clientId);
            db.ExportSchedules.Add(schedule);
        }

        schedule.Update(input.Name!, input.RecordTypes!, input.Format!, input.Folder ?? string.Empty, input.EveryHours!.Value, input.Active ?? true);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(schedule);
    }

    /// <returns>The number of schedules run.</returns>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = (await db.ExportSchedules.Where(s => s.Active).ToListAsync(ct).ConfigureAwait(false)).Where(s => s.IsDue(now)).ToList();
        foreach (var schedule in due)
        {
            await RunAsync(schedule, ct).ConfigureAwait(false);
        }

        return due.Count;
    }

    public async Task RunAsync(ExportSchedule schedule, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var now = clock.UtcNow;
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == schedule.ClientRef, ct).ConfigureAwait(false);
        var folder = FolderPath(schedule.Folder);
        if (client is null || !client.IsUsable(now) || folder is null)
        {
            schedule.Ran(now, client is null || !client.IsUsable(now) ? "client inactive" : "export folder unavailable", schedule.Watermarks);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var principal = authority.PrincipalFor(client);
        var visible = await catalog.VisibleAsync(principal, null, client.AllowedRecordTypes, ct).ConfigureAwait(false);
        var marks = new Dictionary<string, DateTimeOffset>(schedule.Watermarks, StringComparer.Ordinal);
        var written = 0;
        Directory.CreateDirectory(folder);
        foreach (var code in schedule.RecordTypes)
        {
            if (visible.FirstOrDefault(t => t.Code == code) is not { } type)
            {
                continue;
            }

            var items = new List<Dictionary<string, object?>>();
            DateTimeOffset? since = marks.TryGetValue(code, out var mark) ? mark : null;
            RecordCursor? cursor = null;
            ExtractPage page;
            do
            {
                page = await feeds.ExtractAsync(principal, type, RecordQueryMode.Current, since, cursor, null, "en", ct).ConfigureAwait(false);
                items.AddRange(page.Items);
                if (page.Watermark is { } w)
                {
                    marks[code] = w;
                }

                Cursor.TryDecode(page.NextCursor, out cursor);
            }
            while (page.HasMore && items.Count < 1_000_000);

            if (items.Count == 0)
            {
                continue;
            }

            var file = Path.Combine(folder, $"{type.EntitySet}-{now:yyyyMMddHHmmss}.{schedule.Format}");
            var content = schedule.Format == "csv"
                ? FeedService.Csv(page with { Items = items })
                : JsonSerializer.Serialize(new { system = type.SystemCode, entitySet = type.EntitySet, exportedAt = now, items });
            await File.WriteAllTextAsync(file, content, ct).ConfigureAwait(false);
            written += items.Count;
        }

        schedule.Ran(now, $"ok rows={written}", marks);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.export.run", true, IntegrationPermissions.SystemCode, ExportSchedule.EntityType, schedule.Id.ToString(),
            new { rows = written }), ct).ConfigureAwait(false);
    }

    /// <summary>The absolute folder, only inside the export root (no traversal, no absolute paths).</summary>
    public string? FolderPath(string? relative)
    {
        if (Root is not { } root || relative is null || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    public static void Map(RouteGroupBuilder group)
    {
        var exports = group.MapGroup("/clients/{clientId:guid}/exports");
        exports.MapGet("/", async (Guid clientId, ScheduledExports s, CancellationToken ct) => (await s.ListAsync(clientId, ct)).ToOk())
            .WithName("ListExportSchedules").RequirePermission(IntegrationPermissions.ClientManage);
        exports.MapPost("/", async (Guid clientId, ExportScheduleInput body, ScheduledExports s, CancellationToken ct) => (await s.SaveAsync(clientId, null, body, ct)).ToOk())
            .WithName("CreateExportSchedule").RequirePermission(IntegrationPermissions.ClientManage);
        exports.MapPut("/{id:guid}", async (Guid clientId, Guid id, ExportScheduleInput body, ScheduledExports s, CancellationToken ct) =>
                (await s.SaveAsync(clientId, id, body, ct)).ToOk())
            .WithName("UpdateExportSchedule").RequirePermission(IntegrationPermissions.ClientManage);
    }

    private static ExportScheduleDto ToDto(ExportSchedule s) =>
        new(s.Id, s.ClientRef, s.Name, s.RecordTypes, s.Format, s.Folder, s.EveryHours, s.Active, s.LastRunAt, s.LastRunStatus, s.Version);

    private static FieldError Field(string field, string key) => new(field, key, key);
}
