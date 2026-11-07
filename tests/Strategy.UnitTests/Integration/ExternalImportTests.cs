using NSubstitute;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Domain;

namespace Strategy.UnitTests.Integration;

/// <summary>The import column checks shared by every importer (ADR 0165) and the source-system code rule (ADR 0164).</summary>
public sealed class ExternalImportTests
{
    private static readonly Guid Existing = Guid.CreateVersion7();

    private static IExternalReferenceStore Store()
    {
        var store = Substitute.For<IExternalReferenceStore>();
        store.GetSourceAsync("sf", Arg.Any<CancellationToken>()).Returns(new SourceSystemInfo(Guid.CreateVersion7(), "sf", null, "Salesforce", "salesforce", true));
        store.GetSourceAsync("old", Arg.Any<CancellationToken>()).Returns(new SourceSystemInfo(Guid.CreateVersion7(), "old", null, "Old", "custom", false));
        store.GetSourceAsync(Arg.Is<string>(c => c != "sf" && c != "old"), Arg.Any<CancellationToken>()).Returns((SourceSystemInfo?)null);
        store.FindAsync("sf", "org.unit", "LINKED", Arg.Any<CancellationToken>())
            .Returns(new ExternalReference(Guid.CreateVersion7(), "sf", "org.unit", "LINKED", Existing, true, DateTimeOffset.UtcNow, null, null, null, SyncStatuses.Synced, 1));
        store.ListForRecordAsync("org.unit", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(callInfo => callInfo.ArgAt<Guid>(1) == Existing
            ? [new ExternalReference(Guid.CreateVersion7(), "sf", "org.unit", "LINKED", Existing, true, DateTimeOffset.UtcNow, null, null, null, SyncStatuses.Synced, 1)]
            : []);
        return store;
    }

    [Fact]
    public async Task Rows_without_both_columns_have_no_key_and_one_column_alone_is_an_issue()
    {
        var import = new ExternalImport(Store(), "org.unit");
        var issues = new List<ExternalImportIssue>();
        (await import.ReadAsync(2, null, " ", issues, CancellationToken.None)).ShouldBeNull();
        issues.ShouldBeEmpty();
        (await import.ReadAsync(3, "sf", null, issues, CancellationToken.None)).ShouldBeNull();
        (await import.ReadAsync(4, null, "X", issues, CancellationToken.None)).ShouldBeNull();
        issues.Select(i => (i.Row, i.Column, i.MessageKey)).ShouldBe(
        [
            (3, ExternalImport.IdColumn, ExternalReferenceErrors.ExternalIdRequired),
            (4, ExternalImport.SourceColumn, ExternalReferenceErrors.SourceRequired),
        ]);
    }

    [Fact]
    public async Task Unknown_inactive_too_long_and_duplicate_keys_are_reported()
    {
        var import = new ExternalImport(Store(), "org.unit");
        var issues = new List<ExternalImportIssue>();
        (await import.ReadAsync(2, "nope", "A", issues, CancellationToken.None)).ShouldBeNull();
        (await import.ReadAsync(3, "old", "A", issues, CancellationToken.None)).ShouldBeNull();
        (await import.ReadAsync(4, "sf", new string('x', 256), issues, CancellationToken.None)).ShouldBeNull();
        (await import.ReadAsync(5, "sf", " A-1 ", issues, CancellationToken.None)).ShouldBe(new ExternalKey("sf", "A-1"));
        (await import.ReadAsync(6, "sf", "A-1", issues, CancellationToken.None)).ShouldBeNull();
        issues.Select(i => (i.Row, i.MessageKey)).ShouldBe(
        [
            (2, ExternalReferenceErrors.UnknownSource),
            (3, ExternalReferenceErrors.InactiveSource),
            (4, ExternalReferenceErrors.ExternalIdTooLong),
            (6, ExternalReferenceErrors.DuplicateInFile),
        ]);
        issues[0].Code.ShouldBe("integration.unknownSource");
    }

    [Fact]
    public async Task A_matched_record_must_not_take_an_id_of_another_record_nor_a_second_id_of_the_same_source()
    {
        var import = new ExternalImport(Store(), "org.unit");
        var issues = new List<ExternalImportIssue>();
        await import.CheckMatchAsync(2, new ExternalKey("sf", "LINKED"), Guid.CreateVersion7(), issues, CancellationToken.None);
        await import.CheckMatchAsync(3, new ExternalKey("sf", "OTHER"), Existing, issues, CancellationToken.None);
        await import.CheckMatchAsync(4, new ExternalKey("sf", "LINKED"), Existing, issues, CancellationToken.None);
        issues.Select(i => (i.Row, i.MessageKey)).ShouldBe(
        [
            (2, ExternalReferenceErrors.ExternalIdTaken),
            (3, ExternalReferenceErrors.RecordAlreadyReferenced),
        ]);
    }

    [Theory]
    [InlineData("salesforce", true)]
    [InlineData("sap-erp_2", true)]
    [InlineData("s", false)]
    [InlineData("Salesforce", false)]
    [InlineData("-sap", false)]
    [InlineData("sap erp", false)]
    [InlineData(null, false)]
    public void Source_system_codes_are_stable_lower_case_keys(string? code, bool valid) =>
        SourceSystem.IsValidCode(code).ShouldBe(valid);
}
