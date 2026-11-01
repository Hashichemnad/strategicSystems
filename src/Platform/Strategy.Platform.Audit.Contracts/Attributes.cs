namespace Strategy.Platform.Audit;

/// <summary>
/// Audits an entity type that is not an <c>IAuditable</c> business entity (link rows, identity tables, settings singletons).
/// Business entities (<c>AuditableEntity</c>) are audited without this attribute; it may still give them a stable entity code.
/// </summary>
/// <param name="entityType">Stable code shown in the audit trail (e.g. <c>sample.item</c>); default <c>&lt;schema&gt;.&lt;table&gt;</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute(string? entityType = null) : Attribute
{
    public string? EntityType { get; } = entityType;
}

/// <summary>Excludes an entity type or a property from the field-level audit trail (noise such as counters and sign-in stamps).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class NotAuditedAttribute : Attribute;

/// <summary>The property is audited as changed, but its values are written as <c>***</c> (secrets, hashes, keys).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AuditMaskedAttribute : Attribute;

/// <summary>
/// A child row (e.g. a role's permission) belongs to a parent record: its entries carry the parent so the parent's History tab shows them.
/// </summary>
/// <param name="entityType">The parent's entity code (e.g. <c>access.role</c>).</param>
/// <param name="property">The property holding the parent's id.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditParentAttribute(string entityType, string property) : Attribute
{
    public string EntityType { get; } = entityType;

    public string Property { get; } = property;

    /// <summary>Optional property holding the parent's entity code when it varies per row (record roles name their resource type).</summary>
    public string? TypeProperty { get; init; }
}
