using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Portfolio;
using Strategy.IntegrationTests.Tasks;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Dossiers;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// Labels of code fields (ADR 0209, closes the Phase 21 gap "Lookup labels"): every code field of every registered dataset and reportable record type
/// declares where its labels come from, and reports, charts, exports and dossiers show the labels of project stages, statuses, classifications and
/// strategic objectives in Arabic and English — never the codes.
/// </summary>
public sealed class CodeLabelTests(PortfolioFixture fx) : IClassFixture<PortfolioFixture>
{
    private const string ReportType = "portfolio.projects-milestones";
    private const string Dossier = "portfolio.project-dossier";

    private static bool HasArabic(string text) => text.Any(c => c is >= '؀' and <= 'ۿ');

    [Fact]
    public async Task Every_code_field_of_every_dataset_and_record_type_declares_labels_from_a_known_source()
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var services = scope.ServiceProvider;
        var datasets = services.GetServices<IReportingDatasetSource>().SelectMany(s => s.Datasets).ToList();
        var recordTypes = services.GetServices<IReportableRecordSource>().SelectMany(s => s.RecordTypes).ToList();
        var lookupTypes = services.GetServices<LookupTypeDefinition>().Select(l => l.Code).ToHashSet(StringComparer.Ordinal);
        var labelSystems = services.GetServices<IReportingDimensionLabelSource>().Select(s => s.SystemCode).ToHashSet(StringComparer.Ordinal);
        datasets.Select(d => d.SystemCode).Distinct().Count().ShouldBeGreaterThanOrEqualTo(12);
        recordTypes.Select(t => t.SystemCode).Distinct().Count().ShouldBeGreaterThanOrEqualTo(11);

        var failures = new List<string>();
        void Choices(string where, IReadOnlyList<RecordFieldChoice>? choices)
        {
            if (choices is null)
            {
                return;
            }

            if (choices.Count == 0)
            {
                failures.Add($"{where}: an empty choice list");
            }

            failures.AddRange(choices.GroupBy(c => c.Code).Where(g => g.Count() > 1).Select(g => $"{where}: choice '{g.Key}' twice"));
            failures.AddRange(choices.Where(c => string.IsNullOrWhiteSpace(c.Code) || string.IsNullOrWhiteSpace(c.LabelEn) || string.IsNullOrWhiteSpace(c.LabelAr)
                || !HasArabic(c.LabelAr) || c.LabelEn == c.Code).Select(c => $"{where}: choice '{c.Code}' needs an Arabic and an English label"));
        }

        void LookupType(string where, string? type)
        {
            if (type is not null && !lookupTypes.Contains(type))
            {
                failures.Add($"{where}: lookup type '{type}' is not registered");
            }
        }

        foreach (var dataset in datasets)
        {
            foreach (var field in dataset.Fields)
            {
                var where = $"{dataset.Code}.{field.Code}";
                if (field.Type == DatasetFieldType.Lookup && !field.HasCodeLabels)
                {
                    failures.Add($"{where}: a code field without a lookup type, a label source or fixed choices");
                }

                if (field.HasCodeLabels && field.Type is not (DatasetFieldType.Lookup or DatasetFieldType.Text))
                {
                    failures.Add($"{where}: labels on a {field.Type} field");
                }

                LookupType(where, field.LookupType);
                Choices(where, field.Choices);
                if (field.LabelSource is { } source && !labelSystems.Contains(source.SystemCode))
                {
                    failures.Add($"{where}: no label source for system '{source.SystemCode}'");
                }
            }
        }

        foreach (var type in recordTypes)
        {
            foreach (var field in type.Fields)
            {
                var where = $"{type.Code}.{field.Code} (feed)";
                if (field.Type == RecordFieldType.Lookup && field.LookupType is null && field.Choices is not { Count: > 0 })
                {
                    failures.Add($"{where}: a code field without a lookup type or fixed choices");
                }

                LookupType(where, field.LookupType);
                Choices(where, field.Choices);
            }
        }

