using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Requests.Application;

/// <summary>
/// Loads requests within the caller's visibility (docs/design/requests.md §2.9): read scope (own = requester) or record role, or processor of
/// the request's type. Applied in the database query; 404 otherwise.
/// </summary>
internal sealed class RequestReader(
    RequestsDbContext db, IAccessService access, ISystemState systems, RequestTypeCache types, RoleMembership membership)
{
    private IReadOnlyCollection<string>? _processorTypes;

    public async Task<IQueryable<Request>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Requests : db.Requests.AsNoTracking();
        var scoped = await access.ApplyScopeAsync(tracked ? db.Requests : db.Requests.AsNoTracking(), user, RequestPermissions.Read, RequestScope.Map, ct).ConfigureAwait(false);
        var processed = (await ProcessorTypesAsync(user, ct).ConfigureAwait(false)).ToList();
        var userId = user.UserId();
        var scopedIds = scoped.Select(r => r.Id);
        return source.Where(r => scopedIds.Contains(r.Id) || (processed.Contains(r.TypeCode) && r.Status != RequestStatuses.Draft)
            || (userId != null && r.RequesterUserId == userId));
    }

    public async Task<Request?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    /// <summary>Types with at least one processing stage the user processes.</summary>
    public async Task<IReadOnlyCollection<string>> ProcessorTypesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (_processorTypes is not null)
        {
            return _processorTypes;
        }

        var result = new List<string>();
        foreach (var type in types.All)
        {
            foreach (var stage in type.Stages.Where(s => s.Kind == StageKinds.Processing))
            {
                if (await IsProcessorAsync(user, type, stage, ct).ConfigureAwait(false))
                {
                    result.Add(type.Code);
                    break;
                }
            }
        }

        _processorTypes = result;
        return result;
    }

    public async Task<bool> IsProcessorAsync(ClaimsPrincipal user, TypeModel type, StageDefinition stage, CancellationToken ct)
    {
        if (stage.Kind != StageKinds.Processing || !await systems.IsEnabledAsync(type.SystemCode, ct).ConfigureAwait(false))
        {
            return false;
        }

        var permission = type.IsBuiltIn ? RequestPermissions.ProcessPermissionOf(type.SystemCode) : RequestPermissions.Process;
        var roles = stage.ProcessorRoleKeys.Count > 0 ? stage.ProcessorRoleKeys : [RequestPermissions.OfficeRoleOf(type.SystemCode)];
        if (type.IsBuiltIn)
        {
            // Built-in types: the system's processing permission (with All) is enough; otherwise one of the stage's roles.
            return await access.CanAsync(user, permission, cancellationToken: ct).ConfigureAwait(false)
                || await membership.HoldsAnyAsync(user, roles, ct).ConfigureAwait(false);
        }

        return await access.CanAsync(user, permission, cancellationToken: ct).ConfigureAwait(false)
            && await membership.HoldsAnyAsync(user, roles, ct).ConfigureAwait(false);
    }

    public async Task<bool> CanSubmitAsync(ClaimsPrincipal user, TypeModel type, CancellationToken ct)
    {
        if (!type.Active || !await systems.IsEnabledAsync(type.SystemCode, ct).ConfigureAwait(false)
            || !await access.CanAsync(user, RequestPermissions.Submit, cancellationToken: ct).ConfigureAwait(false))
        {
            return false;
        }

        var rule = type.Submitters;
        if (rule.Everyone)
        {
            return true;
        }

        if (rule.RoleKeys is { Count: > 0 } keys && await membership.HoldsAnyAsync(user, keys, ct).ConfigureAwait(false))
        {
            return true;
        }

        return rule.UnitIds is { Count: > 0 } units && (await membership.UnitPathAsync(user, ct).ConfigureAwait(false)).Any(units.Contains);
    }

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, Request request, CancellationToken ct) =>
        access.CanAsync(user, permission, RequestScope.Map.RefOf(request), ct);

    public static bool IsRequester(ClaimsPrincipal user, Request request) =>
        (user.UserId() is { } u && request.RequesterUserId == u) || (user.EmployeeIdOf() is { } e && request.RequesterEmployeeId == e);
}

/// <summary>Encrypts / decrypts <c>secret</c> values of forms (ADR 0086).</summary>
internal sealed class FormSecrets(IDataProtectionProvider provider)
{
    public const string Purpose = "requests.form.secret.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string cipher)
    {
        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public JsonObject Plain(FormSchema schema, JsonObject stored) => SecretValues.ToPlain(schema, stored, Unprotect);

    public JsonObject Stored(FormSchema schema, JsonObject plain) => SecretValues.ToStored(schema, plain, Protect);

    public static JsonObject Masked(FormSchema schema, JsonObject stored) => SecretValues.ToMasked(schema, stored);
}

/// <summary>Reference checks of form values for the validator (lookups, people, units, the request's files, readable records).</summary>
internal sealed class FormReferenceResolver(
    ILookupService lookups, IEmployeeDirectory employees, IOrgDirectory org, IFileService files, ILinkRegistry links, ClaimsPrincipal user, Guid? requestId)
    : IFormReferenceResolver
{
    public Task<bool> IsActiveLookupAsync(string lookupType, string code, CancellationToken ct) => lookups.IsActiveCodeAsync(lookupType, code, ct);

    public async Task<bool> EmployeeExistsAsync(Guid id, CancellationToken ct) => await employees.GetAsync(id, ct).ConfigureAwait(false) is not null;

    public async Task<bool> UnitExistsAsync(Guid id, CancellationToken ct) => await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { Active: true };

    public async Task<bool> IsRequestFileAsync(Guid fileId, CancellationToken ct) =>
        requestId is { } request && await files.FindAsync(fileId, ct).ConfigureAwait(false) is { } file
        && file.OwnerType == Request.EntityType && file.OwnerId == request.ToString("D");

    public async Task<bool> CanReadRecordAsync(string targetType, Guid id, CancellationToken ct) =>
        (await links.ResolveAsync([new LinkRef(targetType, id)], user, ct).ConfigureAwait(false)) is [{ Visibility: LinkVisibility.Visible }];
}
