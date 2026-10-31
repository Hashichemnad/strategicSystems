using FluentValidation;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Sample.Application;

public sealed record CreateSampleItem(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn)
    : ICommand<SampleItemDto>, ISampleItemText;

public sealed class CreateSampleItemValidator : AbstractValidator<CreateSampleItem>
{
    public CreateSampleItemValidator() => this.AddTextRules();
}

/// <summary>
/// The creator's employee owns the item and its unit is the item's unit (data scopes, ADR 0015). The number comes from the
/// Numbering service inside the same transaction as the insert (CLAUDE.md §6, ADR 0017).
/// </summary>
internal sealed class CreateSampleItemHandler(
    SampleDbContext db, INumberingService numbering, IEmployeeDirectory employees, IRequestContext context, IClock clock)
    : ICommandHandler<CreateSampleItem, SampleItemDto>
{
    public async Task<Result<SampleItemDto>> HandleAsync(CreateSampleItem command, CancellationToken cancellationToken)
    {
        var owner = context.User.EmployeeIdOf() is { } employeeId
            ? await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false)
            : null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, SamplePermissions.NumberingFormat, new NumberingContext(clock.Today), cancellationToken)
            .ConfigureAwait(false);
        var item = SampleItem.Create(command.NameAr, command.NameEn, command.DescriptionAr, command.DescriptionEn, number, owner?.Id, owner?.UnitId);
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return SampleItemDto.From(item);
    }
}
