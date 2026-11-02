using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Platform.Audit.Application;

internal sealed class AuditQuery(AuditDbContext db) : IAuditQuery
{
    public async Task<PagedResult<AuditEntryDto>> HistoryAsync(
        string entityType, string recordId, PageRequest page, string? field = null, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Entries.AsNoTracking()
            .Where(e => (e.EntityType == entityType && e.RecordId == recordId) || (e.ParentType == entityType && e.ParentId == recordId));
        if (!string.IsNullOrWhiteSpace(field))
        {
            query = query.Where(e => e.Field == field);
        }

        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }

        return await PageAsync(query, page, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecordSnapshotDto> AsOfAsync(string entityType, string recordId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var rows = await db.Entries.AsNoTracking()
            .Where(e => e.Kind == AuditKinds.Change && e.EntityType == entityType && e.RecordId == recordId && e.OccurredAt <= at)
            .OrderBy(e => e.Seq)
            .Select(e => new { e.Action, e.Field, e.NewValue })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var existed = false;
        foreach (var row in rows)
        {
            switch (row.Action)
            {
                case AuditActions.Create:
                    existed = true;
                    fields.Clear();
                    foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string?>>(row.NewValue ?? "{}") ?? [])
                    {
                        fields[key] = value;
                    }

                    break;
                case AuditActions.Update when row.Field is not null:
                    existed = true; // records created before the audit trail have no snapshot: rebuild what is known
                    fields[row.Field] = row.NewValue;
                    break;
                case AuditActions.Delete:
                    existed = false;
                    fields.Clear();
                    break;
            }
        }

        return new RecordSnapshotDto(entityType, recordId, at, existed, fields);
    }

    public async Task<PagedResult<AuditEntryDto>> SearchAsync(AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default) =>
        await PageAsync(Filter(db.Entries.AsNoTracking(), filter), page, cancellationToken).ConfigureAwait(false);

    public static IQueryable<AuditEntry> Filter(IQueryable<AuditEntry> query, AuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.UserId is { } user)
        {
            query = query.Where(e => e.UserId == user);
        }

        if (!string.IsNullOrWhiteSpace(filter.SystemCode))
        {
            query = query.Where(e => e.SystemCode == filter.SystemCode);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(e => e.EntityType == filter.EntityType || e.ParentType == filter.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.RecordId))
        {
            query = query.Where(e => e.RecordId == filter.RecordId || e.ParentId == filter.RecordId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            // "auth" matches every auth.* event; "create" matches exactly.
            var action = filter.Action.Trim();
            query = filter.ExactAction
                ? query.Where(e => e.Action == action)
                : query.Where(e => e.Action == action || e.Action.StartsWith(action + "."));
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            query = query.Where(e => e.Channel == filter.Channel);
        }

        if (!string.IsNullOrWhiteSpace(filter.Kind))
        {
            query = query.Where(e => e.Kind == filter.Kind);
        }

        if (!string.IsNullOrWhiteSpace(filter.Field))
        {
            query = query.Where(e => e.Field == filter.Field);
        }

        if (filter.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(e => e.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = "%" + filter.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(e => EF.Functions.ILike(e.UserName ?? string.Empty, pattern)
                || EF.Functions.ILike(e.RecordId ?? string.Empty, pattern)
                || EF.Functions.ILike(e.EntityType ?? string.Empty, pattern)
                || EF.Functions.ILike(e.Field ?? string.Empty, pattern)
                || EF.Functions.ILike(e.OldValue ?? string.Empty, pattern)
                || EF.Functions.ILike(e.NewValue ?? string.Empty, pattern)
                || EF.Functions.ILike(e.Data ?? string.Empty, pattern));
        }

        return query;
    }

    private static async Task<PagedResult<AuditEntryDto>> PageAsync(IQueryable<AuditEntry> query, PageRequest page, CancellationToken ct)
    {
        var paging = page.Normalised();
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByDescending(e => e.Seq).Skip(paging.Skip).Take(paging.PageSize).Select(ToDto).ToListAsync(ct)
            .ConfigureAwait(false);
        return new PagedResult<AuditEntryDto>(items, paging.Page, paging.PageSize, total);
    }

    public static readonly System.Linq.Expressions.Expression<Func<AuditEntry, AuditEntryDto>> ToDto = e => new AuditEntryDto(
        e.Id, e.Seq, e.OccurredAt, e.Kind, e.Action, e.SystemCode, e.EntityType, e.RecordId, e.ParentType, e.ParentId, e.Field, e.OldValue,
        e.NewValue, e.ChangeSetId, e.UserId, e.UserName, e.OnBehalfOfId, e.OnBehalfOfName, e.Channel, e.CorrelationId, e.IpAddress, e.UserAgent,
        e.Reason, e.Success, e.Data);
}
