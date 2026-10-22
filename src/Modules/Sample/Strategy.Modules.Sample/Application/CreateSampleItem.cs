using FluentValidation;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Application;

public sealed record CreateSampleItem(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn)
    : ICommand<SampleItemDto>, ISampleItemText;

public sealed class CreateSampleItemValidator : AbstractValidator<CreateSampleItem>
{
    public CreateSampleItemValidator() => this.AddTextRules();
}

internal sealed class CreateSampleItemHandler(SampleDbContext db) : ICommandHandler<CreateSampleItem, SampleItemDto>
{
    public async Task<Result<SampleItemDto>> HandleAsync(CreateSampleItem command, CancellationToken cancellationToken)
    {
        var item = SampleItem.Create(command.NameAr, command.NameEn, command.DescriptionAr, command.DescriptionEn);
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SampleItemDto.From(item);
    }
}
