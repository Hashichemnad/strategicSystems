using System.Reflection;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;

namespace Strategy.UnitTests.Portfolio;

/// <summary>Wasel and Portfolio completion rules (docs/design/portfolio.md §51–§59): transition tables, diff, rating, flags, capacity, reviews, DTO shapes.</summary>
public sealed class WaselRulesTests
{
    public static TheoryData<string, string, string?> ProposalTable() => new()
    {
        { ProposalStatuses.Draft, ProposalActions.Edit, ProposalStatuses.Draft },
        { ProposalStatuses.Draft, ProposalActions.Submit, ProposalStatuses.Submitted },
        { ProposalStatuses.Draft, ProposalActions.Withdraw, ProposalStatuses.Withdrawn },
        { ProposalStatuses.Draft, ProposalActions.Accept, null },
        { ProposalStatuses.Draft, ProposalActions.Return, null },
        { ProposalStatuses.Submitted, ProposalActions.Accept, ProposalStatuses.Accepted },
        { ProposalStatuses.Submitted, ProposalActions.Return, ProposalStatuses.Returned },
        { ProposalStatuses.Submitted, ProposalActions.Edit, null },
        { ProposalStatuses.Submitted, ProposalActions.Submit, null },
        { ProposalStatuses.Submitted, ProposalActions.Withdraw, null },
        { ProposalStatuses.Returned, ProposalActions.Edit, ProposalStatuses.Draft },
        { ProposalStatuses.Returned, ProposalActions.Withdraw, ProposalStatuses.Withdrawn },
        { ProposalStatuses.Returned, ProposalActions.Submit, null },
        { ProposalStatuses.Returned, ProposalActions.Accept, null },
        { ProposalStatuses.Accepted, ProposalActions.Edit, null },
        { ProposalStatuses.Accepted, ProposalActions.Submit, null },
        { ProposalStatuses.Accepted, ProposalActions.Accept, null },
        { ProposalStatuses.Accepted, ProposalActions.Return, null },
        { ProposalStatuses.Accepted, ProposalActions.Withdraw, null },
        { ProposalStatuses.Withdrawn, ProposalActions.Edit, null },
        { ProposalStatuses.Withdrawn, ProposalActions.Submit, null },
    };

    [Theory]
    [MemberData(nameof(ProposalTable))]
    public void Plan_proposal_transitions(string from, string action, string? to)
    {
        var next = PlanProposalLifecycle.Next(from, action);
        if (to is null)
        {
            next.IsFailure.ShouldBeTrue();
            next.Error!.MessageKey.ShouldBe("portfolio.errors.waselTransition");
        }
        else
        {
            next.Value.ShouldBe(to);
        }
    }

    [Fact]
    public void Every_proposal_pair_is_covered_by_the_table()
    {
        var statuses = new[] { ProposalStatuses.Draft, ProposalStatuses.Submitted, ProposalStatuses.Returned, ProposalStatuses.Accepted, ProposalStatuses.Withdrawn };
        var allowed = statuses.SelectMany(s => ProposalActions.All.Select(a => (s, a))).Count(p => PlanProposalLifecycle.Next(p.s, p.a).IsSuccess);
        allowed.ShouldBe(7);
    }

    public static TheoryData<string, string, string?> SubmissionTable() => new()
    {
        { SubmissionStatuses.Draft, SubmissionActions.Edit, SubmissionStatuses.Draft },
        { SubmissionStatuses.Draft, SubmissionActions.Submit, SubmissionStatuses.Submitted },
        { SubmissionStatuses.Draft, SubmissionActions.AllAccepted, null },
        { SubmissionStatuses.Draft, SubmissionActions.Include, null },
        { SubmissionStatuses.Returned, SubmissionActions.Edit, SubmissionStatuses.Returned },
        { SubmissionStatuses.Returned, SubmissionActions.Submit, SubmissionStatuses.Submitted },
        { SubmissionStatuses.Returned, SubmissionActions.Include, null },
        { SubmissionStatuses.Submitted, SubmissionActions.Review, SubmissionStatuses.Submitted },
        { SubmissionStatuses.Submitted, SubmissionActions.AllAccepted, SubmissionStatuses.Accepted },
        { SubmissionStatuses.Submitted, SubmissionActions.SomeReturned, SubmissionStatuses.Returned },
        { SubmissionStatuses.Submitted, SubmissionActions.Edit, null },
        { SubmissionStatuses.Submitted, SubmissionActions.Submit, null },
        { SubmissionStatuses.Submitted, SubmissionActions.Include, null },
        { SubmissionStatuses.Accepted, SubmissionActions.Include, SubmissionStatuses.Included },
        { SubmissionStatuses.Accepted, SubmissionActions.Edit, null },
        { SubmissionStatuses.Accepted, SubmissionActions.Submit, null },
        { SubmissionStatuses.Included, SubmissionActions.Edit, null },
        { SubmissionStatuses.Included, SubmissionActions.Include, null },
    };

