using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;

namespace Strategy.UnitTests.AuditManagement;

/// <summary>Recommendation status rule (docs/design/audit.md §4.1, ADR 0131).</summary>
public class RecommendationStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData(RecommendationStatuses.NotStarted, 0, 10, RecommendationStatuses.NotStarted)]
    [InlineData(RecommendationStatuses.NotStarted, 0.3, 10, RecommendationStatuses.Continuing)]
    [InlineData(RecommendationStatuses.Continuing, 0.3, 0, RecommendationStatuses.Continuing)] // due today: not late yet
    [InlineData(RecommendationStatuses.Continuing, 0.3, -1, RecommendationStatuses.Late)]
    [InlineData(RecommendationStatuses.NotStarted, 0, -1, RecommendationStatuses.Late)]
    [InlineData(RecommendationStatuses.Late, 0.5, 5, RecommendationStatuses.Continuing)] // revised date in the future
    [InlineData(RecommendationStatuses.Late, 1, -5, RecommendationStatuses.Completed)]
    [InlineData(RecommendationStatuses.Continuing, 1, 5, RecommendationStatuses.Completed)]
    [InlineData(RecommendationStatuses.Completed, 0.2, -30, RecommendationStatuses.Completed)]
    [InlineData(RecommendationStatuses.RiskAccepted, 0.2, -30, RecommendationStatuses.RiskAccepted)]
    [InlineData(RecommendationStatuses.Cancelled, 0, -30, RecommendationStatuses.Cancelled)]
    public void Derives_the_status_from_progress_and_due_date(string current, double pct, int dueInDays, string expected) =>
        RecommendationStatus.Derive(current, (decimal)pct, Today.AddDays(dueInDays), Today).ShouldBe(expected);
}

/// <summary>Finding closure and completion (docs/design/audit.md §4.2): closes when all recommendations are Completed / Risk Accepted / Cancelled.</summary>
public class FindingStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static FindingStatus.RecommendationState S(string status, decimal pct = 0) => new(status, pct);

    public static TheoryData<string, FindingStatus.RecommendationState[], int?, string> Table() => new()
    {
        { AuditFindingStatuses.Open, [], null, AuditFindingStatuses.Open },
        { AuditFindingStatuses.Open, [], -1, AuditFindingStatuses.Late },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.Continuing, 0.5m)], null, AuditFindingStatuses.Open },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.Late)], null, AuditFindingStatuses.Late },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.NotStarted)], -3, AuditFindingStatuses.Late },
        { AuditFindingStatuses.Late, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.Continuing)], 10, AuditFindingStatuses.Open },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.RiskAccepted)], null, AuditFindingStatuses.Closed },
        { AuditFindingStatuses.Late, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.Cancelled)], -9, AuditFindingStatuses.Closed },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.RiskAccepted), S(RecommendationStatuses.Cancelled)], null, AuditFindingStatuses.RiskAccepted },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.RiskAccepted), S(RecommendationStatuses.RiskAccepted)], null, AuditFindingStatuses.RiskAccepted },
        { AuditFindingStatuses.Open, [S(RecommendationStatuses.Cancelled)], null, AuditFindingStatuses.Closed },
        { AuditFindingStatuses.Closed, [S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.NotStarted)], null, AuditFindingStatuses.Open },
        { AuditFindingStatuses.Cancelled, [S(RecommendationStatuses.Late)], -9, AuditFindingStatuses.Cancelled },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Derives_the_status_from_the_recommendations(string current, FindingStatus.RecommendationState[] recommendations, int? expectedInDays, string expected) =>
        FindingStatus.Derive(current, recommendations, expectedInDays is { } d ? Today.AddDays(d) : null, Today).ShouldBe(expected);

    [Fact]
    public void Completion_is_the_mean_of_the_live_recommendations()
    {
        FindingStatus.Completion([]).ShouldBe(0m);
        FindingStatus.Completion([S(RecommendationStatuses.Completed, 0.4m), S(RecommendationStatuses.Continuing, 0.5m), S(RecommendationStatuses.Cancelled, 0)])
            .ShouldBe(0.75m);
        FindingStatus.Completion([S(RecommendationStatuses.RiskAccepted, 0.2m), S(RecommendationStatuses.NotStarted)]).ShouldBe(0.1m);
    }

    [Fact]
    public void Refresh_sets_and_clears_the_actual_closed_date()
    {
        var finding = Finding.Create("AR-0001", Guid.CreateVersion7(), new FindingValues("ع", "Title", FindingTypes.Finding, "Condition", null, null, null, null, Today,
            Guid.CreateVersion7(), AuditPriorities.High, false, null, null, null), Guid.CreateVersion7());
        finding.Refresh([S(RecommendationStatuses.Completed, 1)], Today).ShouldBeTrue();
        (finding.Status, finding.ActualClosedOn, finding.Completion).ShouldBe((AuditFindingStatuses.Closed, (DateOnly?)Today, 1m));
        finding.Refresh([S(RecommendationStatuses.Completed, 1), S(RecommendationStatuses.NotStarted)], Today.AddDays(1)).ShouldBeTrue();
        (finding.Status, finding.ActualClosedOn).ShouldBe((AuditFindingStatuses.Open, (DateOnly?)null));
        finding.Cancel("duplicate").IsSuccess.ShouldBeTrue();
        finding.Refresh([S(RecommendationStatuses.Completed, 1)], Today).ShouldBeFalse();
        finding.Cancel("again").IsFailure.ShouldBeTrue();
    }
}

