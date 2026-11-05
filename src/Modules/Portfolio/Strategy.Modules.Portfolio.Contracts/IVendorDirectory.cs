namespace Strategy.Modules.Portfolio.Contracts;

/// <summary>
/// The shared supplier register (Wasel vendor register, ADR 0112): other modules read vendors through this contract, never through the Portfolio tables.
/// </summary>
public interface IVendorDirectory
{
    Task<VendorSummary?> GetAsync(Guid vendorId, CancellationToken cancellationToken = default);

    /// <summary>Up to 50 vendors whose name, number or trade licence matches <paramref name="search"/>.</summary>
    Task<IReadOnlyList<VendorSummary>> SearchAsync(string? search, bool activeOnly, CancellationToken cancellationToken = default);
}

/// <summary>A vendor as other modules see it: no contacts beyond the e-mail, no ratings.</summary>
public sealed record VendorSummary(Guid Id, string Number, string? NameAr, string? NameEn, string Status, string? TradeLicenceNo, string? Email);