    [Theory]
    [MemberData(nameof(SubmissionTable))]
    public void Vendor_submission_transitions(string from, string action, string? to)
    {
        var next = VendorSubmissionLifecycle.Next(from, action);
        if (to is null)
        {
            next.IsFailure.ShouldBeTrue();
        }
        else
        {
            next.Value.ShouldBe(to);
        }
    }

    [Fact]
    public void Every_submission_pair_is_covered_by_the_table()
    {
        var statuses = new[] { SubmissionStatuses.Draft, SubmissionStatuses.Submitted, SubmissionStatuses.Returned, SubmissionStatuses.Accepted, SubmissionStatuses.Included };
        statuses.SelectMany(s => SubmissionActions.All.Select(a => (s, a))).Count(p => VendorSubmissionLifecycle.Next(p.s, p.a).IsSuccess).ShouldBe(8);
    }

    [Fact]
    public void Diff_lists_only_the_fields_the_pm_changed()
    {
        var day = new DateOnly(2026, 11, 1);
        var vendor = new VendorProgressValues(0.3m, day, null, "note", null, null);
        VendorDiff.Of(vendor, vendor).ShouldBeEmpty();
        var pm = vendor with { Achieve = 0.25m, ActualStart = day.AddDays(-1), Causality = "  late supplier " };
        VendorDiff.Of(vendor, pm).ShouldBe(
        [
            new DiffEntry("achieve", "0.3", "0.25"),
            new DiffEntry("actualStart", "2026-11-01", "2026-10-31"),
            new DiffEntry("causality", null, "late supplier"),
        ]);
    }

    [Theory]
    [InlineData(4, 5, 4, 3, 4.00)]
    [InlineData(1, 1, 1, 2, 1.25)]
    [InlineData(5, 5, 5, 5, 5.00)]
    [InlineData(3, 4, 4, 4, 3.75)]
    public void Rating_overall_is_the_average_of_the_four_criteria(int t, int q, int r, int rep, decimal overall) =>
        VendorRatingCalculator.Overall(new VendorRatingValues(t, q, r, rep, null)).ShouldBe(overall);

    [Fact]
    public void Rating_values_must_be_one_to_five_and_satisfaction_rounds_the_average()
    {
        VendorRatingCalculator.Valid(new VendorRatingValues(1, 5, 3, 2, null)).ShouldBeTrue();
        VendorRatingCalculator.Valid(new VendorRatingValues(0, 5, 3, 2, null)).ShouldBeFalse();
        VendorRatingCalculator.Valid(new VendorRatingValues(1, 6, 3, 2, null)).ShouldBeFalse();
        VendorRatingCalculator.Satisfaction([]).ShouldBeNull();
        VendorRatingCalculator.Satisfaction([3.5m]).ShouldBe(4);
        VendorRatingCalculator.Satisfaction([3.25m, 3.5m]).ShouldBe(3);
    }

    [Fact]
    public void Inconsistency_flags_are_computed_by_the_server()
    {
        var today = new DateOnly(2026, 11, 4);
        var complete = new VendorProgressValues(0.4m, today.AddDays(-20), today, null, null, null);
        VendorInconsistencies.Flags(complete, 0.4m, 0m, today.AddDays(-20), today.AddDays(10), today, 0, null, null)
            .ShouldBe(["completeWithoutEvidence", "progressJump"], ignoreOrder: true);
        VendorInconsistencies.Flags(complete, 0.4m, 0.3m, today.AddDays(-20), today.AddDays(10), today, 1, null, null).ShouldBeEmpty();

        var outside = new VendorProgressValues(0.1m, today.AddDays(-5), null, null, null, null);
        VendorInconsistencies.Flags(outside, 0.4m, 0.05m, today.AddDays(-5), today.AddDays(10), today, 0, today, today.AddDays(30))
            .ShouldBe(["dateOutsideContract"]);

        var late = new VendorProgressValues(0.1m, today.AddDays(-50), null, null, null, null);
        VendorInconsistencies.Flags(late, 0.4m, 0.08m, today.AddDays(-50), today.AddDays(-1), today, 0, null, null).ShouldBe(["delayedWithoutCausality"]);

        var early = new VendorProgressValues(0.1m, today.AddDays(-60), today.AddDays(2), "c", "a", null);
        VendorInconsistencies.Flags(early, 0.4m, 0.08m, today.AddDays(-10), today.AddDays(10), today, 0, null, null)
            .ShouldBe(["startBeforePlan", "endInFuture"], ignoreOrder: true);
    }

