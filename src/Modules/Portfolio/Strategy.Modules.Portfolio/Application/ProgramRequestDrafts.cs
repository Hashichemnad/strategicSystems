using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>
/// Draft Program Requests opened by other modules (CAP budget routing, ADR 0124): pre-filled values, requester record role, the caller's
/// transaction when given. The requester completes and submits the draft in the request wizard as any other draft.
/// </summary>
internal sealed class ProgramRequestDrafts(
    PortfolioDbContext db, INumberingService numbering, RequestEvaluator evaluator, IRecordRoleService recordRoles, IUserDirectory users, IClock clock)
    : IProgramRequestDrafts
{
    public async Task<ProgramRequestDraftRef> CreateDraftAsync(ProgramRequestDraft draft, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (transactionContext is not null)
        {
            await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        }

        var values = new RequestValues
        {
            NameAr = Cut(draft.NameAr, RequestRules.NameMaxLength),
            NameEn = Cut(draft.NameEn, RequestRules.NameMaxLength),
            Description = Cut(draft.Description, RequestRules.LongTextMaxLength),
            Justifications = Cut(draft.Justifications, RequestRules.LongTextMaxLength),
            SuggestionSourceCode = draft.SuggestionSourceCode,
            RequiresBudget = true,
            EstimatedBudget = draft.EstimatedBudget,
            ExpectedStartDate = draft.ExpectedStartDate,
            ExpectedEndDate = draft.ExpectedEndDate,
            UnitId = draft.UnitId,
            SectionUnitId = draft.SectionUnitId,
        };
        var number = await numbering.NextAsync(db, PortfolioPermissions.RequestNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
        var requesterUser = (await users.GetByEmployeesAsync([draft.RequesterEmployeeId], cancellationToken).ConfigureAwait(false)).FirstOrDefault(u => u.Active)?.Id;
        var request = ProgramRequest.Create(number, draft.RequesterEmployeeId, requesterUser, ProgramRequestOrigins.ActionPlan);
        var (classification, score, band) = await evaluator.ComputeAsync(values, cancellationToken).ConfigureAwait(false);
        var edited = request.Edit(values, classification, score, band);
        if (edited.IsFailure)
        {
            throw new InvalidOperationException(edited.Error.MessageKey);
        }

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.RequestResource, request.Id, RecordPrincipalKinds.Employee, draft.RequesterEmployeeId,
            PortfolioPermissions.RequesterRole, cancellationToken).ConfigureAwait(false);
        return new ProgramRequestDraftRef(request.Id, request.Number);
    }

    public async Task<ProgramRequestProgress?> GetProgressAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        var project = await db.Projects.AsNoTracking().Where(p => p.RequestId == requestId).Select(p => new { p.Id, p.Number, p.Stage, p.Performance })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new ProgramRequestProgress(request.Id, request.Number, request.Status, project?.Id, project?.Number, project?.Stage, project?.Performance);
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Where a request came from (<see cref="ProgramRequest.Source"/>).</summary>
public static class ProgramRequestOrigins
{
    public const string Manual = "manual";
    public const string Document = "document";
    public const string ActionPlan = "action-plan";
}