        failures.ShouldBeEmpty();
    }

    private sealed record Seed(PortfolioWorld World, Guid Project, string ProjectNumber, string InitiativeNumber);

    private Seed? _seed;

    /// <summary>A project (stage Initiation) and an initiative (stage Planning), both active, on the world's strategic objective.</summary>
    private async Task<Seed> SeedAsync()
    {
        if (_seed is not null)
        {
            return _seed;
        }

        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var r1 = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Labelled project", budget: 240_000m)));
        var r2 = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Labelled initiative", budget: 10_000m, allYes: false)));
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var requests = await db.Requests.Where(r => r.Id == r1.Id || r.Id == r2.Id).ToDictionaryAsync(r => r.Id);
        var project = Project.FromRequest($"LBL-{w.Suffix}-1", requests[r1.Id], w.Director.EmployeeId);
        var initiative = Project.FromRequest($"LBL-{w.Suffix}-2", requests[r2.Id], w.Director.EmployeeId);
        project.Stage.ShouldBe(ProjectStages.Initiation);
        initiative.Stage.ShouldBe(ProjectStages.Planning);
        db.Projects.AddRange(project, initiative);
        await db.SaveChangesAsync();
        return _seed = new Seed(w, project.Id, project.Number, initiative.Number);
    }

    private static object Mine(string field, Seed s) => new { field, @operator = "contains", values = new[] { "LBL-" + s.World.Suffix } };

    /// <summary>Report types run through <c>reports/query</c>, report-builder (dataset) reports through <c>reports/run</c>.</summary>
    private static async Task<JsonElement> QueryAsync(HttpClient client, object definition, string language, string action = "query")
    {
        var response = await client.PostAsJsonAsync($"/api/v1/reporting/reports/{action}", new { definition, language, pageSize = 200 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<List<string?>> Cells(JsonElement run) =>
        run.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList()).ToList();

    private static object TypeDefinition(Seed s) => new
    {
        reportType = ReportType,
        columns = new[] { "a.number", "a.stage", "a.status", "a.classification", "a.strategicObjective" },
        filters = new[] { Mine("a.number", s) },
        groupBy = new[] { "a.stage" },
        sort = new[] { new { field = "a.number", descending = false } },
        summaries = Array.Empty<object>(),
        chart = new { type = "bar", category = "a.stage", series = "a.classification" },
        highlights = Array.Empty<object>(),
    };

    [Fact]
    public async Task A_report_grouped_and_charted_by_project_stage_shows_stage_status_and_classification_labels_in_arabic_and_english()
    {
        var s = await SeedAsync();
        var client = s.World.C(s.World.PmOffice);

        // Report type (Reporting 2.0): cells, group labels, chart categories and series in the reader's language; group keys stay codes.
        var en = await QueryAsync(client, TypeDefinition(s), "en");
        Cells(en).ShouldBe(
        [
            [s.ProjectNumber, "Initiation", "Active", "Project", "Digital services"],
            [s.InitiativeNumber, "Planning", "Active", "Initiative", "Digital services"],
        ], ignoreOrder: true);
        var groups = en.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("label").GetString()).ShouldBe(["Initiation", "Planning"], ignoreOrder: true);
        groups.Select(g => g.GetProperty("keys")[0].GetString()).ShouldBe([ProjectStages.Initiation, ProjectStages.Planning], ignoreOrder: true);
        var chart = en.GetProperty("chart");
        chart.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldBe(["Initiation", "Planning"], ignoreOrder: true);
        chart.GetProperty("series").EnumerateArray().Select(x => x.GetProperty("label").GetString()).ShouldBe(["Project", "Initiative"], ignoreOrder: true);
        chart.GetProperty("series").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ShouldBe([Classifications.Project, Classifications.Initiative], ignoreOrder: true);

        var ar = await QueryAsync(client, TypeDefinition(s), "ar");
        Cells(ar).ShouldBe(
        [
            [s.ProjectNumber, "البدء", "نشط", "مشروع", "خدمات رقمية"],
            [s.InitiativeNumber, "التخطيط", "نشط", "مبادرة", "خدمات رقمية"],
        ], ignoreOrder: true);
        ar.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("label").GetString()).ShouldBe(["البدء", "التخطيط"], ignoreOrder: true);
        ar.GetProperty("chart").GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldBe(["البدء", "التخطيط"], ignoreOrder: true);
        ar.GetProperty("chart").GetProperty("series").EnumerateArray().Select(x => x.GetProperty("label").GetString()).ShouldBe(["مشروع", "مبادرة"], ignoreOrder: true);

        // The export carries the same labels (CSV in Arabic).
        var csv = await client.PostAsJsonAsync("/api/v1/reporting/reports/export", new { definition = TypeDefinition(s), format = "csv", language = "ar", title = "Stages" });
        csv.StatusCode.ShouldBe(HttpStatusCode.OK, await csv.Content.ReadAsStringAsync());
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        text.ShouldContain("البدء");
        text.ShouldContain("مبادرة");
        text.ShouldNotContain(",initiation");
        text.ShouldNotContain(",planning");

        // Report builder (Phase 17 dataset report): rows and groups are labelled the same way.
        object Builder(string[] columns, string[] groupBy) => new
        {
            dataset = "portfolio.projects",
            columns,
            filters = new[] { Mine("number", s) },
            groupBy,
            sort = Array.Empty<object>(),
        };
        Cells(await QueryAsync(client, Builder(["number", "stage", "methodology", "progressStatus"], []), "en", "run")).Select(r => r[1]).ShouldBe(["Initiation", "Planning"], ignoreOrder: true);
        var grouped = Cells(await QueryAsync(client, Builder(["number", "stage"], ["stage"]), "ar", "run"));
        grouped.Select(r => r[0]).ShouldBe(["البدء", "التخطيط"], ignoreOrder: true);
        grouped.Select(r => r[1]).ShouldAllBe(count => count == "1");
    }

    [Fact]
    public async Task The_project_dossier_shows_labels_not_codes_in_its_fields_and_in_word()
    {
        var s = await SeedAsync();
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, s.World.PmOffice);
        var service = scope.ServiceProvider.GetRequiredService<DossierTemplateService>();
        var template = (await service.ListAsync(CancellationToken.None)).Single(t => t.Code == Dossier);
        var doc = (await service.BuildAsync(template, s.Project, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None)).ShouldNotBeNull();

        DossierPair Pair(string section, string labelEn) => doc.Blocks.Single(b => b.Key == section).Pairs!.Single(p => p.LabelEn == labelEn);
        Pair("health", "Stage").ValueEn.ShouldBe("Initiation");
        Pair("health", "Stage").ValueAr.ShouldBe("البدء");
        Pair("health", "Status").ValueEn.ShouldBe("Active");
        Pair("health", "Status").ValueAr.ShouldBe("نشط");
        Pair("overview", "Classification").ValueAr.ShouldBe("مشروع");
        Pair("overview", "Methodology").ValueEn.ShouldNotBe(Methodologies.Waterfall);
        Pair("overview", "Strategic objective").ValueEn.ShouldBe("Digital services");
        Pair("overview", "Strategic objective").ValueAr.ShouldBe("خدمات رقمية");

        // The Word file (rendered in-process) carries the label in the language asked for.
        var response = await s.World.C(s.World.PmOffice).PostAsJsonAsync($"/api/v1/reporting/dossiers/{Dossier}/render", new { recordId = s.Project, format = "docx", language = "ar" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var word = WordprocessingDocument.Open(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), false);
        var body = word.MainDocumentPart!.Document!.Body!.InnerText;
        body.ShouldContain("البدء");
        body.ShouldContain("خدمات رقمية");
        body.ShouldNotContain(ProjectStages.Initiation);
    }
}
