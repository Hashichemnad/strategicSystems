using System.Net;
using System.Net.Http.Json;
using Strategy.Modules.Kpi.Application;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Kpi;

/// <summary>The KPI card picker's lookup (ADR 0169): search as you type, Arabic normalisation, data scope, ids, page size limit.</summary>
public sealed class KpiLookupTests(KpiFixture fx) : IClassFixture<KpiFixture>
{
    private const string Lookup = KpiWorld.Base + "/cards/lookup";

    [Fact]
    public async Task A_card_beyond_the_first_200_is_found_by_search_and_pages_hold_at_most_50()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var tag = "lk" + Guid.CreateVersion7().ToString("N")[^6..];
        KpiCardDto last = null!;
        for (var i = 1; i <= 205; i++)
        {
            last = await w.CreateCardAsync(KpiWorld.Card($"{tag} item {i:000}", w.OwnerA, w.DepartmentA));
        }

        var all = await GetAsync(w.C(w.Pmo), $"?search={tag}&pageSize=500");
        all.PageSize.ShouldBe(50);
        all.TotalCount.ShouldBe(205);
        all.Items.Count.ShouldBe(50);

        var found = await GetAsync(w.C(w.Pmo), $"?search={Uri.EscapeDataString($"{tag} item 205")}");
        found.Items.Single().Id.ShouldBe(last.Id);
        (await GetAsync(w.C(w.Pmo), $"?search={Uri.EscapeDataString(last.Code)}")).Items.Single().Id.ShouldBe(last.Id);

        var page5 = await GetAsync(w.C(w.Pmo), $"?search={tag}&pageSize=50&page=5");
        page5.Items.Select(i => i.Id).ShouldContain(last.Id);
    }

    [Fact]
    public async Task Arabic_search_ignores_diacritics_and_letter_forms()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var tag = Guid.CreateVersion7().ToString("N")[^6..];
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nameAr"] = $"مُؤَشِّر إنجاز المُبادرة {tag}",
            ["nameEn"] = $"Initiative delivery {tag}",
        };
        var card = await w.CreateCardAsync(Merge(KpiWorld.Card("x", w.OwnerA, w.DepartmentA), values));

        (await GetAsync(w.C(w.Pmo), $"?search={Uri.EscapeDataString($"مؤشر انجاز المبادره {tag}")}")).Items.Single().Id.ShouldBe(card.Id);
        (await GetAsync(w.C(w.Pmo), $"?search={Uri.EscapeDataString($"INITIATIVE DELIVERY {tag}")}")).Items.Single().Id.ShouldBe(card.Id);
    }

    [Fact]
    public async Task Lookup_is_scoped_and_ids_resolve_only_visible_cards()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var tag = "sc" + Guid.CreateVersion7().ToString("N")[^6..];
        var card = await w.CreateCardAsync(KpiWorld.Card($"{tag} scoped", w.OwnerA, w.DepartmentA));

        (await GetAsync(w.C(w.Pmo), $"?ids={card.Id}")).Items.Single().Code.ShouldBe(card.Code);
        (await GetAsync(w.C(w.OwnerA), $"?search={tag}")).Items.Select(i => i.Id).ShouldContain(card.Id);
        (await GetAsync(w.C(w.Outsider), $"?search={tag}")).Items.ShouldBeEmpty();
        (await GetAsync(w.C(w.Outsider), $"?ids={card.Id}")).Items.ShouldBeEmpty();

        var anonymous = await fx.Factory.CreateSpaClientAsync();
        (await anonymous.GetAsync(Lookup)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<PagedResult<KpiCardLookupDto>> GetAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(Lookup + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResult<KpiCardLookupDto>>())!;
    }

    private static Dictionary<string, object?> Merge(object card, Dictionary<string, object?> values)
    {
        var merged = card.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(card), StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            merged[key] = value;
        }

        return merged;
    }
}
