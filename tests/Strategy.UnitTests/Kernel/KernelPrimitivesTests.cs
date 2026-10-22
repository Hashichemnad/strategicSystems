using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.UnitTests.Kernel;

public class KernelPrimitivesTests
{
    [Fact]
    public void Entity_ids_are_version_7_and_time_ordered()
    {
        var first = new TestEntity();
        var second = new TestEntity();

        first.Id.Version.ShouldBe(7);
        // The first 48 bits are the Unix time in ms; ordering inside one millisecond is random.
        string.CompareOrdinal(first.Id.ToString("N")[..12], second.Id.ToString("N")[..12]).ShouldBeLessThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("createdAt", "createdAt", false)]
    [InlineData("-nameAr", "nameAr", true)]
    public void Page_request_parses_sort(string? sort, string? field, bool descending)
    {
        var order = new PageRequest(Sort: sort).SortOrder;

        if (field is null)
        {
            order.ShouldBeNull();
        }
        else
        {
            order.ShouldBe((field, descending));
        }
    }

    [Fact]
    public void Page_request_normalises_out_of_range_values()
    {
        var page = new PageRequest(Page: 0, PageSize: 10_000).Normalised();

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(PageRequest.MaxPageSize);
        page.Skip.ShouldBe(0);
    }

    [Fact]
    public void Paged_result_computes_total_pages() =>
        new PagedResult<int>([], 1, 25, 51).TotalPages.ShouldBe(3);

    [Fact]
    public void Request_context_rejects_unknown_channels()
    {
        var context = new RequestContext();

        Should.Throw<ArgumentException>(() => context.Initialise("abc12345", "telepathy"));
        context.Initialise("abc12345", Channels.Job);
        context.Channel.ShouldBe(Channels.Job);
        context.UserId.ShouldBeNull();
    }

    private sealed class TestEntity : Entity;
}
