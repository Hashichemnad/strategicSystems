namespace Strategy.Modules.IpRegistry.Contracts;

/// <summary>Facts other systems use to refer to IP records (links, badges).</summary>
public static class IpResource
{
    public const string Type = "ip.record";

    public const string LinkTemplate = "/ip/records/{id}";

    /// <summary>Link kind from an IP record to the published document it came from (ADR 0087).</summary>
    public const string SourceDocumentLink = "ip-source";

    public static string Link(Guid id) => LinkTemplate.Replace("{id}", id.ToString("D"), StringComparison.Ordinal);
}
