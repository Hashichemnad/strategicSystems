using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Time;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>
/// Test-only volume seed for the executive dashboard performance check (Phase 17: "under 3 s on 500 projects, 1 000 KPIs …").
/// <para>
/// Every record is built in memory through the modules' own domain factories and transitions (requests submitted and approved, projects driven
/// through their lifecycle, milestone updates approved, KPI results opened / submitted / approved with the central PerformanceCalculator, plans
/// aggregated with the AnnualAggregator), so the reporting sources and dashboards compute on valid data. The rows are then written with
/// PostgreSQL binary COPY, column by column from the EF Core model of each module DbContext. This deliberately bypasses the audit interceptor
/// (CLAUDE.md §8): it is set-up of a throw-away test database, never a business write path. The audit columns are stamped by hand.
/// </para>
/// </summary>
internal static class PortfolioKpiVolume
{
    /// <summary>Quarterly results of these cards are approved (index % 10 below this), pending approval (== this) or still open.</summary>
    private const int ApprovedOutOfTen = 8;

    // firstProjectMilestones: milestones of the first project (the others get five); the Gantt performance check uses 30.
    public static async Task SeedAsync(
        ApiFactory factory, IReadOnlyList<Guid> unitIds, int projects = 500, int kpiCards = 1000, int firstProjectMilestones = 5, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(unitIds);
        if (unitIds.Count == 0)
        {
            throw new ArgumentException("At least one unit is needed.", nameof(unitIds));
        }

        await using var scope = TestAccess.SystemScope(factory);
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var run = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();

        var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (requests, projectRows, milestones) = BuildPortfolio(run, unitIds, projects, firstProjectMilestones, now, today);
        await CopyAsync(portfolio, requests, now, ct);
        await CopyAsync(portfolio, projectRows, now, ct);
        await CopyAsync(portfolio, milestones, now, ct);

        var kpi = scope.ServiceProvider.GetRequiredService<KpiDbContext>();
        var (elements, cards, plans, results) = BuildKpi(run, unitIds, kpiCards, now, today);
        await CopyAsync(kpi, elements, now, ct);
        await CopyAsync(kpi, cards, now, ct);
        await CopyAsync(kpi, plans, now, ct);
        await CopyAsync(kpi, results, now, ct);
    }

    // ---- Portfolio ---------------------------------------------------------------------------------------------------------------------------

