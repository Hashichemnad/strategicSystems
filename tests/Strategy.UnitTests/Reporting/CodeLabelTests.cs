using NSubstitute;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.ReportTypes;
using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Reporting;

/// <summary>
/// Labels of code fields (ADR 0209): a Settings lookup, a module label source or fixed choices give the label in the reader's language, with
/// fallback to the other language and then to the code; text fields that declare labels are labelled too, free text is not.
/// </summary>
public sealed class CodeLabelTests
{
    private static readonly IReadOnlyList<RecordFieldChoice> Stages =
    [
        new("initiation", "البدء", "Initiation"),
        new("planning", "التخطيط", "Planning"),
        new("closure", "الإغلاق", ""),
    ];

    private sealed class BandLabels : IReportingDimensionLabelSource
    {
        public string SystemCode => "risk";

        public List<IReadOnlyCollection<string>> Calls { get; } = [];

        public Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
        {
            Calls.Add(codes);
            IReadOnlyDictionary<string, DimensionLabel> labels = dimension == "band"
                ? new Dictionary<string, DimensionLabel> { ["high"] = new("مرتفع", "High") }.Where(p => codes.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, DimensionLabel>();
            return Task.FromResult(labels);
        }
    }

    private static (ReportValueFormatter Formatter, BandLabels Bands) Formatter()
    {
        var lookups = Substitute.For<ILookupService>();
        lookups.GetValuesAsync("risk.response", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
            [new LookupValueReference(Guid.Empty, "mitigate", "تخفيف", "Mitigate", 1, true, null), new LookupValueReference(Guid.Empty, "avoid", null, "Avoid", 2, true, null)]);
        var bands = new BandLabels();
        return (new ReportValueFormatter(lookups, Substitute.For<IOrgDirectory>(), Substitute.For<IEmployeeDirectory>(), [bands]), bands);
    }

    [Fact]
    public async Task Fixed_choices_label_codes_in_both_languages_and_unknown_codes_stay_as_they_are()
    {
        var (formatter, _) = Formatter();
        var stage = new DatasetField("stage", "المرحلة", "Stage", DatasetFieldType.Lookup, Choices: Stages);
        await formatter.PrepareAsync([(stage, ["initiation", "planning", "closure", "unknown"])], CancellationToken.None);

        formatter.Format(stage, "initiation", "ar").ShouldBe("البدء");
        formatter.Format(stage, "initiation", "en").ShouldBe("Initiation");
        formatter.Format(stage, "planning", "en").ShouldBe("Planning");
        formatter.Format(stage, "closure", "en").ShouldBe("الإغلاق"); // empty English label → the Arabic one
        formatter.Format(stage, "unknown", "ar").ShouldBe("unknown");
        formatter.Format(stage, null, "ar").ShouldBeNull();
        stage.HasCodeLabels.ShouldBeTrue();
    }

    [Fact]
    public async Task Lookups_and_label_sources_label_codes_and_fixed_choices_back_up_a_label_source()
    {
        var (formatter, bands) = Formatter();
        var response = new DatasetField("response", "الاستجابة", "Response", DatasetFieldType.Lookup, LookupType: "risk.response");
        var band = new DatasetField("band", "النطاق", "Rating", DatasetFieldType.Text, LabelSource: new("risk", "band"),
            Choices: [new("low", "منخفض", "Low"), new("high", "عالٍ (ثابت)", "High (fixed)")]);
        await formatter.PrepareAsync([(response, ["mitigate", "avoid"]), (band, ["high", "low", "high"])], CancellationToken.None);
        await formatter.PrepareAsync([(band, ["high"])], CancellationToken.None);

        formatter.Format(response, "mitigate", "ar").ShouldBe("تخفيف");
        formatter.Format(response, "avoid", "ar").ShouldBe("Avoid"); // no Arabic label → English
        formatter.Format(band, "high", "ar").ShouldBe("مرتفع"); // the source wins over the fixed label
        formatter.Format(band, "high", "en").ShouldBe("High");
        formatter.Format(band, "low", "en").ShouldBe("Low"); // the source does not know it → fixed choice
        bands.Calls.Count.ShouldBe(1); // codes are asked once per run
        bands.Calls[0].ShouldBe(["high", "low"], ignoreOrder: true);
    }

    [Fact]
    public async Task Free_text_is_never_relabelled_and_a_lookup_field_without_labels_shows_its_code()
    {
        var (formatter, _) = Formatter();
        var title = new DatasetField("title", "العنوان", "Title", DatasetFieldType.Text);
        var raw = new DatasetField("status", "الحالة", "Status", DatasetFieldType.Lookup);
        await formatter.PrepareAsync([(title, ["initiation"]), (raw, ["active"])], CancellationToken.None);

        formatter.Format(title, "initiation", "ar").ShouldBe("initiation");
        formatter.Format(raw, "active", "ar").ShouldBe("active");
        title.HasCodeLabels.ShouldBeFalse();
        raw.HasCodeLabels.ShouldBeFalse();
    }
}
