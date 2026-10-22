using Strategy.Modules.Sample.Domain;

namespace Strategy.UnitTests.Sample;

/// <summary>Transition table from docs/design/sample.md: every allowed and every forbidden transition.</summary>
public class SampleItemTests
{
    public static TheoryData<string, string, string?> Transitions => new()
    {
        // from, command, expected error code (null = allowed)
        { SampleItemStatus.Active, "activate", "sample.item.alreadyActive" },
        { SampleItemStatus.Active, "deactivate", null },
        { SampleItemStatus.Active, "update", null },
        { SampleItemStatus.Inactive, "activate", null },
        { SampleItemStatus.Inactive, "deactivate", "sample.item.alreadyInactive" },
        { SampleItemStatus.Inactive, "update", "sample.item.inactive" },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transition_table(string from, string command, string? expectedError)
    {
        var item = ItemIn(from);

        var result = command switch
        {
            "activate" => item.Activate(),
            "deactivate" => item.Deactivate(),
            _ => item.Update("أ", "A", null, null),
        };

        if (expectedError is null)
        {
            result.IsSuccess.ShouldBeTrue();
        }
        else
        {
            result.Error!.Code.ShouldBe(expectedError);
            item.Status.ShouldBe(from);
        }
    }

    [Fact]
    public void Create_starts_active_and_trims_blank_text_to_null()
    {
        var item = SampleItem.Create("  اسم  ", "   ", "", null);

        item.Status.ShouldBe(SampleItemStatus.Active);
        item.NameAr.ShouldBe("اسم");
        item.NameEn.ShouldBeNull();
        item.DescriptionAr.ShouldBeNull();
        item.Id.Version.ShouldBe(7);
    }

    private static SampleItem ItemIn(string status)
    {
        var item = SampleItem.Create("اسم", "Name", null, null);
        if (status == SampleItemStatus.Inactive)
        {
            item.Deactivate().IsSuccess.ShouldBeTrue();
        }

        return item;
    }
}
