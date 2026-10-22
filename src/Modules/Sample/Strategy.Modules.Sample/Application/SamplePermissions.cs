namespace Strategy.Modules.Sample.Application;

/// <summary>Permission codes (<c>&lt;system&gt;.&lt;object&gt;.&lt;action&gt;</c>). The seeded catalogue with labels arrives in Phase 2.</summary>
public static class SamplePermissions
{
    public const string ResourceType = "sample.item";
    public const string Read = "sample.item.read";
    public const string Create = "sample.item.create";
    public const string Update = "sample.item.update";
}
