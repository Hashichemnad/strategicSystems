using System.Text.Json;
using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Portfolio;

/// <summary>Q-P5 classification table (Appendix A): Project only when every screening answer is Yes and the budget is above the threshold.</summary>
public class ClassificationCalculatorTests
{
    private const decimal Threshold = 50_000m;

    private static RequestValues Yes(decimal? budget, bool requiresBudget = true) => new()
    {
        RequiresBudget = requiresBudget,
        EstimatedBudget = budget,
        ScreenCoreWork = true,
        ScreenOneTime = true,
        ScreenTimeBound = true,
        ScreenUniqueOutput = true,
        ScreenMinCost = true,
        ScreenOperationalCosts = true,
    };

    [Theory]
    [InlineData(50_000.01, true, Classifications.Project)]
    [InlineData(50_000, true, Classifications.Initiative)] // equal to the threshold is not above it
    [InlineData(49_999.99, true, Classifications.Initiative)]
    [InlineData(1_000_000, false, Classifications.Initiative)] // budget not required
    public void Budget_against_the_threshold(double budget, bool requiresBudget, string expected) =>
        ClassificationCalculator.Classify(Yes((decimal)budget, requiresBudget), Threshold).ShouldBe(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Any_single_no_or_unanswered_question_makes_an_initiative(int question)
    {
        foreach (var answer in new bool?[] { false, null })
        {
            var values = Yes(200_000m);
            values = question switch
            {
                0 => values with { ScreenCoreWork = answer },
                1 => values with { ScreenOneTime = answer },
                2 => values with { ScreenTimeBound = answer },
                3 => values with { ScreenUniqueOutput = answer },
                4 => values with { ScreenMinCost = answer },
                _ => values with { ScreenOperationalCosts = answer },
            };
            ClassificationCalculator.Classify(values, Threshold).ShouldBe(Classifications.Initiative);
        }
    }

    [Fact]
    public void No_budget_given_is_an_initiative_and_the_override_wins() =>
        ClassificationCalculator.Effective(ClassificationCalculator.Classify(Yes(null), Threshold), Classifications.Project).ShouldBe(Classifications.Project);
}

/// <summary>Q-P6 evaluation score and bands (High ≥ 80 %, Medium 60–79 %, Low &lt; 60 %), equal weights by default, weights configurable.</summary>
public class PriorityCalculatorTests
{
    private static readonly IReadOnlyList<PriorityCriterion> Criteria = PortfolioSettings.DefaultCriteria;

    private static Dictionary<string, int> Scores(int a, int b, int c, int d) =>
        new() { ["vision-alignment"] = a, ["impact"] = b, ["complexity"] = c, ["radical-change"] = d };

    [Theory]
    [InlineData(5, 5, 5, 5, 1.0, "high")]
    [InlineData(4, 4, 4, 4, 0.8, "high")] // boundary
    [InlineData(5, 4, 3, 4, 0.8, "high")]
    [InlineData(4, 4, 4, 3, 0.75, "medium")]
    [InlineData(3, 3, 3, 3, 0.6, "medium")] // boundary
    [InlineData(3, 3, 3, 2, 0.55, "low")]
    [InlineData(1, 1, 1, 1, 0.2, "low")]
    public void Score_and_band(int a, int b, int c, int d, double score, string band)
    {
        var result = PriorityCalculator.Score(Scores(a, b, c, d), Criteria);
        result.ShouldBe((decimal)score);
        PriorityCalculator.Band(result, PortfolioSettings.DefaultBands)!.Code.ShouldBe(band);
    }

    [Fact]
    public void Weights_are_applied()
    {
        var weighted = Criteria.Select(c => c.Code == "vision-alignment" ? c with { Weight = 2 } : c).ToList();
        PriorityCalculator.Score(Scores(5, 3, 3, 3), weighted).ShouldBe(0.76m); // (10 + 9) / 5 / 5
    }

    [Fact]
    public void Missing_or_invalid_scores_give_no_score()
    {
        PriorityCalculator.Score(new Dictionary<string, int> { ["impact"] = 4 }, Criteria).ShouldBeNull();
        PriorityCalculator.Score(Scores(6, 4, 4, 4), Criteria).ShouldBeNull();
        PriorityCalculator.Score(Scores(0, 4, 4, 4), Criteria).ShouldBeNull();
        PriorityCalculator.Score(null, Criteria).ShouldBeNull();
        PriorityCalculator.Band(null, PortfolioSettings.DefaultBands).ShouldBeNull();
    }