/// <summary>Report status from its findings (docs/design/audit.md §4.3).</summary>
public class ReportStatusTests
{
    [Theory]
    [InlineData(ReportStatuses.Draft, new[] { AuditFindingStatuses.Closed }, ReportStatuses.Draft)]
    [InlineData(ReportStatuses.Cancelled, new[] { AuditFindingStatuses.Open }, ReportStatuses.Cancelled)]
    [InlineData(ReportStatuses.Issued, new string[0], ReportStatuses.Issued)]
    [InlineData(ReportStatuses.Issued, new[] { AuditFindingStatuses.Open }, ReportStatuses.UnderFollowUp)]
    [InlineData(ReportStatuses.UnderFollowUp, new[] { AuditFindingStatuses.Closed, AuditFindingStatuses.Late }, ReportStatuses.UnderFollowUp)]
    [InlineData(ReportStatuses.UnderFollowUp, new[] { AuditFindingStatuses.Closed, AuditFindingStatuses.RiskAccepted, AuditFindingStatuses.Cancelled }, ReportStatuses.Closed)]
    [InlineData(ReportStatuses.UnderFollowUp, new[] { AuditFindingStatuses.Cancelled }, ReportStatuses.UnderFollowUp)]
    [InlineData(ReportStatuses.Closed, new[] { AuditFindingStatuses.Closed, AuditFindingStatuses.Open }, ReportStatuses.UnderFollowUp)]
    public void Closes_when_every_finding_is_closed(string current, string[] findings, string expected) =>
        ReportStatus.Derive(current, findings).ShouldBe(expected);

    [Fact]
    public void Issue_only_from_draft_and_cancel_only_while_open()
    {
        var report = AuditReport.Create("AUD-2026-001", new AuditReportValues(null, "Report", ReportTypes.InternalAudit, null, null, null, null, null, null, null, null, null));
        report.Issue(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        report.Issue(DateTimeOffset.UtcNow).IsFailure.ShouldBeTrue();
        report.Cancel("withdrawn").IsSuccess.ShouldBeTrue();
        report.Cancel("again").IsFailure.ShouldBeTrue();
    }
}

/// <summary>Recommendation actions: follow-up, revision, risk acceptance, reopen, cancel.</summary>
public class RecommendationTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static Recommendation New() =>
        Recommendation.Create("AA-0001", Guid.CreateVersion7(), new RecommendationValues(null, "Act", null, null, null, null, Today.AddDays(10), null));