    private static (List<ProgramRequest>, List<Project>, List<Milestone>) BuildPortfolio(
        string run, IReadOnlyList<Guid> units, int count, int firstProjectMilestones, DateTimeOffset now, DateOnly today)
    {
        var requests = new List<ProgramRequest>(count);
        var projects = new List<Project>(count);
        var milestones = new List<Milestone>(count * 5);
        string[] lifecycle =
        [
            ProjectActions.TakeOwnership, ProjectActions.SubmitCharter, ProjectActions.CharterApproved, ProjectActions.LpoUploaded, ProjectActions.TeamFormed,
            ProjectActions.SubmitPlanning, ProjectActions.PlanningApproved, ProjectActions.AchievementComplete, ProjectActions.ConfirmCompletion,
            ProjectActions.SubmitClosure, ProjectActions.ClosureApproved, ProjectActions.SubmitImpact, ProjectActions.ImpactApproved,
        ];

        for (var i = 0; i < count; i++)
        {
            var unit = units[i % units.Count];
            var initiative = i % 4 == 0;
            var budget = 50_000m + (i % 40 * 25_000m);
            var start = today.AddMonths(-(i % 12)).AddDays(-(i % 17));
            var end = start.AddMonths(12 + (i % 13));
            var request = ProgramRequest.Create(N("VR", run, i), null, null, "manual");
            request.Edit(new RequestValues
            {
                NameAr = "برنامج " + i.ToString(CultureInfo.InvariantCulture),
                NameEn = "Programme " + i.ToString(CultureInfo.InvariantCulture),
                RequiresBudget = true,
                EstimatedBudget = budget,
                Year1Cost = budget,
                ExpectedStartDate = start,
                ExpectedEndDate = end,
                UnitId = unit,
                ProgramNature = ProgramNatures.New,
            }, initiative ? Classifications.Initiative : Classifications.Project, 0.5m + (i % 5 * 0.1m), null).IsSuccess.ShouldBeTrue();
            request.Submit(now).IsSuccess.ShouldBeTrue();
            request.Decide(RequestActions.Approve, now).IsSuccess.ShouldBeTrue();
            requests.Add(request);

            var template = initiative ? StageTemplate.InitiativeDefault : StageTemplate.ProjectDefault;
            var project = Project.FromRequest(N("VP", run, i), request, null, template);

            // Mix of lifecycles: 20 % still in initiation / ownership, 10 % planning, 50 % execution (a third of them delayed), 10 % completed,
            // then on hold and cancelled ones taken from execution.
            var kind = i % 10;
            var target = kind switch
            {
                0 or 1 => SubStates.Ownership,
                2 => SubStates.PlanningDraft,
                7 => SubStates.Done,
                _ => SubStates.Execution,
            };
            if (target != SubStates.Ownership)
            {
                project.SetPm(Guid.CreateVersion7(), now);
            }

            foreach (var action in lifecycle)
            {
                if (project.SubState == target)
                {
                    break;
                }

                if (ProjectLifecycle.Allows(project.SubState, action))
                {
                    project.Apply(action, template, now).IsSuccess.ShouldBeTrue($"{project.SubState} × {action}");
                }
            }

            project.SubState.ShouldBe(target);
            var elapsed = Math.Clamp((decimal)(today.DayNumber - start.DayNumber) / Math.Max(1, end.DayNumber - start.DayNumber), 0m, 1m);
            var planned = Math.Round(elapsed, 4);
            if (project.SubState == SubStates.Execution)
            {
                var delayed = i % 3 == 0;
                project.SetProgress(Math.Round(delayed ? planned * 0.5m : Math.Min(1m, planned + 0.02m), 4), planned,
                    delayed ? ProgressCalculator.Delayed : ProgressCalculator.OnTrack);
            }
            else if (project.Status == ProjectStatuses.Completed)
            {
                project.SetProgress(1m, 1m, ProgressCalculator.OnTrack);
            }

            if (kind == 8)
            {
                project.ApplyStatus(ProjectStatusActions.Hold, "Volume seed", now).IsSuccess.ShouldBeTrue();
            }
            else if (kind == 9)
            {
                project.ApplyStatus(ProjectStatusActions.Cancel, "Volume seed", now).IsSuccess.ShouldBeTrue();
            }

            projects.Add(project);

            // Five leaf milestones (weight 0.2 each) around today: done, overdue, due soon, upcoming and later.
            var milestoneCount = i == 0 ? Math.Max(1, firstProjectMilestones) : 5;
            var weight = Math.Round(1m / milestoneCount, 4);
            for (var k = 0; k < milestoneCount; k++)
            {
                var due = milestoneCount == 5 ? today.AddDays(((k - 2) * 30) + (i % 20)) : today.AddDays(((k - (milestoneCount / 2)) * 14) + (i % 20));
                var milestone = Milestone.Create(project.Id, $"{project.Number}-{k + 1}", new MilestoneValues(
                    "مرحلة " + (k + 1).ToString(CultureInfo.InvariantCulture), "Milestone " + (k + 1).ToString(CultureInfo.InvariantCulture), null, false, false,
                    due.AddDays(-45), due, weight, Math.Round(budget / milestoneCount, 2), k == milestoneCount - 1, null, null, null, false, null, k));
                if (project.Status == ProjectStatuses.Completed || (k == 0 && project.SubState == SubStates.Execution))
                {
                    milestone.RecordProgress(new MilestoneProgressValues(weight, due.AddDays(-45), due.AddDays(-(i % 3)), null, null, null));
                    milestone.SubmitUpdate();
                    milestone.ApproveUpdate();
                }

                milestones.Add(milestone);
            }
        }

        return (requests, projects, milestones);
    }