    [Fact]
    public void Inactive_criteria_are_ignored()
    {
        var criteria = Criteria.Select(c => c.Code == "complexity" ? c with { Active = false } : c).ToList();
        PriorityCalculator.Score(new Dictionary<string, int> { ["vision-alignment"] = 5, ["impact"] = 5, ["radical-change"] = 5 }, criteria).ShouldBe(1m);
    }
}

/// <summary>Every SPEC §1.4 A validation rule (ADR 0047) with its boundaries.</summary>
public class RequestRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static readonly RequestRuleContext Context = new(Today, 3, 3, PortfolioSettings.DefaultCriteria, CostEstimationUploaded: true);

    public static RequestValues Valid() => new()
    {
        NameEn = "Smart licensing",
        Description = "d",
        Justifications = "j",
        StrategicObjectiveCode = "obj",
        SuggestionSourceCode = "idea",
        ProgramTypeCode = "digital",
        RequiresBudget = true,
        EstimatedBudget = 100_000m,
        Year1Cost = 40_000m,
        Year2Cost = 30_000m,
        Year3Cost = 20_000m,
        Year4Cost = 10_000m,
        CostPath = CostPaths.CostEstimation,
        ExpectedStartDate = new DateOnly(2027, 1, 1),
        ExpectedEndDate = new DateOnly(2028, 12, 31),
        UnitId = Guid.CreateVersion7(),
        ProgramNature = ProgramNatures.New,
        ScreenCoreWork = true,
        ScreenOneTime = true,
        ScreenTimeBound = true,
        ScreenUniqueOutput = true,
        ScreenMinCost = true,
        ScreenOperationalCosts = false,
        PriorityScores = new Dictionary<string, int> { ["vision-alignment"] = 4, ["impact"] = 4, ["complexity"] = 3, ["radical-change"] = 2 },
        PracticeOperation = "Licensing",
    };

