using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>My delegations: given and received.</summary>
public sealed record ListMyDelegations : IQuery<IReadOnlyList<DelegationDto>>;

internal sealed class ListMyDelegationsHandler(DelegationReader reader, IRequestContext context) : IQueryHandler<ListMyDelegations, IReadOnlyList<DelegationDto>>
{
    public async Task<Result<IReadOnlyList<DelegationDto>>> HandleAsync(ListMyDelegations query, CancellationToken cancellationToken)
    {
        if (context.User.EmployeeIdOf() is not { } me)
        {
            return Result.Success<IReadOnlyList<DelegationDto>>([]);
        }

        return Result.Success(await reader.ListAsync(d => d.DelegatorEmployeeId == me || d.DelegateEmployeeId == me, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrators: all delegations, optionally for one employee and / or one status.</summary>
public sealed record ListDelegations(Guid? EmployeeId, string? Status) : IQuery<IReadOnlyList<DelegationDto>>;

internal sealed class ListDelegationsHandler(DelegationReader reader) : IQueryHandler<ListDelegations, IReadOnlyList<DelegationDto>>
{
    public async Task<Result<IReadOnlyList<DelegationDto>>> HandleAsync(ListDelegations query, CancellationToken cancellationToken) =>
        Result.Success(await reader.ListAsync(
            d => (query.EmployeeId == null || d.DelegatorEmployeeId == query.EmployeeId || d.DelegateEmployeeId == query.EmployeeId)
                 && (query.Status == null || d.Status == query.Status),
            cancellationToken).ConfigureAwait(false));
}

public sealed record CreateDelegation(
    Guid? DelegatorEmployeeId,
    Guid? DelegateEmployeeId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<string>? Scope,
    string? Reason) : ICommand<DelegationDto>;

public sealed class CreateDelegationValidator : AbstractValidator<CreateDelegation>
{
    public CreateDelegationValidator()
    {
        RuleFor(x => x.DelegateEmployeeId).NotEmpty();
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate).NotEmpty();
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate).When(x => x.FromDate is not null && x.ToDate is not null)
            .WithErrorCode("validation.dateRange");
        RuleFor(x => x.Reason).MaximumLength(Delegation.ReasonMaxLength);
        RuleForEach(x => x.Scope).NotEmpty().MaximumLength(Delegation.ScopeItemMaxLength)
            .Matches("^[a-z][a-z0-9-]*(\\.[a-z0-9-]+)*$").WithErrorCode("validation.codeFormat");
    }
}

/// <summary>
/// Creates a delegation. Without <c>delegatorEmployeeId</c> (or with the caller's own) it is self-service;
/// for anyone else the caller needs <see cref="OrgPermissions.DelegationManage"/>.
/// </summary>
internal sealed class CreateDelegationHandler(OrgDbContext db, IAccessService access, IRequestContext context, IClock clock, DelegationReader reader)
    : ICommandHandler<CreateDelegation, DelegationDto>
{
    public async Task<Result<DelegationDto>> HandleAsync(CreateDelegation command, CancellationToken cancellationToken)
    {
        var me = context.User.EmployeeIdOf();
        var delegatorId = command.DelegatorEmployeeId ?? me;
        if (delegatorId is null)
        {
            return OrgErrors.NoEmployeeLinked;
        }

        if (delegatorId != me && !await access.CanAsync(context.User, OrgPermissions.DelegationManage, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var delegateId = command.DelegateEmployeeId!.Value;
        var people = await db.Employees.AsNoTracking().Where(e => e.Id == delegatorId || e.Id == delegateId)
            .ToDictionaryAsync(e => e.Id, cancellationToken).ConfigureAwait(false);
        if (!people.ContainsKey(delegatorId.Value) || !people.TryGetValue(delegateId, out var delegatePerson))
        {
            return OrgErrors.EmployeeNotFound;
        }

        var existing = await db.Delegations
            .Where(d => d.Status == DelegationStatus.Active
                && (d.DelegatorEmployeeId == delegatorId || (d.DelegatorEmployeeId == delegateId && d.DelegateEmployeeId == delegatorId)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var created = Delegation.Create(
            delegatorId.Value, delegateId, delegatePerson.Status == EmployeeStatus.Active, command.FromDate!.Value, command.ToDate!.Value,
            command.Scope ?? [], command.Reason, clock.Today, existing);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Delegations.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await reader.ListAsync(d => d.Id == created.Value.Id, cancellationToken).ConfigureAwait(false))[0];
    }
}

public sealed record CancelDelegation(Guid Id, string? Reason, uint Version) : ICommand<DelegationDto>;

public sealed class CancelDelegationValidator : AbstractValidator<CancelDelegation>
{
    public CancelDelegationValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(Delegation.ReasonMaxLength);
}

/// <summary>The delegator cancels their own delegation; administrators cancel anyone's. Others get 404.</summary>
internal sealed class CancelDelegationHandler(OrgDbContext db, IAccessService access, IRequestContext context, DelegationReader reader)
    : ICommandHandler<CancelDelegation, DelegationDto>
{
    public async Task<Result<DelegationDto>> HandleAsync(CancelDelegation command, CancellationToken cancellationToken)
    {
        var delegation = await db.Delegations.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken).ConfigureAwait(false);
        var mine = delegation is not null && delegation.DelegatorEmployeeId == context.User.EmployeeIdOf();
        if (delegation is null
            || (!mine && !await access.CanAsync(context.User, OrgPermissions.DelegationManage,
                new ResourceRef(OrgPermissions.DelegationResource, command.Id), cancellationToken).ConfigureAwait(false)))
        {
            return Error.NotFound();
        }

        db.Entry(delegation).Property(x => x.Version).OriginalValue = command.Version;
        var outcome = delegation.Cancel(command.Reason!);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return (await reader.ListAsync(d => d.Id == command.Id, cancellationToken).ConfigureAwait(false))[0];
    }
}

internal sealed class DelegationReader(OrgDbContext db, OrgReader orgReader)
{
    public async Task<IReadOnlyList<DelegationDto>> ListAsync(
        System.Linq.Expressions.Expression<Func<Delegation, bool>> filter, CancellationToken cancellationToken)
    {
        var delegations = await db.Delegations.AsNoTracking().Where(filter)
            .OrderByDescending(d => d.FromDate).ThenByDescending(d => d.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await orgReader.EmployeeRefsAsync(
            delegations.SelectMany(d => new Guid?[] { d.DelegatorEmployeeId, d.DelegateEmployeeId }), cancellationToken).ConfigureAwait(false);
        return delegations.Select(d => new DelegationDto(
            d.Id, people[d.DelegatorEmployeeId], people[d.DelegateEmployeeId], d.FromDate, d.ToDate, d.Scope, d.Reason, d.Status,
            d.CancelReason, d.CreatedAt, d.Version)).ToList();
    }
}
