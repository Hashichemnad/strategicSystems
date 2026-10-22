using FluentValidation.Results;
using Strategy.Platform.Kernel.Validation;

namespace Strategy.UnitTests.Kernel;

public class ValidationErrorMapperTests
{
    [Theory]
    [InlineData("NotEmptyValidator", "validation.notEmpty")]
    [InlineData("MaximumLengthValidator", "validation.maximumLength")]
    [InlineData("validation.atLeastOneLanguage", "validation.atLeastOneLanguage")]
    public void Maps_error_codes_to_message_keys(string errorCode, string expectedKey)
    {
        var failure = new ValidationFailure("NameAr", "ignored") { ErrorCode = errorCode };

        ValidationErrorMapper.ToFieldError(failure).MessageKey.ShouldBe(expectedKey);
    }

    [Fact]
    public void Keeps_placeholders_as_params_but_drops_internal_ones()
    {
        var failure = new ValidationFailure("NameEn", "ignored")
        {
            ErrorCode = "MaximumLengthValidator",
            FormattedMessagePlaceholderValues = new Dictionary<string, object>
            {
                ["PropertyName"] = "Name En",
                ["PropertyValue"] = "secret value",
                ["MaxLength"] = 200,
                ["TotalLength"] = 250,
            },
        };

        var error = ValidationErrorMapper.ToFieldError(failure);

        error.Params!.Keys.ShouldBe(["maxLength", "totalLength"], ignoreOrder: true);
        error.Params["maxLength"].ShouldBe(200);
    }

    [Theory]
    [InlineData("NameAr", "nameAr")]
    [InlineData("Items[0].NameEn", "items[0].nameEn")]
    [InlineData("page", "page")]
    public void Field_paths_match_json_names(string path, string expected) =>
        ValidationErrorMapper.ToFieldPath(path).ShouldBe(expected);
}
