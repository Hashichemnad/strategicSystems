namespace Strategy.Modules.Sample.Contracts;

/// <summary>How another module reads a sample item synchronously (by id, never through the Sample DbContext).</summary>
public interface ISampleItemDirectory
{
    Task<SampleItemReference?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>The minimum another module needs to show a link badge to a sample item.</summary>
public sealed record SampleItemReference(Guid Id, string? NameAr, string? NameEn, string Status);
