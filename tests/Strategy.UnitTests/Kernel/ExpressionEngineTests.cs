using Strategy.Platform.Kernel.Expressions;

namespace Strategy.UnitTests.Kernel;

public sealed class ExpressionEngineTests
{
    private static readonly ExpressionVariable[] Variables =
    [
        new("amount", ExpressionValueType.Number),
        new("category", ExpressionValueType.Text),
        new("urgent", ExpressionValueType.Boolean),
        new("dueDate", ExpressionValueType.Date),
        new("sectionManager", ExpressionValueType.Text),
        new("departmentDirector", ExpressionValueType.Text),
        new("submitter", ExpressionValueType.Text),
    ];

    private static object? Eval(string text, params (string Name, object? Value)[] values)
    {
        var compiled = ExpressionEngine.Compile(text, Variables);
        compiled.Success.ShouldBeTrue(string.Join(", ", compiled.Errors.Select(e => e.Code)));
        var result = compiled.Expression!.Evaluate(values.ToDictionary(v => v.Name, v => v.Value), new DateOnly(2026, 11, 2));
        result.Success.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    public static TheoryData<string, object?> Cases => new()
    {
        { "1 + 2 * 3", 7m },
        { "(1 + 2) * 3", 9m },
        { "10 / 4", 2.5m },
        { "10 % 4", 2m },
        { "-amount + 1", -99m },
        { "amount > 50000", false },
        { "amount >= 100", true },
        { "amount = 100", true },
        { "amount <> 100", false },
        { "category == 'IT'", true },  // text comparison ignores case
        { "category in ['hr', 'it']", true },
        { "category not in ('hr', 'it')", false },
        { "urgent and amount > 10", true },
        { "urgent && amount > 1000", false },
        { "not urgent or amount < 1000", true },
        { "!urgent", false },
        { "if(amount > 50, 'big', 'small')", "big" },
        { "min(amount, 20, 300)", 20m },
        { "max(amount, 20, 300)", 300m },
        { "abs(-3.5)", 3.5m },
        { "round(2.345, 2)", 2.35m },
        { "round(2.5)", 3m },
        { "coalesce(sectionManager, 'none')", "none" },
        { "isEmpty(sectionManager)", true },
        { "contains(category, 'T')", true },
        { "startsWith('Finance', 'fin')", true },
        { "lower('ABC') + upper('d')", "abcD" },
        { "len('عربي')", 4m },
        { "daysBetween(date('2026-01-01'), dueDate)", 31m },
        { "dueDate < today()", true },
        { "'a' + 1", "a1" },
        { "sectionManager == null", true },
        { "sectionManager != departmentDirector", true },
        { "amount + sectionManager", null }, // null propagates
        { "sectionManager > 'a'", false },   // ordering with null is false
        { "AMOUNT > 1", true },              // variable names ignore case
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluates(string text, object? expected) =>
        Eval(text, ("amount", 100), ("category", "it"), ("urgent", true), ("dueDate", new DateOnly(2026, 2, 1)), ("sectionManager", null),
            ("departmentDirector", "d-1")).ShouldBe(expected);

    [Fact]
    public void Q_P4_section_manager_condition()
    {
        const string condition = "sectionManager != null and sectionManager != departmentDirector and sectionManager != submitter";
        Eval(condition, ("sectionManager", "s"), ("departmentDirector", "d"), ("submitter", "x")).ShouldBe(true);
        Eval(condition, ("sectionManager", null), ("departmentDirector", "d"), ("submitter", "x")).ShouldBe(false);
        Eval(condition, ("sectionManager", "d"), ("departmentDirector", "d"), ("submitter", "x")).ShouldBe(false);
        Eval(condition, ("sectionManager", "x"), ("departmentDirector", "d"), ("submitter", "x")).ShouldBe(false);
    }

    [Theory]
    [InlineData("amount >", "expressions.unexpectedEnd", 8)]
    [InlineData("amount > > 1", "expressions.syntax", 9)]
    [InlineData("salary > 1", "expressions.unknownVariable", 0)]
    [InlineData("exec('x')", "expressions.unknownFunction", 0)]
    [InlineData("abs(1, 2)", "expressions.argumentCount", 0)]
    [InlineData("amount > 'x'", "expressions.typeMismatch", 7)]
    [InlineData("urgent + 1", "expressions.typeMismatch", 7)]
    [InlineData("category and urgent", "expressions.typeMismatch", 9)]
    [InlineData("'open", "expressions.unterminatedText", 0)]
    [InlineData("amount # 2", "expressions.unexpectedCharacter", 7)]
    [InlineData("1.2.3", "expressions.invalidNumber", 0)]
    [InlineData("  ", "expressions.empty", 0)]
    public void Refuses_invalid_expressions_with_a_position(string text, string code, int position)
    {
        var result = ExpressionEngine.Compile(text, Variables);
        result.Success.ShouldBeFalse();
        result.Errors[0].Code.ShouldBe(code);
        result.Errors[0].MessageKey.ShouldStartWith("expressions.errors.");
        result.Errors[0].Position.ShouldBe(position);
    }

    [Fact]
    public void A_condition_must_be_yes_or_no()
    {
        var result = ExpressionEngine.Compile("amount + 1", Variables, ExpressionValueType.Boolean);
        result.Errors.Single().Code.ShouldBe("expressions.notBoolean");
        ExpressionEngine.Compile("amount > 1", Variables, ExpressionValueType.Boolean).Success.ShouldBeTrue();
    }

    [Fact]
    public void Reports_the_variables_it_reads()
    {
        var compiled = ExpressionEngine.Compile("amount > 1 and category == 'x' and amount < 9", Variables).Expression!;
        compiled.Variables.Order(StringComparer.Ordinal).ShouldBe(["amount", "category"]);
        compiled.ResultType.ShouldBe(ExpressionValueType.Boolean);
    }

    [Fact]
    public void Runtime_errors_are_reported_not_thrown()
    {
        var compiled = ExpressionEngine.Compile("10 / amount", Variables).Expression!;
        var result = compiled.Evaluate(new Dictionary<string, object?> { ["amount"] = 0 });
        result.Success.ShouldBeFalse();
        result.Error!.Code.ShouldBe("expressions.divisionByZero");
        ExpressionEngine.Compile("date('31/01/2026') < today()", Variables).Expression!
            .Evaluate(new Dictionary<string, object?>()).Error!.Code.ShouldBe("expressions.invalidDate");
    }

    [Fact]
    public void Limits_length_size_and_depth()
    {
        ExpressionEngine.Compile(new string('1', ExpressionLimits.MaxLength + 1), Variables).Errors[0].Code.ShouldBe("expressions.tooLong");
        var wide = string.Join(" + ", Enumerable.Repeat("1", 200));
        ExpressionEngine.Compile(wide, Variables).Errors[0].Code.ShouldBe("expressions.tooComplex");
        var deep = new string('(', 60) + "1" + new string(')', 60);
        ExpressionEngine.Compile(deep, Variables).Errors[0].Code.ShouldBe("expressions.tooComplex");
        var nots = string.Concat(Enumerable.Repeat("not ", 60)) + "true";
        ExpressionEngine.Compile(nots, Variables).Errors[0].Code.ShouldBe("expressions.tooComplex");
    }

    [Fact]
    public void Normalises_input_values()
    {
        ExpressionEngine.Normalise(5).ShouldBe(5m);
        ExpressionEngine.Normalise(2.5d).ShouldBe(2.5m);
        var id = Guid.CreateVersion7();
        ExpressionEngine.Normalise(id).ShouldBe(id.ToString("D"));
        ExpressionEngine.Normalise(new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc)).ShouldBe(new DateOnly(2026, 1, 2));
    }
}
