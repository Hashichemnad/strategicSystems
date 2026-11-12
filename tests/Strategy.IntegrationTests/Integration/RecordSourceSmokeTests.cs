using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Integration;

/// <summary>
/// Every reportable record type of every module (ADR 0190) runs on PostgreSQL: all fields, a filter on each filterable field type, sorting,
/// paging with a total, changed-since with a keyset cursor and the deleted feed — so no module query fails to translate.
/// </summary>
public sealed class RecordSourceSmokeTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Every_record_type_of_every_module_answers_every_query_form()
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        var sources = scope.ServiceProvider.GetServices<IReportableRecordSource>().ToList();
        sources.Select(s => s.SystemCode).Distinct().Count().ShouldBeGreaterThanOrEqualTo(11);
        var system = StrategyClaims.CreateSystemPrincipal();
        var failures = new List<string>();
        foreach (var source in sources)
        {
            foreach (var type in source.RecordTypes)
            {
                type.Fields.Select(f => f.Code).ShouldBeUnique();
                type.Relationships.ShouldAllBe(r => type.Field(r.SourceField) != null, $"{type.Code}: relationship source fields are declared");
                var fields = new[] { RecordFields.Id, RecordFields.ModifiedAt }.Concat(type.Fields.Select(f => f.Code)).ToList();
                var filters = type.Fields.Select(Sample).OfType<RecordFilter>().ToList();
                RecordFilter? filter = filters.Count == 0 ? null : filters.Aggregate((a, b) => new RecordFilterOr(a, b));
                var queries = new[]
                {
                    new RecordQuery(type.Code, fields, null, type.Fields.Take(2).Select(f => new RecordSort(f.Code, true)).ToList(), 0, 5, IncludeTotal: true),
                    new RecordQuery(type.Code, fields, filter, [], 0, 5),
                    new RecordQuery(type.Code, fields, null, [], 0, 5, ChangedSince: DateTimeOffset.UnixEpoch,
                        After: new RecordCursor(DateTimeOffset.UnixEpoch, Guid.Empty)),
                    new RecordQuery(type.Code, fields, null, [], 0, 5, ChangedSince: DateTimeOffset.UnixEpoch, Mode: RecordQueryMode.Deleted),
                };
                foreach (var query in queries)
                {
                    try
                    {
                        var page = await source.QueryAsync(query, system, CancellationToken.None);
                        page.Fields.Count.ShouldBe(fields.Count);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type.Code} ({query.Mode}, filter {query.Filter is not null}): {ex.GetType().Name} {ex.Message}");
                    }
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    private static RecordFilter? Sample(RecordFieldDefinition field) => field.Type switch
    {
        RecordFieldType.Text => new RecordFilterText(field.Code, RecordTextMatch.Contains, "x"),
        RecordFieldType.Lookup => new RecordFilterIn(field.Code, ["a", "b"]),
        RecordFieldType.Number or RecordFieldType.Money or RecordFieldType.Ratio => new RecordFilterCompare(field.Code, RecordCompare.Ge, 1m),
        RecordFieldType.Date => new RecordFilterCompare(field.Code, RecordCompare.Lt, new DateOnly(2026, 1, 1)),
        RecordFieldType.DateTime => new RecordFilterCompare(field.Code, RecordCompare.Gt, DateTimeOffset.UnixEpoch),
        RecordFieldType.Bool => new RecordFilterCompare(field.Code, RecordCompare.Eq, true),
        RecordFieldType.Id or RecordFieldType.Unit or RecordFieldType.Employee => new RecordFilterCompare(field.Code, RecordCompare.Eq, Guid.Empty),
        _ => null,
    };
}