    // ---- KPI ---------------------------------------------------------------------------------------------------------------------------------

    private static (List<StrategyElement>, List<KpiCard>, List<AnnualPlan>, List<KpiResult>) BuildKpi(
        string run, IReadOnlyList<Guid> units, int count, DateTimeOffset now, DateOnly today)
    {
        var year = today.Year;
        var elements = new List<StrategyElement>();
        var vision = Element(ElementKinds.Vision, $"VV-{run}", null, 1);
        elements.Add(vision);
        var objectives = new List<StrategyElement>();
        for (var p = 0; p < 3; p++)
        {
            var perspective = Element(ElementKinds.Perspective, $"VPE-{run}-{p + 1}", vision.Id, p + 1);
            elements.Add(perspective);
            for (var o = 0; o < 2; o++)
            {
                var objective = Element(ElementKinds.Objective, $"VO-{run}-{p + 1}{o + 1}", perspective.Id, o + 1);
                elements.Add(objective);
                objectives.Add(objective);
            }
        }

        var cards = new List<KpiCard>(count);
        var plans = new List<AnnualPlan>(count);
        var results = new List<KpiResult>(count * 4);
        string[] types = ["strategic", "operational", "regulatory"];
        for (var j = 0; j < count; j++)
        {
            var unit = units[j % units.Count];
            var card = KpiCard.Create(N("VK", run, j));
            card.Edit(new CardValues(
                NameAr: "مؤشر " + j.ToString(CultureInfo.InvariantCulture), NameEn: "KPI " + j.ToString(CultureInfo.InvariantCulture), DescriptionAr: null,
                DescriptionEn: null, MeasuringEquation: "count", ProcessName: null, InitiatedOn: new DateOnly(year, 1, 1), TypeCode: types[j % types.Length],
                IndexClassificationCode: null, EffectivenessCode: null, Nature: Natures.Quantitative, SourceCodes: [], MeasuringMethodCode: AggregationRules.Average,
                CompetitivenessIndicator: false, IsGlobalStar: false, UnitCode: "number", MeasuringPeriod: MeasuringPeriods.Quarterly,
                Direction: Directions.Increase, TargetType: TargetTypes.SingleValue, TargetSettingMechanism: null, AllowsNegative: false,
                ObjectiveId: objectives[j % objectives.Count].Id, SectorGoal: null, NationalGoalCode: null, VisionLinkCode: null, GemCriteriaCodes: [],
                CriteriaText: null, ExcellenceLink: null, DepartmentUnitId: unit, ResponsibleUnitId: unit, ResponsibleEmployeeId: null, ResponsibleTeamId: null,
                BenchmarkDescription: null, BenchmarkResult: null, Level: CardLevels.Department, ParentCardId: null, Weight: 1 + (j % 3), BaselineValue: null,
                BaselineYear: null, GreenFrom: null, AmberFrom: null, Formula: null, DataElements: [], Scale: []));
            cards.Add(card);

            var plan = AnnualPlan.Create(card.Id, year, MeasuringPeriods.Quarterly, "number", Directions.Increase, false, null, unit);
            plan.SetTarget(TargetTypes.SingleValue, 100m, null, null);
            plan.Confirm(now, null);
            plans.Add(plan);

            // Green (≥ 95 %), amber (≥ 80 %) and red results; approved for the ended quarters, the last one pending for some cards.
            var achieve = (j % 3) switch { 0 => 100m, 1 => 85m, _ => 60m };
            var values = new List<PeriodValue>();
            var ended = PeriodSchedule.For(year, MeasuringPeriods.Quarterly).Where(s => s.End < today).ToList();
            foreach (var spec in PeriodSchedule.For(year, MeasuringPeriods.Quarterly))
            {
                var result = KpiResult.Create(plan, spec, new PeriodTarget(100m, null, null), null, unit);
                var due = spec.End.AddDays(15);
                result.SetWindow(spec.End.AddDays(1), due, new DateTimeOffset(due.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero));
                if (spec.End < today)
                {
                    result.Apply(ResultActions.OpenWindow, now).IsSuccess.ShouldBeTrue();
                    var pending = j % 10 == ApprovedOutOfTen && ended.Count > 0 && spec == ended[^1];
                    var open = j % 10 > ApprovedOutOfTen && ended.Count > 0 && spec == ended[^1];
                    if (!open)
                    {
                        var performance = PerformanceCalculator.Calculate(TargetTypes.SingleValue, Directions.Increase, 100m, null, null, achieve);
                        result.SaveEntry(new ResultEntry(achieve, null, null, achieve < 100 ? "Volume seed" : null, achieve < 100 ? "Volume seed" : null, null,
                            false, null), achieve, performance);
                        result.Apply(ResultActions.Submit, now).IsSuccess.ShouldBeTrue();
                        if (!pending)
                        {
                            result.Apply(ResultActions.Approved, now).IsSuccess.ShouldBeTrue();
                        }
                    }
                }
                else if (spec.Start <= today)
                {
                    result.Apply(ResultActions.OpenWindow, now).IsSuccess.ShouldBeTrue();
                }

                values.Add(new PeriodValue(result.Target, null, null, result.Achieve, false, result.Status == ResultStatuses.Approved));
                results.Add(result);
            }

            var aggregate = AnnualAggregator.Aggregate(AggregationRules.Average, TargetTypes.SingleValue, values, 100m, null, null);
            var annual = AnnualAggregator.Performance(AggregationRules.Average, TargetTypes.SingleValue, Directions.Increase, values, 100m, null, null);
            plan.SetComputed(aggregate, annual, RagBands.Band(annual.Capped), values.Count(v => v.Achieve is not null), values.Count(v => v.Approved), 0);
        }

        return (elements, cards, plans, results);
    }

