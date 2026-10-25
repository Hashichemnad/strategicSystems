using Strategy.Platform.Organisation.Domain;

namespace Strategy.UnitTests.Organisation;

public class DelegationTests
{
    private static readonly DateOnly Today = new(2025, 5, 1);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static Strategy.Platform.Kernel.Results.Result<Delegation> Create(
        Guid from, Guid to, DateOnly start, DateOnly end, string[]? scope = null, bool delegateActive = true, params Delegation[] existing) =>
        Delegation.Create(from, to, delegateActive, start, end, scope ?? [], "Leave", Today, existing);

    [Fact]
    public void Valid_delegation_is_created_with_a_sorted_distinct_scope()
    {
        var result = Create(A, B, Today, Today.AddDays(10), ["portfolio", " kpi ", "portfolio"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Scope.ShouldBe(["kpi", "portfolio"]);
        result.Value.Status.ShouldBe(DelegationStatus.Active);
    }

    [Fact]
    public void Self_delegation_is_refused() => Create(A, A, Today, Today).Error!.Code.ShouldBe("org.delegation.self");

    [Fact]
    public void Inactive_delegate_is_refused() =>
        Create(A, B, Today, Today, delegateActive: false).Error!.Code.ShouldBe("org.delegation.delegateInactive");

    [Fact]
    public void Range_in_the_past_is_refused() => Create(A, B, Today.AddDays(-5), Today.AddDays(-1)).Error!.Code.ShouldBe("org.delegation.inPast");

    [Fact]
    public void Overlapping_delegation_with_overlapping_scope_is_refused()
    {
        var existing = Create(A, B, Today, Today.AddDays(10), ["portfolio"]).Value;

        Create(A, C, Today.AddDays(5), Today.AddDays(20), ["portfolio", "kpi"], existing: existing).Error!.Code.ShouldBe("org.delegation.overlap");
        Create(A, C, Today.AddDays(5), Today.AddDays(20), [], existing: existing).Error!.Code.ShouldBe("org.delegation.overlap");
        Create(A, C, Today.AddDays(5), Today.AddDays(20), ["kpi"], existing: existing).IsSuccess.ShouldBeTrue();
        Create(A, C, Today.AddDays(11), Today.AddDays(20), ["portfolio"], existing: existing).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Reverse_delegation_in_the_same_period_is_refused()
    {
        var existing = Create(A, B, Today, Today.AddDays(10)).Value;

        Create(B, A, Today.AddDays(3), Today.AddDays(4), existing: existing).Error!.Code.ShouldBe("org.delegation.cycle");
    }

    [Fact]
    public void Cancelled_delegations_do_not_block_new_ones()
    {
        var existing = Create(A, B, Today, Today.AddDays(10)).Value;
        existing.Cancel("Plans changed").IsSuccess.ShouldBeTrue();

        Create(A, C, Today, Today.AddDays(10), existing: existing).IsSuccess.ShouldBeTrue();
        existing.Cancel("again").Error!.Code.ShouldBe("org.delegation.notActive");
    }

    [Theory]
    [InlineData("2025-05-01", null, true)]
    [InlineData("2025-05-11", null, false)]
    [InlineData("2025-05-05", "portfolio.request", true)]
    [InlineData("2025-05-05", "portfolio", true)]
    [InlineData("2025-05-05", "kpi.result", false)]
    [InlineData("2025-05-05", "portfolio-x.request", false)]
    public void Covers_checks_dates_and_scope(string date, string? scopeKey, bool expected)
    {
        var delegation = Create(A, B, Today, Today.AddDays(9), ["portfolio"]).Value;

        delegation.Covers(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), scopeKey).ShouldBe(expected);
    }
}
