using Strategy.Platform.Kernel.Results;

namespace Strategy.UnitTests.Kernel;

public class ResultTests
{
    [Fact]
    public void Success_carries_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_carries_error_and_refuses_value()
    {
        Result<int> result = Error.NotFound();

        result.IsFailure.ShouldBeTrue();
        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Validation_error_lists_fields_with_message_keys()
    {
        var error = Error.Validation([new FieldError("nameAr", "NotEmptyValidator", "validation.notEmpty")]);

        error.Code.ShouldBe("validation.failed");
        error.MessageKey.ShouldBe("errors.validationFailed");
        error.Fields!.Single().MessageKey.ShouldBe("validation.notEmpty");
    }
}