    [Fact]
    public void Follow_up_at_100_percent_completes_and_clears_the_next_follow_up()
    {
        var r = New();
        r.ApplyFollowUp(0.4m, Today, null, Today.AddDays(7), "partly");
        r.Refresh(Today);
        (r.Status, r.NextFollowUpOn, r.LastComment).ShouldBe((RecommendationStatuses.Continuing, (DateOnly?)Today.AddDays(7), "partly"));
        r.ApplyFollowUp(1m, Today.AddDays(5), Today.AddDays(4), Today.AddDays(30), "done");
        (r.Status, r.CompletedOn, r.NextFollowUpOn).ShouldBe((RecommendationStatuses.Completed, (DateOnly?)Today.AddDays(4), (DateOnly?)null));
        r.Revise(Today.AddDays(60), "x").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revision_keeps_the_original_date_and_risk_acceptance_can_be_reopened()
    {
        var r = New();
        r.Refresh(Today.AddDays(11)).ShouldBeTrue();
        r.Status.ShouldBe(RecommendationStatuses.Late);
        r.Revise(Today.AddDays(40), "restructuring").IsSuccess.ShouldBeTrue();
        r.Refresh(Today.AddDays(11)).ShouldBeTrue();
        (r.Status, r.DueOn, r.EffectiveDue).ShouldBe((RecommendationStatuses.NotStarted, Today.AddDays(10), Today.AddDays(40)));
        r.AcceptRisk("accepted by management", "Memo 14", Today).IsSuccess.ShouldBeTrue();
        r.Reopen().IsSuccess.ShouldBeTrue();
        r.Reopen().IsFailure.ShouldBeTrue();
        r.Cancel("superseded").IsSuccess.ShouldBeTrue();
        r.Cancel("again").IsFailure.ShouldBeTrue();
    }
}

/// <summary>Follow-up validation (docs/design/audit.md §4.4).</summary>
public class FollowUpValidationTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public void Next_follow_up_date_is_required_below_100_percent_and_must_be_later()
    {
        FollowUp.Validate(new FollowUpValues(Today, null, 0.5m, null, null, null, null), Today).Select(e => e.Field).ShouldBe(["nextFollowUpOn"]);
        FollowUp.Validate(new FollowUpValues(Today, null, 0.5m, null, null, Today, null), Today).Single().Code.ShouldBe("auditmgmt.errors.nextFollowUpAfter");
        FollowUp.Validate(new FollowUpValues(Today, null, 0.5m, null, null, Today.AddDays(1), null), Today).ShouldBeEmpty();
        FollowUp.Validate(new FollowUpValues(Today, Today, 1m, null, null, null, null), Today).ShouldBeEmpty();
    }

    [Fact]
    public void Percent_range_and_future_dates_are_refused()
    {
        FollowUp.Validate(new FollowUpValues(Today, null, 1.2m, null, null, null, null), Today).ShouldContain(e => e.Field == "pct");
        FollowUp.Validate(new FollowUpValues(Today.AddDays(1), null, 1m, null, null, null, null), Today).ShouldContain(e => e.Field == "followUpOn");
        FollowUp.Validate(new FollowUpValues(Today, Today.AddDays(2), 1m, null, null, null, null), Today).ShouldContain(e => e.Field == "achievedOn");
    }
}