    private static Dictionary<string, string> Submit(RequestValues v, RequestRuleContext? context = null) =>
        RequestRules.ValidateForSubmission(v, context ?? Context).GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.First().Code);

    [Fact]
    public void A_complete_request_passes() => Submit(Valid()).ShouldBeEmpty();

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_000.01, false)]
    [InlineData(9_999.99, false)]
    public void Year_costs_must_equal_the_budget_exactly(double year4, bool ok)
    {
        var errors = RequestRules.ValidateForSubmission(Valid() with { Year4Cost = (decimal)year4 }, Context);
        if (ok)
        {
            errors.ShouldBeEmpty();
            return;
        }

        var error = errors.Single();
        error.Code.ShouldBe("portfolio.errors.yearCostsMismatch");
        error.Params!["budget"].ShouldBe("100000.00");
    }

    [Fact]
    public void Missing_year_costs_count_as_zero() =>
        Submit(Valid() with { Year2Cost = null, Year3Cost = null, Year4Cost = null })["yearCosts"].ShouldBe("portfolio.errors.yearCostsMismatch");

    [Theory]
    [InlineData("2026-01-01", null)] // current year
    [InlineData("2025-12-31", "portfolio.errors.startOutOfRange")] // previous year
    [InlineData("2029-12-31", null)] // current year + 3
    [InlineData("2030-01-01", "portfolio.errors.startOutOfRange")]
    public void Start_within_the_current_year_or_the_next_three(string start, string? code)
    {
        var date = DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        Submit(Valid() with { ExpectedStartDate = date, ExpectedEndDate = date.AddDays(1) > new DateOnly(2029, 12, 31) ? date : date.AddDays(1) })
            .GetValueOrDefault("expectedStartDate").ShouldBe(code);
    }

    [Theory]
    [InlineData("2029-12-31", null)]
    [InlineData("2030-01-01", "portfolio.errors.endOutOfRange")]
    [InlineData("2026-12-31", "portfolio.errors.endBeforeStart")]
    public void End_on_or_after_start_and_within_three_years(string end, string? code) =>
        Submit(Valid() with { ExpectedEndDate = DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture) }).GetValueOrDefault("expectedEndDate").ShouldBe(code);

    [Theory]
    [InlineData(999, "portfolio.errors.fourDigitYear")]
    [InlineData(1000, null)]
    [InlineData(9999, null)]
    [InlineData(10000, "portfolio.errors.fourDigitYear")]
    public void Return_and_reduction_years_have_four_digits(int year, string? code)
    {
        var values = Valid() with { ReturnExpected = true, ReturnValue = 1, ReturnYear = year, ReductionExpected = true, ReductionValue = 1, ReductionYear = year };
        var errors = RequestRules.ValidateFormat(values, Context).ToDictionary(e => e.Field, e => e.Code);
        errors.GetValueOrDefault("returnYear").ShouldBe(code);
        errors.GetValueOrDefault("reductionYear").ShouldBe(code);
    }

    [Fact]
    public void Expected_return_or_reduction_needs_value_and_year()
    {
        var errors = Submit(Valid() with { ReturnExpected = true, ReductionExpected = true });
        errors.Keys.ShouldBe(["returnValue", "returnYear", "reductionValue", "reductionYear"], ignoreOrder: true);
    }

    [Fact]
    public void Either_operation_or_service_is_required()
    {
        Submit(Valid() with { PracticeOperation = null })["practiceOperation"].ShouldBe("portfolio.errors.operationOrService");
        Submit(Valid() with { PracticeOperation = " ", PracticeService = "Service" }).ShouldBeEmpty();
    }

    [Fact]
    public void Continuous_programs_name_the_previous_program()
    {
        Submit(Valid() with { ProgramNature = ProgramNatures.Continuous })["previousProgramName"].ShouldBe("portfolio.errors.previousProgramRequired");
        Submit(Valid() with { ProgramNature = ProgramNatures.Continuous, PreviousProgramName = "Phase 1" }).ShouldBeEmpty();
    }

    [Fact]
    public void Other_requires_free_text_on_every_save()
    {
        var errors = RequestRules.ValidateFormat(Valid() with { SuggestionSourceCode = "other", MainOptionCode = "other", AltOptionCode = "other" }, Context)
            .Select(e => (e.Field, e.Code)).ToList();
        errors.ShouldBe([
            ("suggestionSourceOther", "portfolio.errors.otherTextRequired"), ("mainOptionOther", "portfolio.errors.otherTextRequired"),
            ("altOptionOther", "portfolio.errors.otherTextRequired")], ignoreOrder: true);
        RequestRules.ValidateFormat(Valid() with { SuggestionSourceCode = "other", SuggestionSourceOther = "Citizen council" }, Context).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100.005, "portfolio.errors.twoDecimals")]
    [InlineData(100.05, null)]
    [InlineData(-1, "validation.greaterThanOrEqual")]
    public void Amounts_have_two_decimals_and_are_not_negative(double amount, string? code) =>
        (RequestRules.ValidateFormat(Valid() with { AnnualOperationCost = (decimal)amount }, Context).SingleOrDefault()?.Code).ShouldBe(code);

    [Fact]
    public void Screening_priority_and_core_fields_are_required_at_submission()
    {
        var errors = Submit(new RequestValues());
        errors.Keys.ShouldBe([
            "nameEn", "description", "justifications", "strategicObjectiveCode", "suggestionSourceCode", "programTypeCode", "programNature", "requiresBudget",
            "unitId", "expectedStartDate", "expectedEndDate", "screening", "priorityScores", "practiceOperation"], ignoreOrder: true);
        RequestRules.ValidateFormat(new RequestValues(), Context).ShouldBeEmpty(); // an empty draft can be saved
    }

    [Fact]
    public void Cost_path_must_be_complete_when_budget_is_required()
    {
        Submit(Valid() with { CostPath = null })["costPath"].ShouldBe("validation.notEmpty");
        Submit(Valid(), Context with { CostEstimationUploaded = false })["costEstimation"].ShouldBe("portfolio.errors.costEstimationMissing");
        Submit(Valid() with { CostPath = CostPaths.ProjectScope })["quotations"].ShouldBe("portfolio.errors.quotationsMissing");
        Submit(Valid() with { CostPath = CostPaths.ProjectScope }, Context with { QuotationsUploaded = true }).ShouldBeEmpty();
        Submit(Valid() with { RequiresBudget = false, EstimatedBudget = null, Year1Cost = null, Year2Cost = null, Year3Cost = null, Year4Cost = null, CostPath = null })
            .ShouldBeEmpty();
    }

    [Fact]
    public void Scores_are_one_to_five_for_known_criteria()
    {
        var errors = RequestRules.ValidateFormat(Valid() with { PriorityScores = new Dictionary<string, int> { ["impact"] = 6, ["unknown"] = 3 } }, Context);
        errors.Select(e => (e.Field, e.Code)).ShouldBe([("priorityScores.impact", "validation.inclusiveBetween"), ("priorityScores.unknown", "validation.unknownCode")],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("2027-01-15", "2027-03-10", 3)]
    [InlineData("2027-01-01", "2028-12-31", 24)]
    public void Time_frame_in_months(string start, string end, int months) =>
        RequestRules.TimeFrameMonths(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(months);
}

/// <summary>The request state machine (docs/design/portfolio.md §6): every allowed pair and every forbidden one.</summary>
public class RequestLifecycleTests
{
    private static readonly (string From, string Action, string To)[] Expected =
    [
        (RequestStatuses.Draft, RequestActions.Edit, RequestStatuses.Draft),
        (RequestStatuses.Draft, RequestActions.Submit, RequestStatuses.InApproval),
        (RequestStatuses.Draft, RequestActions.Cancel, RequestStatuses.Cancelled),
        (RequestStatuses.Draft, RequestActions.Classify, RequestStatuses.Draft),
        (RequestStatuses.Draft, RequestActions.Procure, RequestStatuses.Draft),
        (RequestStatuses.InApproval, RequestActions.Approve, RequestStatuses.Approved),
        (RequestStatuses.InApproval, RequestActions.Reject, RequestStatuses.Rejected),
        (RequestStatuses.InApproval, RequestActions.Return, RequestStatuses.Draft),
        (RequestStatuses.InApproval, RequestActions.Cancel, RequestStatuses.Cancelled),
        (RequestStatuses.InApproval, RequestActions.Classify, RequestStatuses.InApproval),
    ];

    [Fact]
    public void Every_pair_is_allowed_or_refused_as_designed()
    {
        foreach (var status in RequestStatuses.All)
        {
            foreach (var action in RequestActions.All)
            {
                var expected = Expected.Where(e => e.From == status && e.Action == action).Select(e => e.To).FirstOrDefault();
                var result = RequestLifecycle.Next(status, action);
                if (expected is null)
                {
                    result.IsFailure.ShouldBeTrue($"{status} + {action} should be refused");
                    result.Error.MessageKey.ShouldBe("portfolio.errors.notAllowedInStatus");
                }
                else
                {
                    result.Value.ShouldBe(expected, $"{status} + {action}");
                }
            }
        }
    }

    [Fact]
    public void Return_keeps_the_request_editable_and_records_the_result()
    {
        var request = ProgramRequest.Create("PR-0001", null, Guid.CreateVersion7(), "manual");
        request.Submit(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        request.EnterStep("pm-office", "مكتب", "PM Office", DateTimeOffset.UtcNow);
        request.Decide(RequestActions.Return, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(RequestStatuses.Draft);
        request.ApprovalStatus.ShouldBe(RequestApprovalResults.Returned);
        request.ApprovalStepCode.ShouldBeNull();
        request.Edit(new RequestValues { NameEn = "x" }, Classifications.Initiative, null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Hand_off_needs_the_project_scope_and_happens_once()
    {
        var request = ProgramRequest.Create("PR-0002", null, Guid.CreateVersion7(), "manual");
        request.Edit(new RequestValues { RequiresBudget = true, CostPath = CostPaths.ProjectScope }, Classifications.Initiative, null, null);
        request.HandOff(DateTimeOffset.UtcNow).Error!.Code.ShouldBe("portfolio.request.scopeMissing");
        request.SetFileFlags(false, true);
        request.HandOff(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        request.HandOff(DateTimeOffset.UtcNow).Error!.Code.ShouldBe("portfolio.request.alreadyHandedOff");
        request.ConfirmQuotations(DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        request.ProcurementStatus.ShouldBe(ProcurementStatuses.QuotationsUploaded);
    }

    [Fact]
    public void Project_copies_the_request_with_the_approved_budget_and_starts_by_classification()
    {
        var request = ProgramRequest.Create("PR-0003", Guid.CreateVersion7(), Guid.CreateVersion7(), "document");
        request.Edit(RequestRulesTests.Valid(), Classifications.Project, 0.65m, "medium");
        request.Submit(DateTimeOffset.UtcNow);
        request.SetApprovedBudget(90_000m).IsSuccess.ShouldBeTrue();
        request.Decide(RequestActions.Approve, DateTimeOffset.UtcNow);
        var owner = Guid.CreateVersion7();
        var project = Project.FromRequest("PROJ-SPMO-2026-001", request, owner);
        project.Classification.ShouldBe(Classifications.Project);
        project.Stage.ShouldBe(ProjectStages.Initiation);
        project.Budget.ShouldBe(90_000m);
        project.OwnerEmployeeId.ShouldBe(owner);
        project.PlannedEnd.ShouldBe(new DateOnly(2028, 12, 31));

        request.OverrideClassification(null, null, null, DateTimeOffset.UtcNow).IsFailure.ShouldBeTrue(); // decided requests are locked
        var initiative = ProgramRequest.Create("PR-0004", null, Guid.CreateVersion7(), "manual");
        initiative.Edit(new RequestValues { RequiresBudget = false }, Classifications.Initiative, null, null);
        Project.FromRequest("PROJ-X-2026-002", initiative, null).Stage.ShouldBe(ProjectStages.Planning); // Q-P2
    }
}

/// <summary>Settings → Portfolio validation and the assessment post-processing (the model is never trusted).</summary>
public class PortfolioSettingsAndAssessmentTests
{
    [Fact]
    public void Default_settings_are_valid_and_broken_ones_are_refused()
    {
        PortfolioSettings.Validate(new PortfolioSettings()).ShouldBeEmpty();
        var broken = new PortfolioSettings
        {
            ThresholdAmount = -1,
            StartYearsAhead = 11,
            PriorityCriteria = [new("a", "أ", "A", 0), new("a", "أ", "A", 1)],
            PriorityBands = [new("low", "م", "Low", 0.1m), new("high", "ع", "High", 0.1m)],
        };
        PortfolioSettings.Validate(broken).Select(e => e.Field).ShouldBe(
            ["thresholdAmount", "startYearsAhead", "priorityCriteria[0].weight", "priorityCriteria", "priorityBands", "priorityBands"], ignoreOrder: true);
    }

    [Fact]
    public void Assessment_drops_unknown_codes_clamps_scores_and_recomputes_the_priority()
    {
        var output = JsonDocument.Parse("""
            {"summary":"S","gaps":["No risks"],"strengths":["Clear scope"],"risks":[],"recommendations":["Add KPIs"],
             "alignment":{"objectives":[{"code":"obj","strength":"strong","reason":"r"},{"code":"invented","strength":"strong"},{"code":"obj","strength":"weak"}],"summary":"A"},
             "suggestedClassification":{"value":"programme","reasoning":"x"},
             "suggestedPriority":{"scores":{"vision-alignment":9,"impact":4,"complexity":4,"radical-change":4,"unknown":5},"reasoning":"p"}}
            """).RootElement;
        var objectives = new List<LookupValueReference> { new(Guid.CreateVersion7(), "obj", "هدف", "Objective", 1, true, null) };
        var content = AssessmentService.PostProcess(output, objectives, new PortfolioSettings(), Classifications.Initiative, []);
        content.Alignment.Objectives.Single().Code.ShouldBe("obj");
        content.Alignment.Objectives.Single().LabelEn.ShouldBe("Objective");
        content.SuggestedClassification.Value.ShouldBeNull(); // not a known classification
        content.SuggestedClassification.Computed.ShouldBe(Classifications.Initiative);
        content.SuggestedPriority.Scores.ShouldBe(new Dictionary<string, int> { ["vision-alignment"] = 5, ["impact"] = 4, ["complexity"] = 4, ["radical-change"] = 4 });
        content.SuggestedPriority.Score.ShouldBe(0.85m);
        content.SuggestedPriority.Band.ShouldBe("high");
        content.Gaps.ShouldBe(["No risks"]);
    }
}
