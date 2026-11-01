using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Email;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Notifications.Application;

public sealed record NotificationDto(Guid Id, string TemplateKey, string TitleAr, string TitleEn, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record MyNotificationsDto(IReadOnlyList<NotificationDto> Items, int Unread, int TotalCount, int Page, int PageSize);

public sealed record NotificationPreferenceDto(string EventKey, string NameAr, string NameEn, bool Email, bool InApp, bool Mandatory);

public sealed record NotificationPreferencesDto(bool Digest, bool Email, bool InApp, IReadOnlyList<NotificationPreferenceDto> Events);

public sealed record NotificationPreferenceInput(string EventKey, bool Email, bool InApp);

public sealed record NotificationPreferencesInput(bool Digest, bool Email, bool InApp, IReadOnlyList<NotificationPreferenceInput>? Events);

public sealed record TemplateDto(
    string Key, string SystemCode, string NameAr, string NameEn, string SubjectAr, string SubjectEn, string BodyAr, string BodyEn, string? ButtonAr,
    string? ButtonEn, IReadOnlyList<string> Placeholders, bool Mandatory, bool Customised, uint Version);

public sealed record TemplateInput(string? SubjectAr, string? SubjectEn, string? BodyAr, string? BodyEn, string? ButtonAr, string? ButtonEn, uint Version);

public sealed record TemplatePreviewDto(string Subject, string Html);

public sealed record DeliveryDto(Guid Id, string To, string TemplateKey, string Status, string? Error, DateTimeOffset At);

/// <summary>The caller's notification centre and preferences, and template administration.</summary>
internal sealed class NotificationAdministration(
    NotifyDbContext db, IRequestContext context, IClock clock, ISettingsStore settings, IEnumerable<NotificationTemplateDefinition> definitions)
{
    // ---- My notifications ----

    public async Task<MyNotificationsDto> MineAsync(PageRequest page, bool unreadOnly, CancellationToken ct)
    {
        var userId = context.UserId ?? Guid.Empty;
        var paging = page.Normalised();
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByDescending(n => n.CreatedAt).Skip(paging.Skip).Take(paging.PageSize)
            .Select(n => new NotificationDto(n.Id, n.TemplateKey, n.TitleAr, n.TitleEn, n.Link, n.CreatedAt, n.ReadAt)).ToListAsync(ct).ConfigureAwait(false);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct).ConfigureAwait(false);
        return new MyNotificationsDto(items, unread, total, paging.Page, paging.PageSize);
    }

    public async Task<Result> MarkReadAsync(Guid? id, CancellationToken ct)
    {
        var userId = context.UserId ?? Guid.Empty;
        var query = db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null);
        if (id is { } one)
        {
            if (!await db.Notifications.AnyAsync(n => n.Id == one && n.UserId == userId, ct).ConfigureAwait(false))
            {
                return Error.NotFound(); // someone else's item looks like a missing one
            }

            query = query.Where(n => n.Id == one);
        }

        var now = clock.UtcNow;
        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct).ConfigureAwait(false);
        return Result.Success();
    }

    // ---- Preferences ----

    public async Task<NotificationPreferencesDto> PreferencesAsync(CancellationToken ct)
    {
        var userId = context.UserId ?? Guid.Empty;
        var rows = await db.Preferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct).ConfigureAwait(false);
        var general = rows.Find(p => p.EventKey == NotificationPreference.AllEvents);
        var events = definitions.Where(d => d.Key != NotificationTemplates.Layout.Key && d.Key != NotificationTemplates.Digest.Key)
            .DistinctBy(d => d.Key).OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d =>
            {
                var row = rows.Find(p => p.EventKey == d.Key);
                return new NotificationPreferenceDto(d.Key, d.NameAr, d.NameEn, d.Mandatory || (row?.Email ?? general?.Email ?? true), row?.InApp ?? general?.InApp ?? true, d.Mandatory);
            }).ToList();
        return new NotificationPreferencesDto(general?.Digest ?? false, general?.Email ?? true, general?.InApp ?? true, events);
    }

    public async Task<NotificationPreferencesDto> SavePreferencesAsync(NotificationPreferencesInput input, CancellationToken ct)
    {
        var userId = context.UserId ?? throw new InvalidOperationException("Signed-in user required.");
        var rows = await db.Preferences.Where(p => p.UserId == userId).ToListAsync(ct).ConfigureAwait(false);
        NotificationPreference Row(string key)
        {
            var row = rows.Find(p => p.EventKey == key);
            if (row is null)
            {
                row = NotificationPreference.Create(userId, key);
                db.Preferences.Add(row);
                rows.Add(row);
            }

            return row;
        }

        Row(NotificationPreference.AllEvents).Set(input.Email, input.InApp, input.Digest);
        var known = definitions.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in input.Events ?? [])
        {
            if (known.Contains(item.EventKey))
            {
                Row(item.EventKey).Set(item.Email, item.InApp, digest: false);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PreferencesAsync(ct).ConfigureAwait(false);
    }

    // ---- Templates ----

    public async Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct) =>
        (await db.Templates.AsNoTracking().OrderBy(t => t.SystemCode).ThenBy(t => t.Key).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto).ToList();

    public async Task<Result<TemplateDto>> UpdateTemplateAsync(string key, TemplateInput input, CancellationToken ct)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Key == key, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound();
        }

        var fields = new List<FieldError>();
        Required(fields, "subjectAr", input.SubjectAr, 300);
        Required(fields, "subjectEn", input.SubjectEn, 300);
        Required(fields, "bodyAr", input.BodyAr, 8000);
        Required(fields, "bodyEn", input.BodyEn, 8000);
        Optional(fields, "buttonAr", input.ButtonAr, 100);
        Optional(fields, "buttonEn", input.ButtonEn, 100);
        if (fields.Count > 0)
        {
            return Error.Validation(fields);
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
        template.Edit(input.SubjectAr!, input.SubjectEn!, input.BodyAr!, input.BodyEn!, input.ButtonAr, input.ButtonEn);
        return await SaveAsync(template, ct).ConfigureAwait(false);
    }

    public async Task<Result<TemplateDto>> ResetTemplateAsync(string key, uint version, CancellationToken ct)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Key == key, ct).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(d => d.Key == key);
        if (template is null || definition is null)
        {
            return Error.NotFound();
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = version;
        template.Reset(definition);
        return await SaveAsync(template, ct).ConfigureAwait(false);
    }

    /// <summary>Renders the template (saved text, or the unsaved text from the editor) with sample values.</summary>
    public async Task<Result<TemplatePreviewDto>> PreviewAsync(string key, TemplateInput? draft, CancellationToken ct)
    {
        var templates = await db.Templates.AsNoTracking().Where(t => t.Key == key || t.Key == NotificationTemplates.Layout.Key).ToListAsync(ct)
            .ConfigureAwait(false);
        var template = templates.Find(t => t.Key == key);
        if (template is null)
        {
            return Error.NotFound();
        }

        var text = draft is null
            ? TemplateText.From(template)
            : new TemplateText(draft.SubjectAr ?? string.Empty, draft.SubjectEn ?? string.Empty, draft.BodyAr ?? string.Empty, draft.BodyEn ?? string.Empty,
                draft.ButtonAr, draft.ButtonEn);
        var layout = templates.Find(t => t.Key == NotificationTemplates.Layout.Key);
        var sample = template.Placeholders.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(p => p, p => p == "code" ? "123456" : $"[{p}]", StringComparer.Ordinal);
        sample.TryAdd("name", "[name]");
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var (subject, html, _) = EmailRenderer.Render(text, layout is null ? NotificationTemplates.LayoutText : TemplateText.From(layout), sample,
            "https://example.invalid/", branding);
        return new TemplatePreviewDto(subject, html);
    }

    public async Task<PagedResult<DeliveryDto>> DeliveriesAsync(PageRequest page, string? status, CancellationToken ct)
    {
        var paging = page.Normalised();
        var query = db.Deliveries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(d => d.Status == status);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByDescending(d => d.At).Skip(paging.Skip).Take(paging.PageSize)
            .Select(d => new DeliveryDto(d.Id, d.To, d.TemplateKey, d.Status, d.Error, d.At)).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<DeliveryDto>(items, paging.Page, paging.PageSize, total);
    }

    private async Task<Result<TemplateDto>> SaveAsync(NotificationTemplate template, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(template);
    }

    private static TemplateDto ToDto(NotificationTemplate t) => new(
        t.Key, t.SystemCode, t.NameAr, t.NameEn, t.SubjectAr, t.SubjectEn, t.BodyAr, t.BodyEn, t.ButtonAr, t.ButtonEn,
        t.Placeholders.Split(' ', StringSplitOptions.RemoveEmptyEntries), t.Mandatory, t.Customised, t.Version);

    private static void Required(List<FieldError> fields, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new FieldError(field, "validation.notEmpty", "validation.notEmpty"));
        }
        else
        {
            Optional(fields, field, value, max);
        }
    }

    private static void Optional(List<FieldError> fields, string field, string? value, int max)
    {
        if (value is { Length: > 0 } && value.Length > max)
        {
            fields.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max }));
        }
    }

}
