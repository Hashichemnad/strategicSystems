using System.Security.Claims;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.ReportTypes;

namespace Strategy.UnitTests.Reporting;

/// <summary>Reporting 2.0 pure parts (ADRs 0181, 0182): joins in the reporting layer, grouping / subtotals / summaries / charts / highlights, relative dates, type rules.</summary>
public sealed class ReportTypeEngineTests
{
    private static NodeRow Row(Guid? id, params (string Field, object? Value)[] values) =>
        new(values.ToDictionary(v => v.Field, v => v.Value, StringComparer.Ordinal), id);

    private static readonly Guid P1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid P2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid P3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

    /// <summary>Worked example: three projects (P1, P2 in unit A; P3 in unit B), milestones M1 (P1, 100, open), M2 (P1, 300, done), M3 (P2, 50, open); P3 has none.</summary>
    private static (List<NodeRow> Projects, List<NodeRow> Milestones) Example()
    {
        var projects = new List<NodeRow>
        {
            Row(P1, ("id", P1), ("number", "P1"), ("unit", "A")),
            Row(P2, ("id", P2), ("number", "P2"), ("unit", "A")),
            Row(P3, ("id", P3), ("number", "P3"), ("unit", "B")),
        };
        var milestones = new List<NodeRow>
        {
            Row(P1, ("projectId", P1), ("code", "M1"), ("cost", 100m), ("status", "open"), ("end", new DateOnly(2026, 3, 1))),
            Row(P1, ("projectId", P1), ("code", "M2"), ("cost", 300m), ("status", "done"), ("end", new DateOnly(2026, 6, 1))),
            Row(P2, ("projectId", P2), ("code", "M3"), ("cost", 50m), ("status", "open"), ("end", new DateOnly(2026, 1, 15))),
        };
        return (projects, milestones);
    }

    private static List<FlatRow> Compose(string join, int limit = 100)
    {
        var (projects, milestones) = Example();
        return ReportComposer.Compose(
            [new ComposerNode("a", null, JoinKinds.Outer, null, null, projects), new ComposerNode("b", "a", join, "id", "projectId", milestones)], limit, out _);
    }

    [Fact]
    public void Outer_join_keeps_a_parent_without_related_rows_and_inner_join_drops_it()
    {
        var outer = Compose(JoinKinds.Outer);
        outer.Select(r => (r.Value("a.number"), r.Value("b.code"))).ShouldBe([("P1", "M1"), ("P1", "M2"), ("P2", "M3"), ("P3", null)]);

        var inner = Compose(JoinKinds.Inner);
        inner.Select(r => (r.Value("a.number"), r.Value("b.code"))).ShouldBe([("P1", "M1"), ("P1", "M2"), ("P2", "M3")]);
    }

    [Fact]
    public void Related_rows_the_module_did_not_return_never_join()
    {
        // The module returned only M3 for the runner (M1 / M2 are out of their scope): P1 shows no milestones (outer) or disappears (inner).
        var (projects, milestones) = Example();
        var visible = milestones.Where(m => (string)m["code"]! == "M3").ToList();
        var inner = ReportComposer.Compose(
            [new ComposerNode("a", null, JoinKinds.Outer, null, null, projects), new ComposerNode("b", "a", JoinKinds.Inner, "id", "projectId", visible)], 100, out _);
        inner.Select(r => r.Value("a.number")).ShouldBe(["P2"]);
    }

