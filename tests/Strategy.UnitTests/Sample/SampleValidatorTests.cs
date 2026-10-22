using Strategy.Modules.Sample.Application;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.UnitTests.Sample;

public class SampleValidatorTests
{
    [Theory]
    [InlineData("اسم", null, true)]
    [InlineData(null, "Name", true)]
    [InlineData(null, null, false)]
    [InlineData("  ", "", false)]
    public void At_least_one_name_is_required(string? nameAr, string? nameEn, bool valid)
    {
        var result = new CreateSampleItemValidator().Validate(new CreateSampleItem(nameAr, nameEn, null, null));

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.ShouldContain(e => e.ErrorCode == "validation.atLeastOneLanguage");
        }
    }

    [Fact]
    public void Names_are_limited_to_200_characters()
    {
        var result = new UpdateSampleItemValidator().Validate(
            new UpdateSampleItem(Guid.NewGuid(), new string('ع', 201), "ok", null, null, 1));

        result.Errors.ShouldContain(e => e.PropertyName == "NameAr" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Theory]
    [InlineData("-createdAt", true)]
    [InlineData("nameEn", true)]
    [InlineData("password", false)]
    public void List_sort_field_must_be_whitelisted(string sort, bool valid) =>
        Validate(new ListSampleItems(new PageRequest(Sort: sort), null, null)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(0, 25, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 201, false)]
    [InlineData(1, 200, true)]
    public void List_paging_bounds(int page, int pageSize, bool valid) =>
        Validate(new ListSampleItems(new PageRequest(page, pageSize), null, null)).IsValid.ShouldBe(valid);

    [Fact]
    public void List_status_must_be_a_known_code() =>
        Validate(new ListSampleItems(new PageRequest(), null, "deleted")).Errors
            .ShouldContain(e => e.ErrorCode == "validation.unknownCode");

    private static FluentValidation.Results.ValidationResult Validate(ListSampleItems query) =>
        new ListSampleItemsValidator().Validate(query);
}
