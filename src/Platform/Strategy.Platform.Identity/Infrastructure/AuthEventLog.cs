using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Sign-in and account events, written into the hash-chained audit trail as <c>auth.&lt;type&gt;</c> events about the user
/// (entity <c>identity.user</c>), so they appear in the Audit Log and the user's history (ADR 0018 closes ADR 0014's interim table).
/// Never records passwords, codes or tokens.
/// </summary>
public sealed class AuthEventLog(IAuditLog audit, IAuditQuery query)
{
    public const string ActionPrefix = "auth.";
    public const string UserEntity = "identity.user";

    public Task WriteAsync(
        string type,
        bool success,
        Guid? userId = null,
        string? userName = null,
        string? provider = null,
        string? detail = null,
        CancellationToken cancellationToken = default) =>
        audit.WriteAsync(
            new AuditEvent(ActionPrefix + type, success, IdentityComponentInfo.SystemCode, UserEntity, userId?.ToString("D"),
                new AuthEventData(Truncate(userName, 256), provider, Truncate(detail, 200)))
            {
                ActorId = userId,
                ActorName = Truncate(userName, 256),
            },
            cancellationToken);

    /// <summary>Sign-in events from the audit trail, newest first (Settings → Users, tests).</summary>
    public async Task<PagedResult<AuthEventDto>> ListAsync(PageRequest page, Guid? userId, string? type, CancellationToken cancellationToken)
    {
        var filter = new AuditFilter(
            EntityType: UserEntity,
            RecordId: userId?.ToString("D"),
            Action: string.IsNullOrWhiteSpace(type) ? "auth" : ActionPrefix + type.Trim(),
            ExactAction: !string.IsNullOrWhiteSpace(type));
        var result = await query.SearchAsync(filter, page, cancellationToken).ConfigureAwait(false);
        return new PagedResult<AuthEventDto>(result.Items.Select(ToDto).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    private static AuthEventDto ToDto(AuditEntryDto e)
    {
        var data = e.Data is null ? null : JsonSerializer.Deserialize<AuthEventData>(e.Data, JsonOptions);
        return new AuthEventDto(e.Id, e.Action[ActionPrefix.Length..], e.Success ?? true, Guid.TryParse(e.RecordId, out var id) ? id : null,
            data?.UserName, e.UserId, data?.Provider, data?.Detail, e.IpAddress, e.OccurredAt);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];

    private sealed record AuthEventData(string? UserName, string? Provider, string? Detail);
}

internal static class IdentityComponentInfo
{
    public const string SystemCode = "identity";
}