    [Fact]
    public void Siblings_are_listed_one_after_the_other_not_multiplied_and_grandchildren_follow_their_parent()
    {
        var (projects, milestones) = Example();
        var risks = new List<NodeRow> { Row(P1, ("projectId", P1), ("title", "R1")), Row(P1, ("projectId", P1), ("title", "R2")) };
        var tasks = new List<NodeRow> { Row(null, ("linkedId", "M1"), ("task", "T1")), Row(null, ("linkedId", "M1"), ("task", "T2")) };
        var milestonesWithKey = milestones.Select(m => Row(m.RowId, [.. m.Values.Select(v => (v.Key, v.Value)), ("key", m["code"])])).ToList();
        var rows = ReportComposer.Compose(
        [
            new ComposerNode("a", null, JoinKinds.Outer, null, null, projects.Take(1).ToList()),
            new ComposerNode("b", "a", JoinKinds.Outer, "id", "projectId", milestonesWithKey),
            new ComposerNode("c", "b", JoinKinds.Outer, "key", "linkedId", tasks),
            new ComposerNode("d", "a", JoinKinds.Outer, "id", "projectId", risks),
        ], 100, out var truncated);

        // P1: M1 × (T1, T2), M2 (no task), then R1, R2 — 5 rows, not 3 × 2.
        truncated.ShouldBeFalse();
        rows.Select(r => (r.Value("b.code"), r.Value("c.task"), r.Value("d.title")))
            .ShouldBe([("M1", "T1", null), ("M1", "T2", null), ("M2", null, null), (null, null, "R1"), (null, null, "R2")]);
    }

