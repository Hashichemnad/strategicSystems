using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Application;

public sealed record UpdateSampleItem(
    Guid Id,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    uint Version) : ICommand<SampleItemDto>, ISampleItemText;

public sealed class UpdateSampleItemValidator : AbstractValidator<UpdateSampleItem>
{
    public UpdateSampleItemValidator() => this.AddTextRules();
}

internal sealed class UpdateSampleItemHandler(SampleDbContext db, IAccessService access, IRequestContext context)
    : ICommandHandler<UpdateSampleItem, SampleItemDto>
{
    public Task<Result<SampleItemDto>> HandleAsync(UpdateSampleItem command, CancellationToken cancellationToken) =>
        SampleItemChange.ApplyAsync(
            db,
            access,
            context,
            command.Id,
            command.Version,
            item => item.Update(command.NameAr, command.NameEn, command.DescriptionAr, command.DescriptionEn),
            cancellationToken);
}

public sealed record ActivateSampleItem(Guid Id, uint Version) : ICommand<SampleItemDto>;

public sealed record DeactivateSampleItem(Guid Id, uint Version) : ICommand<SampleItemDto>;

internal sealed class ActivateSampleItemHandler(SampleDbContext db, IAccessService access, IRequestContext context)
    : ICommandHandler<ActivateSampleItem, SampleItemDto>
{
    public Task<Result<SampleItemDto>> HandleAsync(ActivateSampleItem command, CancellationToken cancellationToken) =>
        SampleItemChange.ApplyAsync(db, access, context, command.Id, command.Version, item => item.Activate(), cancellationToken);
}

internal sealed class DeactivateSampleItemHandler(SampleDbContext db, IAccessService access, IRequestContext context)
    : ICommandHandler<DeactivateSampleItem, SampleItemDto>
{
    public Task<Result<SampleItemDto>> HandleAsync(DeactivateSampleItem command, CancellationToken cancellationToken) =>
        SampleItemChange.ApplyAsync(db, access, context, command.Id, command.Version, item => item.Deactivate(), cancellationToken);
}

/// <summary>Load → record access check (404 outside scope) → domain change → save with the client's row version (409 if stale).</summary>
internal static class SampleItemChange
{
    public static async Task<Result<SampleItemDto>> ApplyAsync(
        SampleDbContext db,
        IAccessService access,
        IRequestContext context,
        Guid id,
        uint expectedVersion,
        Func<SampleItem, Result> change,
        CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

        // Outside the read scope: 404 (existence does not leak). Readable but not editable: 403 (CLAUDE.md §7).
        if (item is null
            || !await access.CanAsync(context.User, SamplePermissions.Read, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!await access.CanAsync(context.User, SamplePermissions.Update, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(item).Property(x => x.Version).OriginalValue = expectedVersion;
        var outcome = change(item);
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

        return SampleItemDto.From(item);
    }
}
