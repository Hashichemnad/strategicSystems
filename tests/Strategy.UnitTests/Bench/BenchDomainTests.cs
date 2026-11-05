using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;

namespace Strategy.UnitTests.Bench;

/// <summary>The study stage machine (docs/design/benchmarking.md §2): every allowed and every forbidden transition.</summary>
public class StudyLifecycleTests
{
    public static TheoryData<string, bool, string, string?> Table()
    {
        var allowed = new Dictionary<(string, bool, string), string>
        {
            [(StudyStages.Plan, false, StudyActions.PlanApproved)] = StudyStages.Search,
            [(StudyStages.Search, false, StudyActions.SubmitSearch)] = StudyStages.Search,
            [(StudyStages.Search, false, StudyActions.SearchApproved)] = StudyStages.Acquire,
            [(StudyStages.Search, false, StudyActions.SearchReturned)] = StudyStages.Search,
            [(StudyStages.Acquire, false, StudyActions.SendToAnalysis)] = StudyStages.Analysis,
            [(StudyStages.Analysis, false, StudyActions.SubmitAnalysis)] = StudyStages.Analysis,
            [(StudyStages.Analysis, false, StudyActions.AnalysisApproved)] = StudyStages.Analysis,
            [(StudyStages.Analysis, false, StudyActions.AnalysisReturned)] = StudyStages.Analysis,
            [(StudyStages.Analysis, true, StudyActions.RequestClosure)] = StudyStages.Analysis,
            [(StudyStages.Analysis, true, StudyActions.ClosureApproved)] = StudyStages.Completed,
            [(StudyStages.Analysis, true, StudyActions.ClosureRefused)] = StudyStages.Analysis,
        };
        var data = new TheoryData<string, bool, string, string?>();
        foreach (var stage in StudyStages.All)
        {
            foreach (var approved in new[] { false, true })
            {
                foreach (var action in StudyActions.All)
                {
                    data.Add(stage, approved, action, allowed.GetValueOrDefault((stage, approved, action)));
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Transition(string stage, bool analysisApproved, string action, string? to)
    {
        StudyLifecycle.Next(stage, analysisApproved, action).ShouldBe(to);
        StudyLifecycle.Allows(stage, analysisApproved, action).ShouldBe(to is not null);
    }

    [Fact]
    public void A_cancelled_study_accepts_no_action()
    {
        var study = Study.Create("BMS-0001", Guid.NewGuid(), "BMREQ-1");
        study.Apply(StudyActions.PlanApproved, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        study.Cancel("merged");
        study.Apply(StudyActions.SubmitSearch, DateTimeOffset.UtcNow).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Closure_completes_the_study_and_sets_the_impact_date()
    {
        var study = Study.Create("BMS-0002", Guid.NewGuid(), null);
        foreach (var a in new[] { StudyActions.PlanApproved, StudyActions.SubmitSearch, StudyActions.SearchApproved, StudyActions.SendToAnalysis, StudyActions.SubmitAnalysis,
                     StudyActions.AnalysisApproved, StudyActions.RequestClosure })
        {
            study.Apply(a, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue(a);
        }

        study.Apply(StudyActions.ClosureApproved, DateTimeOffset.UtcNow, impactDue: new DateOnly(2027, 6, 1)).IsSuccess.ShouldBeTrue();
        study.Status.ShouldBe(StudyStatuses.Completed);
        study.Stage.ShouldBe(StudyStages.Completed);
        study.ImpactDueOn.ShouldBe(new DateOnly(2027, 6, 1));
    }
}

public class QuartersTests
{
    [Theory]
    [InlineData(2027, 1, 2027, 1, 1)]
    [InlineData(2027, 2, 2027, 4, 1)]
    [InlineData(2027, 3, 2027, 7, 1)]
    [InlineData(2027, 4, 2027, 10, 1)]
    public void Quarter_start(int year, int quarter, int y, int m, int d) => Quarters.Start(year, quarter).ShouldBe(new DateOnly(y, m, d));

    [Fact]
    public void Quarter_end_and_of()
    {
        Quarters.End(2027, 1).ShouldBe(new DateOnly(2027, 3, 31));
        Quarters.End(2027, 4).ShouldBe(new DateOnly(2027, 12, 31));
        Quarters.Of(new DateOnly(2027, 5, 15)).ShouldBe(2);
    }

    [Fact]
    public void Settings_defaults_and_ranges()
    {
        var s = new BenchSettings();
        s.SearchNoticeDays.ShouldBe(30);
        s.ImpactFollowUpMonths.ShouldBe(6);
        BenchSettings.Validate(s).ShouldBeEmpty();
        BenchSettings.Validate(new BenchSettings { SearchNoticeDays = 400 }).ShouldContain(e => e.Field == "searchNoticeDays");
    }
}