    [Fact]
    public void Proposal_rules_check_parents_weights_dates_and_tasks()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        List<ProposalItem> plan =
        [
            new("p", ProposalItemTypes.Phase, null, null, "Phase", null, null, null),
            new("m1", ProposalItemTypes.Milestone, "p", null, "One", start, start.AddDays(30), 0.6m, Tasks: ["Task A"]),
            new("m2", ProposalItemTypes.Milestone, "p", "اثنان", null, start.AddDays(31), end, 0.4m),
        ];
        ProposalRules.Validate(plan, Methodologies.Waterfall, 1m, start, end).ShouldBeEmpty();
        var leaf = plan[1] with { ParentKey = null };
        ProposalRules.Validate(plan, Methodologies.Waterfall, 0.9m, start, end).Single().MessageKey.ShouldBe("portfolio.errors.weightsOver100");
        ProposalRules.Validate([plan[1] with { ParentKey = "x" }], Methodologies.Waterfall, 1m, start, end).Single().MessageKey.ShouldBe("portfolio.errors.parentInvalid");
        ProposalRules.Validate([leaf with { PlannedEnd = end.AddDays(1) }], Methodologies.Waterfall, 1m, start, end).Single().MessageKey
            .ShouldBe("portfolio.errors.outsideProjectDates");
        ProposalRules.Validate([leaf with { Weight = 0 }], Methodologies.Waterfall, 1m, null, null).Single().MessageKey.ShouldBe("portfolio.errors.weightRange");
        ProposalRules.Validate([leaf with { TitleEn = null }], Methodologies.Waterfall, 1m, null, null).Single().MessageKey.ShouldBe("validation.atLeastOneLanguage");
        ProposalRules.Validate([leaf with { IsDeliverable = true }], Methodologies.Waterfall, 1m, null, null).Single().MessageKey
            .ShouldBe("portfolio.errors.deliverableFields");
        ProposalRules.Validate([], Methodologies.Waterfall, 1m, null, null).Single().MessageKey.ShouldBe("portfolio.errors.proposalEmpty");
        ProposalRules.Validate([leaf, leaf], Methodologies.Waterfall, 1m, null, null).ShouldContain(e => e.MessageKey == "portfolio.errors.proposalKeys");

