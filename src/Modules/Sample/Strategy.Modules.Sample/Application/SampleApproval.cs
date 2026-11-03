using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Sample.Application;

/// <summary>
/// The worked example of the approval engine (Phase 4): the process, its default chain (Q-P4 conditional section manager), submission,
/// the hook that moves the item's approval status, the current-status provider and the My Work card. docs/design/sample.md.
/// </summary>
public static class SampleApprovalProcess
{
    public const string Key = "sample.item-approval";
    public const string AmountVariable = "amount";

    /// <summary>Q-P4 (SPEC Appendix A): the section manager step only when the section has a manager distinct from the director and the submitter.</summary>
    public const string SectionManagerCondition =
        "sectionManager != null and sectionManager != departmentDirector and sectionManager != submitter";

    public static readonly ApprovalProcessDefinition Definition = new(
        Key, "sample", "اعتماد البند التجريبي", "Sample item approval", SamplePermissions.ResourceType, SamplePermissions.Read, "/sample/items/{id}",
        [new(AmountVariable, ExpressionValueType.Number, "المبلغ", "Amount")],
        [
            new("section-manager", "رئيس القسم", "Section manager", new ApproverRule(ApproverRuleKinds.SectionManager, Of: ApproverRuleSubjects.Record),
                SectionManagerCondition, SlaHours: 16),
            new("department-director", "مدير الإدارة", "Department director",
                new ApproverRule(ApproverRuleKinds.DepartmentDirector, Of: ApproverRuleSubjects.Record), SlaHours: 16),
            new("item-approver", "معتمد البنود", "Item approver", new ApproverRule(ApproverRuleKinds.Role, RoleKey: "sample.approver"),
                "amount > 100000", SlaHours: 40),
        ],
        SamplePermissions.ApproverRole,
        "مسار تجريبي يوضح خطوات الموافقة الشرطية والإعادة للتصحيح.",
        "Demo chain showing conditional steps, return for correction and resubmission.");
}

public sealed record SubmitSampleItem(Guid Id, uint Version, string? Comment) : ICommand<SampleItemDto>;

public sealed class SubmitSampleItemValidator : AbstractValidator<SubmitSampleItem>
{
    public SubmitSampleItemValidator() => RuleFor(x => x.Comment).MaximumLength(4000);
}

