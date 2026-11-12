using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

/// <summary>
/// One hook for every request approval process (built-in and custom, ADR 0081): stores the approve form given through the decide endpoint,
/// refuses completion while a required approve-form field is empty, moves the request and calls the type's handler — inside the engine's
/// transaction.
/// </summary>
internal sealed class RequestApprovalHook(
    RequestsDbContext db, RequestTypeCache types, RequestEngine engine, FormSecrets secrets, PendingDecision pending, Platform.Kernel.Time.IClock clock,
    IIntegrationEventPublisher events) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys => types.ProcessKeys;

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.ResourceType != Request.EntityType
            || approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned or ApprovalEventKinds.StepEntered))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type || type.Stage(request.Stage) is not { Kind: StageKinds.Approval } stage
            || stage.ProcessKey != approvalEvent.ProcessKey || request.Status != RequestStatuses.InReview)
        {
            return Result.Success();
        }

        var form = stage.ApproveForm ?? new FormSchema();
        if (pending.RequestId == request.Id && pending.Values is { } values && !form.IsEmpty)
        {
            request.SetProcessData(stage.Code, secrets.Stored(form, values));
            pending.RequestId = null;
        }

        var (fromStatus, fromStage) = (request.Status, request.Stage);
        switch (approvalEvent.Kind)
        {
            case ApprovalEventKinds.StepEntered:
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Success();

            case ApprovalEventKinds.Approved:
                {
                    var stored = request.ProcessData[stage.Code] as JsonObject ?? [];
                    var plain = secrets.Plain(form, stored);
                    if (form.AllFields.Any(f => f.Required && FormDataValidator.IsEmpty(plain[f.Code])))
                    {
                        // The generic approval screens cannot skip the approve form (classification, types…).
                        return Error.Rule("requests.approveForm.required", "requests.errors.approveFormRequired");
                    }

                    var next = RequestLifecycle.After(type.Stages, stage.Code);
                    var handled = await engine.NotifyHandlerAsync(request, type, TransitionKinds.Approved, stage.Code, next?.Code, null, plain, approvalEvent.Comment,
                        cancellationToken).ConfigureAwait(false);
                    if (handled.IsFailure)
                    {
                        return handled.Error;
                    }

                    var target = handled.Value.NextStage is { } code ? type.Stage(code) : next;
                    var entered = await engine.EnterAsync(request, type, target, null, cancellationToken).ConfigureAwait(false);
                    if (entered.IsFailure)
                    {
                        return entered.Error;
                    }

                    await engine.AddEventAsync(request, "approved", fromStatus, fromStage, approvalEvent.Comment, FormSecrets.Masked(form, stored), cancellationToken)
                        .ConfigureAwait(false);
                    if (request.Status != RequestStatuses.InReview)
                    {
                        // Final approval: no further approval stage is pending (Phase 22 webhook event, ADR 0194).
                        await events.PublishAsync(new RequestDecided(request.Id, request.Number, request.TypeCode, RequestDecisions.Approved, clock.UtcNow), db,
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;
                }

            case ApprovalEventKinds.Rejected:
            case ApprovalEventKinds.Returned:
                {
                    var rejected = approvalEvent.Kind == ApprovalEventKinds.Rejected;
                    request.MoveTo(rejected ? RequestStatuses.Rejected : RequestStatuses.Returned, stage.Code, clock.UtcNow);
                    var handled = await engine.NotifyHandlerAsync(request, type, rejected ? TransitionKinds.Rejected : TransitionKinds.Returned, stage.Code, null, null, null,
                        approvalEvent.Comment, cancellationToken).ConfigureAwait(false);
                    if (handled.IsFailure)
                    {
                        return handled.Error;
                    }

                    await engine.AddEventAsync(request, rejected ? "rejected" : "returned", fromStatus, fromStage, approvalEvent.Comment, null, cancellationToken)
                        .ConfigureAwait(false);
                    if (rejected)
                    {
                        await events.PublishAsync(new RequestDecided(request.Id, request.Number, request.TypeCode, RequestDecisions.Rejected, clock.UtcNow), db,
                            cancellationToken).ConfigureAwait(false);
                    }

                    // The approval engine tells the submitter (workflow.rejected / workflow.returned).
                    break;
                }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