/// <summary>Layer 2 engagement state machine (docs/design/audit.md §4.6): every allowed and every forbidden transition.</summary>
public class EngagementLifecycleTests
{
    public static TheoryData<string, string, string?> Table()
    {
        var allowed = new Dictionary<(string, string), string>
        {
            [(EngagementStages.Planned, EngagementActions.Start)] = EngagementStages.Planning,
            [(EngagementStages.Planning, EngagementActions.ToFieldwork)] = EngagementStages.Fieldwork,
            [(EngagementStages.Fieldwork, EngagementActions.ToReporting)] = EngagementStages.Reporting,
            [(EngagementStages.Reporting, EngagementActions.ToFollowUp)] = EngagementStages.FollowUp,
            [(EngagementStages.FollowUp, EngagementActions.Close)] = EngagementStages.Closed,
        };
        foreach (var open in new[] { EngagementStages.Planned, EngagementStages.Planning, EngagementStages.Fieldwork, EngagementStages.Reporting, EngagementStages.FollowUp })
        {
            allowed[(open, EngagementActions.Cancel)] = EngagementStages.Cancelled;
        }

        var data = new TheoryData<string, string, string?>();
        foreach (var stage in EngagementStages.All)
        {
            foreach (var action in EngagementActions.All)
            {
                data.Add(stage, action, allowed.GetValueOrDefault((stage, action)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Transition_table(string from, string action, string? expected) => EngagementLifecycle.Next(from, action).ShouldBe(expected);

    [Fact]
    public void Reporting_stamps_the_end_of_fieldwork_and_cancel_keeps_the_reason()
    {
        var e = Engagement.Create("ENG-2027-001", null, new EngagementValues(null, "Audit", null, null, null, null, null, 2, null, null, 100), 6m);
        var day = new DateOnly(2027, 5, 1);
        e.Apply(EngagementActions.Start, day).IsSuccess.ShouldBeTrue();
        e.Apply(EngagementActions.ToFieldwork, day).IsSuccess.ShouldBeTrue();
        e.Apply(EngagementActions.ToReporting, day).IsSuccess.ShouldBeTrue();
        e.FieldworkEndOn.ShouldBe(day);
        e.Apply(EngagementActions.Start, day).IsFailure.ShouldBeTrue();
        e.Apply(EngagementActions.Cancel, day, "scope moved").IsSuccess.ShouldBeTrue();
        (e.Stage, e.CancelReason).ShouldBe((EngagementStages.Cancelled, "scope moved"));
    }

    [Theory]
    [InlineData(AuditPriorities.Critical, null, 24)]
    [InlineData(AuditPriorities.Low, 0, 1)]
    [InlineData(AuditPriorities.High, 730, 9)]
    [InlineData(null, 365, 2)]
    public void Risk_score_weights_the_rating_by_the_years_since_the_last_audit(string? rating, int? daysSinceAudit, decimal expected)
    {
        var today = new DateOnly(2027, 1, 1);
        AuditRiskScore.Of(rating, daysSinceAudit is { } d ? today.AddDays(-d) : null, today).ShouldBe(expected);
    }

    [Fact]
    public void Working_paper_sign_off_needs_a_prepared_paper_and_another_reviewer()
    {
        var preparer = Guid.CreateVersion7();
        var paper = WorkPaper.Create(Guid.CreateVersion7(), new WorkPaperValues("WP-1", "Test", null, null, null, null));
        paper.SignOff(Guid.CreateVersion7(), DateTimeOffset.UtcNow).IsFailure.ShouldBeTrue();
        paper.Prepare(preparer);
        paper.SignOff(preparer, DateTimeOffset.UtcNow).IsFailure.ShouldBeTrue();
        paper.SignOff(Guid.CreateVersion7(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        paper.Status.ShouldBe(WorkPaperStatuses.Reviewed);
        paper.Edit(new WorkPaperValues("WP-1", "Test changed", null, null, null, null));
        paper.Status.ShouldBe(WorkPaperStatuses.Prepared);
    }
}

/// <summary>Settings validation (docs/design/audit.md §2.1).</summary>
public class AuditSettingsTests
{
    [Fact]
    public void Defaults_are_valid_and_invalid_values_are_reported()
    {
        var defaults = new AuditMgmtSettings();
        AuditMgmtSettings.Validate(defaults).ShouldBeEmpty();
        (defaults.DepartmentScope, defaults.FollowUpReview, defaults.NotifyDepartments).ShouldBe((DepartmentScopes.Section, false, false));
        AuditMgmtSettings.Validate(defaults with { DepartmentScope = "x", FollowUpReminderDays = -1, CapObservations = "auto", DepartmentLevelCode = " " })
            .Select(e => e.Field).ShouldBe(["followUpReminderDays", "departmentScope", "departmentLevelCode", "capObservations"], ignoreOrder: true);
    }
}