    private static StrategyElement Element(string kind, string code, Guid? parent, int sort)
    {
        var element = StrategyElement.Create(kind, code);
        element.Edit(code, "عنصر " + code, "Element " + code, null, null, parent, 1m, sort, true, null, null);
        return element;
    }

    private static string N(string prefix, string run, int i) => string.Create(CultureInfo.InvariantCulture, $"{prefix}-{run}-{i:D5}");

    // ---- binary COPY from the EF Core model (test-only) ---------------------------------------------------------------------------------------

    /// <summary>Writes <paramref name="rows"/> to the entity's table with binary COPY: every mapped, store-written column, converters applied.</summary>
    private static async Task CopyAsync<T>(DbContext db, IReadOnlyList<T> rows, DateTimeOffset now, CancellationToken ct)
        where T : class
    {
        if (rows.Count == 0)
        {
            return;
        }

        var entity = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var columns = entity.GetProperties()
            .Where(p => p.GetColumnName(table) is not null && p.GetComputedColumnSql() is null && p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate)
            .Select(p => (Property: p, Column: p.GetColumnName(table)!, Type: Regex.Replace(p.GetColumnType(), @"\(.*?\)", string.Empty).Trim(),
                Converter: p.GetTypeMapping().Converter, Getter: p.GetGetter()))
            .ToList();
        foreach (var row in rows)
        {
            if (row is IAuditable auditable)
            {
                auditable.MarkCreated(now, null);
            }
        }

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var sql = $"COPY \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" ({string.Join(", ", columns.Select(c => $"\"{c.Column}\""))}) FROM STDIN (FORMAT BINARY)";
        await using var writer = await connection.BeginBinaryImportAsync(sql, ct);
        foreach (var row in rows)
        {
            await writer.StartRowAsync(ct);
            foreach (var column in columns)
            {
                var value = column.Getter.GetClrValue(row);
                if (value is not null && column.Converter is not null)
                {
                    value = column.Converter.ConvertToProvider(value);
                }

                if (value is null)
                {
                    await writer.WriteNullAsync(ct);
                }
                else
                {
                    await writer.WriteAsync(value, column.Type, ct);
                }
            }
        }

        await writer.CompleteAsync(ct);
    }
}