/// <summary>Draft / returned → pending and start the approval, in one transaction (ADR 0023). 404 outside the read scope, 403 without submit.</summary>
internal sealed class SubmitSampleItemHandler(SampleDbContext db, IAccessService access, IRequestContext context, IApprovalEngine engine)
    : ICommandHandler<SubmitSampleItem, SampleItemDto>
{
    public async Task<Result<SampleItemDto>> HandleAsync(SubmitSampleItem command, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null
            || !await access.CanAsync(context.User, SamplePermissions.Read, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!await access.CanAsync(context.User, SamplePermissions.Submit, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(item).Property(x => x.Version).OriginalValue = command.Version;
        var submitted = item.SubmitForApproval();
        if (submitted.IsFailure)
        {
            return submitted.Error;
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

        var started = await engine.SubmitAsync(
            new ApprovalSubmission(
                SampleApprovalProcess.Key,
                new ApprovalSubject(SamplePermissions.ResourceType, item.Id, item.Number, item.NameAr, item.NameEn, item.OwnerEmployeeId, item.CreatedBy, item.UnitId),
                new Dictionary<string, object?> { [SampleApprovalProcess.AmountVariable] = item.Amount },
                command.Comment),
            db,
            cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error; // the transaction is disposed without commit: the item stays as it was
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(item).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return SampleItemDto.From(item);
    }
}

/// <summary>Moves the item's approval status with the approval (inside the engine's transaction).</summary>
internal sealed class SampleApprovalHook(SampleDbContext db) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [SampleApprovalProcess.Key];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        var status = approvalEvent.Kind switch
        {
            ApprovalEventKinds.Approved => SampleApprovalStatus.Approved,
            ApprovalEventKinds.Rejected => SampleApprovalStatus.Rejected,
            ApprovalEventKinds.Returned => SampleApprovalStatus.Returned,
            ApprovalEventKinds.Cancelled => SampleApprovalStatus.Draft,
            _ => null,
        };
        if (status is null)
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var item = await db.Items.FirstOrDefaultAsync(x => x.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        var applied = item.ApplyApprovalOutcome(status);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>"Next action required + who" for a sample item (SPEC §0.2 current-status engine).</summary>
internal sealed class SampleCurrentStatus(
    SampleDbContext db, IAccessService access, IRequestContext context, IApprovalEngine engine, Platform.Organisation.Contracts.IEmployeeDirectory employees)
    : ICurrentStatusProvider
{
    public string ResourceType => SamplePermissions.ResourceType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == resourceId, cancellationToken).ConfigureAwait(false);
        if (item is null
            || !await access.CanAsync(context.User, SamplePermissions.Read, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var owner = item.OwnerEmployeeId is { } ownerId ? await employees.GetAsync(ownerId, cancellationToken).ConfigureAwait(false) : null;
        PersonName[] owners = owner is null ? [] : [new PersonName(null, owner.NameAr ?? owner.NameEn, owner.NameEn ?? owner.NameAr)];
        var empty = new Dictionary<string, BilingualText>();
        if (item.Status == SampleItemStatus.Inactive)
        {
            return new CurrentStatus("sample.currentStatus.inactive", empty, "neutral", null, [], [], item.ApprovalStatus);
        }

        switch (item.ApprovalStatus)
        {
            case SampleApprovalStatus.Pending:
                {
                    var view = await engine.GetStatusViewAsync(SampleApprovalProcess.Key, item.Id, context.User, cancellationToken).ConfigureAwait(false);
                    var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? view?.Instance.CurrentStepNameEn, view?.Instance.CurrentStepNameEn ?? view?.Instance.CurrentStepNameAr);
                    var actions = view?.MyTaskId is { } taskId
                        ? view.MyActions.Select(a => new CurrentStatusAction(a, $"workflow.actions.{a}", a == ApprovalActions.Approve ? "primary" : a == ApprovalActions.Reject ? "danger" : "secondary", taskId))
                            .ToList()
                        : [];
                    return new CurrentStatus("workflow.currentStatus.waitingStep", new Dictionary<string, BilingualText> { ["step"] = step }, "info",
                        "workflow.currentStatus.approvers", view?.Approvers ?? [], actions, item.ApprovalStatus);
                }

            case SampleApprovalStatus.Approved:
                return new CurrentStatus("sample.currentStatus.approved", empty, "success", null, [], [], item.ApprovalStatus);
            case SampleApprovalStatus.Rejected:
                return new CurrentStatus("sample.currentStatus.rejected", empty, "danger", null, [], [], item.ApprovalStatus);
        }

        var canSubmit = await access.CanAsync(context.User, SamplePermissions.Submit, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false);
        var returned = item.ApprovalStatus == SampleApprovalStatus.Returned;
        return new CurrentStatus(
            returned ? "sample.currentStatus.returned" : "sample.currentStatus.draft", empty, returned ? "warning" : "info", "sample.currentStatus.owner", owners,
            canSubmit ? [new CurrentStatusAction(returned ? "resubmit" : "submit", returned ? "sample.actions.resubmit" : "sample.actions.submit")] : [],
            item.ApprovalStatus);
    }
}

/// <summary>My Work card: my items returned for correction (they need me to correct and resubmit).</summary>
internal sealed class SampleMyWork(SampleDbContext db, IAccessService access) : IMyWorkSource
{
    public string SystemCode => "sample";

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.UserId() is not { } userId)
        {
            return [];
        }

        var employeeId = user.EmployeeIdOf();
        var query = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Submit, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        var items = await query
            .Where(x => x.Status == SampleItemStatus.Active && x.ApprovalStatus == SampleApprovalStatus.Returned
                && (x.OwnerEmployeeId == employeeId || x.CreatedBy == userId))
            .OrderBy(x => x.UpdatedAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(x => new MyWorkItem("sample", SamplePermissions.ResourceType, x.Id, x.Number, x.NameAr, x.NameEn, "sample.myWork.returned",
            $"/sample/items/{x.Id:D}", x.UpdatedAt)).ToList();
    }
}
