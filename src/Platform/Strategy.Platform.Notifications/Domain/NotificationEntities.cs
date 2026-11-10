using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Notifications.Domain;

/// <summary>An editable template (seeded from <see cref="NotificationTemplateDefinition"/>). Edits are audited like any business change.</summary>
[Audited("notifications.template")]
public sealed class NotificationTemplate : AuditableEntity
{
    private NotificationTemplate()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string SubjectAr { get; private set; } = string.Empty;

    public string SubjectEn { get; private set; } = string.Empty;

    public string BodyAr { get; private set; } = string.Empty;

    public string BodyEn { get; private set; } = string.Empty;

    public string? ButtonAr { get; private set; }

    public string? ButtonEn { get; private set; }

    /// <summary>Space-separated placeholder names offered in the editor.</summary>
    public string Placeholders { get; private set; } = string.Empty;

    public bool Mandatory { get; private set; }

    /// <summary>True once an administrator changed the text; seeding then leaves the text alone.</summary>
    public bool Customised { get; private set; }

    public static NotificationTemplate Create(NotificationTemplateDefinition d)
    {
        var template = new NotificationTemplate { Key = d.Key };
        template.Sync(d);
        template.Reset(d);
        return template;
    }

    /// <summary>Names, placeholders and the mandatory flag always follow the code.</summary>
    public void Sync(NotificationTemplateDefinition d)
    {
        SystemCode = d.SystemCode;
        NameAr = d.NameAr;
        NameEn = d.NameEn;
        Placeholders = string.Join(' ', d.Placeholders ?? []);
        Mandatory = d.Mandatory;
        if (!Customised)
        {
            Reset(d);
        }
    }

    public void Reset(NotificationTemplateDefinition d)
    {
        SubjectAr = d.SubjectAr;
        SubjectEn = d.SubjectEn;
        BodyAr = d.BodyAr;
        BodyEn = d.BodyEn;
        ButtonAr = d.ButtonAr;
        ButtonEn = d.ButtonEn;
        Customised = false;
    }

    public void Edit(string subjectAr, string subjectEn, string bodyAr, string bodyEn, string? buttonAr, string? buttonEn)
    {
        SubjectAr = subjectAr.Trim();
        SubjectEn = subjectEn.Trim();
        BodyAr = bodyAr.Trim();
        BodyEn = bodyEn.Trim();
        ButtonAr = string.IsNullOrWhiteSpace(buttonAr) ? null : buttonAr.Trim();
        ButtonEn = string.IsNullOrWhiteSpace(buttonEn) ? null : buttonEn.Trim();
        Customised = true;
    }
}

/// <summary>An item of a user's in-app notification centre.</summary>
public sealed class InAppNotification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>Subject in both languages, rendered when the item was created.</summary>
    public string TitleAr { get; set; } = string.Empty;

    public string TitleEn { get; set; } = string.Empty;

    /// <summary>Placeholder values (JSON), never sensitive values.</summary>
    public string Values { get; set; } = "{}";

    /// <summary>Relative SPA link to the record.</summary>
    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Set when the item was included in a daily digest e-mail.</summary>
    public DateTimeOffset? DigestedAt { get; set; }

    /// <summary>True when the user chose the digest for this event: the individual e-mail was not sent.</summary>
    public bool AwaitingDigest { get; set; }
}

/// <summary>A user's choice per event key (<c>*</c> = default and the daily digest switch).</summary>
[Audited("notifications.preference")]
public sealed class NotificationPreference : AuditableEntity
{
    public const string AllEvents = "*";

    private NotificationPreference()
    {
    }

    public Guid UserId { get; private set; }

    public string EventKey { get; private set; } = AllEvents;

    public bool Email { get; private set; } = true;

    public bool InApp { get; private set; } = true;

    /// <summary>Only on the <c>*</c> row: replace individual e-mails by one daily summary.</summary>
    public bool Digest { get; private set; }

    public static NotificationPreference Create(Guid userId, string eventKey) => new() { UserId = userId, EventKey = eventKey };

    public void Set(bool email, bool inApp, bool digest)
    {
        Email = email;
        InApp = inApp;
        Digest = EventKey == AllEvents && digest;
    }
}

public static class DeliveryStatuses
{
    public const string Sent = "sent";
    public const string Failed = "failed";
}

/// <summary>One e-mail delivery attempt (Settings → Notifications → Deliveries). No body or values are stored.</summary>
public sealed class EmailDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string To { get; set; } = string.Empty;

    public string TemplateKey { get; set; } = string.Empty;

    public string Status { get; set; } = DeliveryStatuses.Sent;

    public string? Error { get; set; }

    public DateTimeOffset At { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary><c>{request key}:{address}</c> when the request had an idempotency key (ADR 0162): a sent delivery is never repeated.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>Idempotency key of a queued notification request (ADR 0162). Technical table, like the outbox; never edited.</summary>
public sealed class SentRequest
{
    public string Key { get; set; } = string.Empty;

    public string TemplateKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