        List<ProposalItem> agile =
        [
            new("e", ProposalItemTypes.Epic, null, null, "Epic", null, null, 1m),
            new("s", ProposalItemTypes.Story, "e", null, "Story", null, null, null, StoryPoints: 5),
        ];
        ProposalRules.Validate(agile, Methodologies.Agile, 1m, null, null).ShouldBeEmpty();
        ProposalRules.Validate([agile[1] with { ParentKey = null }], Methodologies.Agile, 1m, null, null).Single().MessageKey.ShouldBe("portfolio.errors.parentInvalid");
        ProposalRules.Validate([agile[0], agile[1] with { StoryPoints = 101 }], Methodologies.Agile, 1m, null, null).Single().MessageKey
            .ShouldBe("validation.inclusiveBetween");
        ProposalRules.Validate(plan, Methodologies.Agile, 1m, null, null).ShouldContain(e => e.MessageKey == "validation.unknownCode");
    }

    [Fact]
    public void Proposal_diff_records_changes_and_added_and_removed_items()
    {
        var day = new DateOnly(2026, 3, 1);
        List<ProposalItem> vendor =
        [
            new("m1", ProposalItemTypes.Milestone, null, null, "Design", day, day.AddDays(9), 0.6m),
            new("m2", ProposalItemTypes.Milestone, null, null, "Build", day.AddDays(10), day.AddDays(30), 0.4m),
        ];
        List<ProposalItem> pm =
        [
            vendor[0] with { TitleEn = "Design and sign-off", Weight = 0.5m },
            new("m3", ProposalItemTypes.Milestone, null, null, "Test", day.AddDays(10), day.AddDays(30), 0.5m),
        ];
        ProposalRules.Diff(vendor, pm).ShouldBe(
        [
            new ProposalDiffEntry("m1", "titleEn", "Design", "Design and sign-off"),
            new ProposalDiffEntry("m1", "weight", "0.6", "0.5"),
            new ProposalDiffEntry("m2", "removed", "Build", null),
            new ProposalDiffEntry("m3", "added", null, "Test"),
        ]);
        ProposalRules.Diff(vendor, vendor).ShouldBeEmpty();
    }

    [Fact]
    public void Capacity_sums_allocations_and_warns_above_the_threshold()
    {
        CapacityCalculator.Total([0.6m, 0.5m, null]).ShouldBe(1.1m);
        CapacityCalculator.OverAllocated(1.1m, 1m).ShouldBeTrue();
        CapacityCalculator.OverAllocated(1m, 1m).ShouldBeFalse("exactly 100 % is not over-allocated");
        CapacityCalculator.OverAllocated(1.1m, 1.2m).ShouldBeFalse();
        CapacityCalculator.CountProposedMember(null).ShouldBeTrue();
        CapacityCalculator.CountProposedMember(Guid.CreateVersion7()).ShouldBeFalse("a formed team's own allocation is used, not the charter list");
    }

    [Theory]
    [InlineData(ReviewDecisions.Continue, null, false)]
    [InlineData(ReviewDecisions.Rescope, ChangeTypes.Details, false)]
    [InlineData(ReviewDecisions.Hold, ChangeTypes.Hold, true)]
    [InlineData(ReviewDecisions.Cancel, ChangeTypes.Cancellation, true)]
    public void Review_decisions_start_change_requests_never_direct_changes(string decision, string? changeType, bool submitted)
    {
        ReviewDecisions.ChangeTypeFor(decision).ShouldBe(changeType);
        ReviewDecisions.SubmitsAtOnce(decision).ShouldBe(submitted);
    }

    [Theory]
    [InlineData("2026-Q1", true)]
    [InlineData("2026-Q4", true)]
    [InlineData("2026-Q5", false)]
    [InlineData("2026-Q0", false)]
    [InlineData("26-Q1", false)]
    [InlineData("2026Q1", false)]
    [InlineData(null, false)]
    public void Review_periods_are_quarters(string? period, bool valid) => PortfolioReview.ValidPeriod(period).ShouldBe(valid);

    /// <summary>ADR 0113: vendor DTOs are separate records whose shapes never carry internal fields.</summary>
    [Fact]
    public void Vendor_dtos_carry_no_internal_fields()
    {
        var vendorDtos = typeof(VendorProjectDto).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == typeof(VendorProjectDto).Namespace && t.Name.StartsWith("Vendor", StringComparison.Ordinal) && t.Name.EndsWith("Dto", StringComparison.Ordinal)
                && t != typeof(VendorDetailDto) && t != typeof(VendorListItemDto) && t != typeof(VendorUserDto) && t != typeof(VendorRatingDto) && t != typeof(VendorPerformanceDto)
                && t != typeof(VendorReviewAiDto) && t != typeof(VendorReviewFlagDto))
            .ToList();
        vendorDtos.Count.ShouldBeGreaterThanOrEqualTo(10);
        string[] denied = ["Approval", "Approver", "Internal", "Team", "Stakeholder", "Owner", "Cost", "Budget", "Flags", "PendingSource", "Health", "Pm"];
        string[] allowed = ["Budget", "Cost", "Owner"]; // optional vendor-visible flags (null unless switched on in Settings)
        string[] approvedNames = ["ApprovedAchieve", "ApprovedActualStart", "ApprovalStatus", "PmComment", "ProjectManager"];
        foreach (var dto in vendorDtos)
        {
            foreach (var property in dto.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (approvedNames.Contains(property.Name) || allowed.Contains(property.Name))
                {
                    continue;
                }

                denied.ShouldNotContain(d => property.Name.Contains(d, StringComparison.Ordinal), $"{dto.Name}.{property.Name}");
            }
        }

        typeof(VendorProjectDto).GetProperty(nameof(VendorProjectDto.Budget))!.PropertyType.ShouldBe(typeof(decimal?));
        typeof(VendorMilestoneDto).GetProperty(nameof(VendorMilestoneDto.Cost))!.PropertyType.ShouldBe(typeof(decimal?));
    }

    [Fact]
    public void Vendor_record_roles_give_wasel_permissions_only_and_the_contributor_cannot_submit()
    {
        WaselPermissions.LeadPermissions.ShouldAllBe(p => p.StartsWith("wasel.", StringComparison.Ordinal));
        WaselPermissions.ContributorPermissions.ShouldNotContain(WaselPermissions.UpdateSubmit);
        WaselPermissions.ContributorPermissions.ShouldNotContain(WaselPermissions.PlanSubmit);
        WaselPermissions.LeadPermissions.ShouldContain(WaselPermissions.UpdateSubmit);
        WaselPermissions.LeadPermissions.ShouldContain(WaselPermissions.PlanSubmit);
    }
}