    [Fact]
    public void Composition_stops_at_the_row_limit_and_says_so()
    {
        var (projects, milestones) = Example();
        var rows = ReportComposer.Compose(
            [new ComposerNode("a", null, JoinKinds.Outer, null, null, projects), new ComposerNode("b", "a", JoinKinds.Outer, "id", "projectId", milestones)], 2, out var truncated);
        rows.Count.ShouldBe(2);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void Worked_example_two_level_grouping_gives_correct_subtotals_summaries_and_grand_total()
    {
        var rows = ReportAggregation.Sort(Compose(JoinKinds.Inner), ["a.unit", "a.number"], []);
        var summaries = new List<ReportSummary>
        {
            new() { Column = "b.cost", Functions = [SummaryFunctions.Sum, SummaryFunctions.Average, SummaryFunctions.Min, SummaryFunctions.Max] },
            new() { Column = "b.end", Functions = [SummaryFunctions.Max] },
        };
        var groups = ReportAggregation.Groups(rows, ["a.unit", "a.number"], summaries);

        // Tree order: A (3 rows: 450), A/P1 (2: 400), A/P2 (1: 50).
        groups.Select(g => (g.Level, string.Join('/', g.Keys), g.Count)).ShouldBe([(1, "A", 3), (2, "A/P1", 2), (2, "A/P2", 1)]);
        decimal Agg(GroupNode g, string column, string fn) => (decimal)g.Aggregates.Single(a => a.Column == column && a.Function == fn).Value!;
        Agg(groups[0], "b.cost", SummaryFunctions.Sum).ShouldBe(450m);
        Agg(groups[0], "b.cost", SummaryFunctions.Average).ShouldBe(150m);
        Agg(groups[0], "b.cost", SummaryFunctions.Min).ShouldBe(50m);
        Agg(groups[0], "b.cost", SummaryFunctions.Max).ShouldBe(300m);
        Agg(groups[1], "b.cost", SummaryFunctions.Sum).ShouldBe(400m);
        Agg(groups[1], "b.cost", SummaryFunctions.Average).ShouldBe(200m);
        Agg(groups[2], "b.cost", SummaryFunctions.Sum).ShouldBe(50m);
        groups[1].Aggregates.Single(a => a.Column == "b.end").Value.ShouldBe(new DateOnly(2026, 6, 1));

        var total = ReportAggregation.Aggregate(rows, summaries);
        ((decimal)total.First(a => a.Function == SummaryFunctions.Sum).Value!).ShouldBe(450m);
    }

    [Fact]
    public void Three_grouping_levels_nest_in_tree_order()
    {
        var rows = ReportAggregation.Sort(Compose(JoinKinds.Inner), ["a.unit", "b.status", "a.number"], []);
        var groups = ReportAggregation.Groups(rows, ["a.unit", "b.status", "a.number"], []);
        groups.Select(g => string.Join('/', g.Keys) + ":" + g.Count).ShouldBe(["A:3", "A/done:1", "A/done/P1:1", "A/open:2", "A/open/P1:1", "A/open/P2:1"]);
    }

    [Fact]
    public void Charts_count_or_aggregate_per_category_and_stack_per_series()
    {
        var rows = Compose(JoinKinds.Inner);
        var count = ReportAggregation.Chart(rows, new ReportChart { Type = ChartTypes.Bar, Category = "a.number" });
        count.Categories.ShouldBe(["P1", "P2"]);
        count.Series.Single().Values.ShouldBe([2m, 1m]);

        var sum = ReportAggregation.Chart(rows, new ReportChart { Type = ChartTypes.Pie, Category = "a.number", Measure = "b.cost", Aggregate = SummaryFunctions.Sum });
        sum.Series.Single().Values.ShouldBe([400m, 50m]);

        var stacked = ReportAggregation.Chart(rows, new ReportChart { Type = ChartTypes.StackedBar, Category = "a.number", Series = "b.status", Measure = "b.cost" });
        stacked.Series.Select(s => s.Key).ShouldBe(["done", "open"]);
        stacked.Series[0].Values.ShouldBe([300m, 0m]);
        stacked.Series[1].Values.ShouldBe([100m, 50m]);
    }

    [Fact]
    public void Highlights_apply_the_first_matching_rule_on_raw_values()
    {
        List<ReportHighlight> rules =
        [
            new() { Column = "b.cost", Operator = ReportOperators.GreaterOrEqual, Values = ["300"], Tone = "bad" },
            new() { Column = "b.cost", Operator = ReportOperators.Between, Values = ["60", "299"], Tone = "warning" },
            new() { Column = "b.status", Operator = ReportOperators.Equal, Values = ["done"], Tone = "good" },
        ];
        ReportAggregation.Tone(300m, "b.cost", rules).ShouldBe("bad");
        ReportAggregation.Tone(100m, "b.cost", rules).ShouldBe("warning");
        ReportAggregation.Tone(50m, "b.cost", rules).ShouldBeNull();
        ReportAggregation.Tone("done", "b.status", rules).ShouldBe("good");
        ReportAggregation.Tone(null, "b.status", rules).ShouldBeNull();
        ReportAggregation.Matches(new DateOnly(2026, 3, 1), ReportOperators.LessOrEqual, ["2026-03-31"]).ShouldBeTrue();
    }

    [Fact]
    public void Sorting_puts_empty_values_last_and_keeps_the_composition_order_for_ties()
    {
        var rows = Compose(JoinKinds.Outer);
        var sorted = ReportAggregation.Sort(rows, [], [new ReportSort("b.cost", true)]);
        sorted.Select(r => r.Value("b.code")).ShouldBe(["M2", "M1", "M3", null]);
    }

    [Theory]
    [InlineData("today", "2026-11-11", "2026-11-11")]
    [InlineData("yesterday", "2026-11-10", "2026-11-10")]
    [InlineData("thisWeek", "2026-11-09", "2026-11-15")]
    [InlineData("lastWeek", "2026-11-02", "2026-11-08")]
    [InlineData("thisMonth", "2026-11-01", "2026-11-30")]
    [InlineData("lastMonth", "2026-10-01", "2026-10-31")]
    [InlineData("nextMonth", "2026-12-01", "2026-12-31")]
    [InlineData("thisQuarter", "2026-10-01", "2026-12-31")]
    [InlineData("lastQuarter", "2026-07-01", "2026-09-30")]
    [InlineData("nextQuarter", "2027-01-01", "2027-03-31")]
    [InlineData("thisYear", "2026-01-01", "2026-12-31")]
    [InlineData("lastYear", "2025-01-01", "2025-12-31")]
    public void Relative_dates_resolve_to_inclusive_ranges(string code, string from, string to)
    {
        var (f, t) = RelativeDates.Resolve([code], new DateOnly(2026, 11, 11), 1);
        f.ShouldBe(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture));
        t.ShouldBe(DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Relative_dates_follow_the_fiscal_year_and_take_a_number_of_days()
    {
        // Fiscal year from April: 11 Nov 2026 is in Q3 (Oct–Dec) of FY 2026/27.
        RelativeDates.Resolve(["thisYear"], new DateOnly(2026, 11, 11), 4).ShouldBe((new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)));
        RelativeDates.Resolve(["lastDays", "30"], new DateOnly(2026, 11, 11), 1).ShouldBe((new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 11)));
        RelativeDates.IsValid(["lastDays"]).ShouldBeFalse();
        RelativeDates.IsValid(["lastDays", "0"]).ShouldBeFalse();
        RelativeDates.IsValid(["soon"]).ShouldBeFalse();
        RelativeDates.IsValid(["nextDays", "7"]).ShouldBeTrue();

        // Date-times: the local days of the installation as UTC instants (UTC+4 → the day starts at 20:00 UTC the day before).
        var dubai = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(4), "t", "t");
        var values = RelativeDates.BetweenValues(DatasetFieldType.DateTime, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11), dubai);
        DateTimeOffset.Parse(values[0], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        DateTimeOffset.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture).ShouldBeLessThan(new DateTimeOffset(2026, 11, 11, 20, 0, 0, TimeSpan.Zero));
        RelativeDates.BetweenValues(DatasetFieldType.Date, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), dubai).ShouldBe(["2026-01-01", "2026-01-31"]);
    }

    private sealed class FakeMetadata : IReportableMetadata
    {
        public IReadOnlyList<DatasetDefinition> Datasets { get; } =
        [
            new("x.projects", "x", "م", "Projects", "x.read", [new("number", "ر", "Number", DatasetFieldType.Text), new("id", "م", "Id", DatasetFieldType.Reference)], "/p/{id}")
            {
                KeyField = "id",
            },
            new("x.milestones", "x", "م", "Milestones", "x.read",
                [new("code", "ر", "Code", DatasetFieldType.Text), new("id", "م", "Id", DatasetFieldType.Reference), new("projectId", "م", "Project", DatasetFieldType.Reference)], "/p/{id}")
            {
                KeyField = "id",
            },
            new("x.tasks", "x", "م", "Tasks", "x.read", [new("title", "ع", "Title", DatasetFieldType.Text), new("linkedId", "م", "Linked", DatasetFieldType.Reference)], "/t/{id}"),
            new("x.history", "x", "س", "History", "x.read", [new("step", "خ", "Step", DatasetFieldType.Text), new("resourceId", "م", "Record", DatasetFieldType.Reference)], string.Empty)
            {
                JoinOnly = true,
            },
        ];

        public IReadOnlyList<DatasetRelationship> Relationships { get; } =
        [
            new("x.project-milestones", "x.projects", "id", "x.milestones", "projectId", "م", "Milestones"),
            new("x.milestone-tasks", "x.milestones", "id", "x.tasks", "linkedId", "م", "Tasks"),
            new("x.project-history", "x.projects", "id", "x.history", "resourceId", "س", "History"),
        ];

        public DatasetDefinition? Dataset(string code) => Datasets.FirstOrDefault(d => d.Code == code);

        public IReadOnlyList<DatasetRelationship> RelationshipsFrom(string parentDataset) => Relationships.Where(r => r.ParentDataset == parentDataset).ToList();

        public Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ReportTypeNode N(string alias, string dataset, string? parent = null, string? relationship = null, string join = JoinKinds.Outer, List<string>? fields = null) =>
        new() { Alias = alias, Dataset = dataset, Parent = parent, Relationship = relationship, Join = join, Fields = fields };

    [Fact]
    public void Report_type_rules_accept_a_valid_tree_and_refuse_every_broken_shape()
    {
        var metadata = new FakeMetadata();
        IReadOnlyList<string> V(params ReportTypeNode[] nodes) => ReportTypeRules.Validate(new ReportTypeDefinition { Nodes = [.. nodes] }, metadata);

        V(N("a", "x.projects"), N("b", "x.milestones", "a", "x.project-milestones"), N("c", "x.tasks", "b", "x.milestone-tasks", JoinKinds.Inner),
            N("d", "x.history", "a", "x.project-history")).ShouldBeEmpty();
        V().ShouldContain("reporting.errors.reportType.primary");
        V(N("a", "x.history")).ShouldContain("reporting.errors.reportType.primary"); // join-only as primary
        V(N("a", "x.projects"), N("b", "x.tasks", "a", "x.milestone-tasks")).ShouldContain("reporting.errors.reportType.relationship"); // wrong parent
        V(N("a", "x.projects"), N("a", "x.milestones", "a", "x.project-milestones")).ShouldContain("reporting.errors.reportType.alias");
        V(N("a", "x.projects"), N("b", "x.milestones", "z", "x.project-milestones")).ShouldContain("reporting.errors.reportType.parent");
        V(N("a", "x.projects"), N("b", "x.milestones", "a", "x.project-milestones", "left")).ShouldContain("reporting.errors.reportType.join");
        V(N("a", "x.projects", fields: ["id"])).ShouldContain("reporting.errors.reportType.fields"); // reference fields are never offered
        V(N("a", "x.projects", fields: ["nope"])).ShouldContain("reporting.errors.reportType.fields");
        V(N("a", "x.unknown")).ShouldContain("reporting.errors.reportType.dataset");
        ReportTypeRules.Split("b.title").ShouldBe(("b", "title"));
        ReportTypeRules.Split("title").ShouldBeNull();
    }

    [Fact]
    public void Exports_list_groups_with_subtotals_details_and_the_grand_total_and_csv_guards_formulas()
    {
        var run = new ReportTypeRunDto("t", "ت", "T",
            [new ReportTypeColumnDto("a.number", "a", "الرقم", "Number", "م", "P", "text"), new ReportTypeColumnDto("b.cost", "b", "التكلفة", "Cost", "م", "M", "money")],
            [
                new ReportTypeRowDto(["=P1", "100.00"], [null, "bad"], new Dictionary<string, string>(), ["A"]),
                new ReportTypeRowDto(["P2", "50.00"], [null, null], new Dictionary<string, string>(), ["A"]),
            ],
            2, 1, 50, false, [], ["a.unit"],
            [new ReportGroupDto(1, ["A"], "Unit A", 2, [new ReportAggregateDto("b.cost", SummaryFunctions.Sum, "150.00", 150m)])],
            new ReportGroupDto(0, [], null, 2, [new ReportAggregateDto("b.cost", SummaryFunctions.Sum, "150.00", 150m)]), null);
        var (headers, lines) = ReportTypeExport.Lines(run, "en");
        headers.ShouldBe(["Number", "Cost"]);
        lines.Select(l => l.Kind).ShouldBe(["group", "row", "row", "total"]);
        lines[0].Cells.ShouldBe(["Unit A (2)", "Sum: 150.00"]);
        lines[^1].Cells[0]!.ShouldStartWith("Grand total (2)");
        lines[1].Tones[1].ShouldBe("bad");

        var csv = System.Text.Encoding.UTF8.GetString(ReportTypeExport.Csv(run, "ar"));
        csv.ShouldStartWith("﻿\"الرقم\",\"التكلفة\"");
        csv.ShouldContain("\"'=P1\""); // never a formula in a spreadsheet
        ReportTypeExport.Excel(run, "ar", "Report").Length.ShouldBeGreaterThan(1000);
    }
}
