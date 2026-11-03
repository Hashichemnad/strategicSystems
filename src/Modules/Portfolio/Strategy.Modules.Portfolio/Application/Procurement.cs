using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>A file on a request with its kind (<c>document</c>, <c>cost-estimation</c>, <c>project-scope</c>, <c>quotation</c>).</summary>
public sealed record RequestFileDto(string Kind, FileDto File);

public sealed record ListRequestFiles(Guid Id) : IQuery<IReadOnlyList<RequestFileDto>>;

public sealed record UploadRequestFile(Guid Id, string Kind, string FileName, Stream Content, string? Comment) : ICommand<RequestFileDto>;

public sealed record HandOffRequest(Guid Id, uint Version) : ICommand<RequestDto>;

public sealed record ConfirmQuotations(Guid Id, uint Version) : ICommand<RequestDto>;

/// <summary>Cost files, the Administration hand-off and quotations (SPEC W1.3, ADR 0051).</summary>
internal sealed class ProcurementHandlers(
    PortfolioDbContext db,
    RequestReader reader,
    RequestQueries queries,
    RequestFileFlags flags,
    PortfolioNotifier notifier,
    IFileService files,
    IRecordRoleService recordRoles,
    IRoleHolderQuery roles,
    IOrgDirectory org,
    IEmployeeDirectory employees,
    IRequestContext context,
    IClock clock)
    : IQueryHandler<ListRequestFiles, IReadOnlyList<RequestFileDto>>,
        ICommandHandler<UploadRequestFile, RequestFileDto>,
        ICommandHandler<HandOffRequest, RequestDto>,
        ICommandHandler<ConfirmQuotations, RequestDto>
{
    public async Task<Result<IReadOnlyList<RequestFileDto>>> HandleAsync(ListRequestFiles query, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        var all = await files.ListAsync(PortfolioPermissions.RequestResource, request.Id.ToString("D"), cancellationToken: cancellationToken).ConfigureAwait(false);
        return all.Where(f => RequestFilePurposes.OnRequest.Any(p => p.Key == f.Purpose)).OrderBy(f => f.UpdatedAt)
            .Select(f => new RequestFileDto(RequestFilePurposes.KindOf(f.Purpose), f)).ToList();
    }

    public async Task<Result<RequestFileDto>> HandleAsync(UploadRequestFile command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        var purpose = RequestFilePurposes.ByKind(command.Kind);
        if (purpose is null)
        {
            return Error.Validation([new FieldError("kind", "validation.unknownCode", "validation.unknownCode")]);
        }

        var user = context.User;
        var allowed = purpose == RequestFilePurposes.Quotation
            ? request.Status == RequestStatuses.Draft && request.ProcurementStatus == ProcurementStatuses.WaitingQuotations
                && await reader.CanAsync(user, PortfolioPermissions.RequestQuotations, request, cancellationToken).ConfigureAwait(false)
            : purpose == RequestFilePurposes.Document
                ? await CanAttachAsync(request, user, cancellationToken).ConfigureAwait(false)
                : request.Status == RequestStatuses.Draft && (purpose != RequestFilePurposes.ProjectScope || request.ProcurementStatus is null)
                    && await reader.CanAsync(user, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            return Error.Forbidden();
        }

        var uploaded = await files.UploadAsync(new FileUpload(purpose.Key, request.Id.ToString("D"), command.FileName, command.Content, command.Comment), cancellationToken)
            .ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        if (request.Status == RequestStatuses.Draft)
        {
            await flags.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RequestFileDto(RequestFilePurposes.KindOf(purpose.Key), uploaded.Value);
    }

    public async Task<Result<RequestDto>> HandleAsync(HandOffRequest command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        await flags.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
        var handed = request.HandOff(clock.UtcNow);
        if (handed.IsFailure)
        {
            return handed.Error;
        }

        var (employeeIds, userIds) = await AdministrationAsync(cancellationToken).ConfigureAwait(false);
        if (employeeIds.Count == 0 && userIds.Count == 0)
        {
            return Error.Rule("portfolio.request.noAdministration", "portfolio.errors.noAdministration");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        foreach (var employee in employeeIds)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.RequestResource, request.Id, RecordPrincipalKinds.Employee, employee,
                PortfolioPermissions.ProcurementRole, cancellationToken).ConfigureAwait(false);
        }

        foreach (var userId in userIds)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.RequestResource, request.Id, RecordPrincipalKinds.User, userId,
                PortfolioPermissions.ProcurementRole, cancellationToken).ConfigureAwait(false);
        }

        var recipients = userIds.ToHashSet();
        foreach (var user in await UsersOfAsync(employeeIds, cancellationToken).ConfigureAwait(false))
        {
            recipients.Add(user);
        }

        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(PortfolioNotifications.ScopeSubmitted, request, recipients, null, db, cancellationToken, actor: actor).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(ConfirmQuotations command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestQuotations, request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var quotations = await files.ListAsync(PortfolioPermissions.RequestResource, request.Id.ToString("D"), RequestFilePurposes.Quotation.Key, cancellationToken)
            .ConfigureAwait(false);
        if (quotations.Count == 0)
        {
            return Error.Rule("portfolio.request.noQuotation", "portfolio.errors.quotationsMissing");
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var confirmed = request.ConfirmQuotations(clock.UtcNow);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (request.RequesterUserId is { } requester)
        {
            await notifier.SendAsync(PortfolioNotifications.QuotationsUploaded, request, [requester], null, db, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CanAttachAsync(ProgramRequest request, ClaimsPrincipal user, CancellationToken ct) =>
        (request.Status == RequestStatuses.Draft && await reader.CanAsync(user, PortfolioPermissions.RequestEdit, request, ct).ConfigureAwait(false))
        || (!RequestStatuses.IsFinal(request.Status) && await reader.CanAsync(user, PortfolioPermissions.RequestManage, request, ct).ConfigureAwait(false));

    /// <summary>Holders of the Administration role today: users assigned directly and employees of assigned units (and below).</summary>
    private async Task<(List<Guid> Employees, List<Guid> Users)> AdministrationAsync(CancellationToken ct)
    {
        var holders = await roles.GetHoldersAsync(PortfolioPermissions.AdministrationRoleKey, ct).ConfigureAwait(false);
        var employeeIds = new List<Guid>();
        if (holders.UnitIds.Count > 0)
        {
            var units = new HashSet<Guid>();
            foreach (var unit in holders.UnitIds)
            {
                units.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, ct).ConfigureAwait(false));
            }

            employeeIds.AddRange(await employees.GetEmployeeIdsInUnitsAsync(units.ToList(), ct).ConfigureAwait(false));
        }

        return (employeeIds.Distinct().ToList(), holders.UserIds.Distinct().ToList());
    }

    private async Task<IReadOnlyList<Guid>> UsersOfAsync(List<Guid> employeeIds, CancellationToken ct) =>
        employeeIds.Count == 0
            ? []
            : (await reader.UsersOfEmployeesAsync(employeeIds, ct).ConfigureAwait(false));

    private async Task<string?> ActorAsync(CancellationToken ct)
    {
        var me = context.User.EmployeeIdOf();
        var people = await reader.PeopleAsync([me], ct).ConfigureAwait(false);
        return me is { } id && people.TryGetValue(id, out var person) ? $"{person.NameAr} / {person.NameEn}" : null;
    }
}

/// <summary>Keeps the request's cost-file flags in line with its files (uploads, deletions through the generic files API).</summary>
internal sealed class RequestFileFlags(IFileService files)
{
    public async Task RefreshAsync(ProgramRequest request, CancellationToken ct)
    {
        var owner = request.Id.ToString("D");
        var estimation = await files.OwnersWithFilesAsync(RequestFilePurposes.CostEstimation.Key, [owner], ct).ConfigureAwait(false);
        var scope = await files.OwnersWithFilesAsync(RequestFilePurposes.ProjectScope.Key, [owner], ct).ConfigureAwait(false);
        request.SetFileFlags(estimation.Contains(owner), scope.Contains(owner));
    }
}

/// <summary>Files of a request: readers of the request read them; editors (draft) and the PM Office (until decided) change attachments.</summary>
internal sealed class RequestFiles(RequestReader reader) : IFileOwnerAccess
{
    public string OwnerType => PortfolioPermissions.RequestResource;

    public string AttachmentPurpose => RequestFilePurposes.Document.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id) || await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } request)
        {
            return false;
        }

        return (request.Status == RequestStatuses.Draft && await reader.CanAsync(user, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false))
            || (!RequestStatuses.IsFinal(request.Status) && await reader.CanAsync(user, PortfolioPermissions.RequestManage, request, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>The Project Scope template (Settings → Portfolio): anyone who may create requests downloads it; settings managers replace it.</summary>
internal sealed class TemplateFiles(IAccessService access) : IFileOwnerAccess
{
    public const string OwnerType = "portfolio.settings";
    public const string OwnerId = "project-scope-template";

    string IFileOwnerAccess.OwnerType => OwnerType;

    public string AttachmentPurpose => RequestFilePurposes.Template.Key;

    public Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        access.CanAsync(user, PortfolioPermissions.RequestCreate, cancellationToken: cancellationToken);

    public Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        access.CanAsync(user, PortfolioPermissions.SettingsManage, cancellationToken: cancellationToken);
}
