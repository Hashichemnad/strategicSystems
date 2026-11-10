using System.Text.Json;
using Strategy.Platform.AI.Tools;

namespace Strategy.Platform.Integration.Mcp;

/// <summary>
/// <c>find_by_external_reference</c> (ADR 0165): the record a source system's ID points at, as a summary with its link — only when the token owner
/// may read it (same resolver as the record page); otherwise <c>not_found</c>, so existence does not leak.
/// </summary>
internal sealed class FindByExternalReferenceTool(IExternalReferenceStore store, IReferencedRecordResolver resolver) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "find_by_external_reference", IntegrationPermissions.SystemCode, ToolKind.Read, "البحث بالمعرّف الخارجي", "Find a record by external reference",
        "Finds the record that an external system's ID refers to (e.g. a Salesforce record ID), given the source system code, the record type "
        + "(e.g. portfolio.project, risk.risk, org.employee) and the external ID. Returns the record's type, id, number, title and link, or not_found.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "source":{"type":"string","maxLength":50,"description":"Source system code, e.g. salesforce"},
              "type":{"type":"string","maxLength":100,"description":"Record type, e.g. portfolio.project"},
              "externalId":{"type":"string","maxLength":255,"description":"The record's ID in the source system"}},
             "required":["source","type","externalId"]}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (arguments.Text("source") is not { Length: > 0 } source || arguments.Text("type") is not { Length: > 0 } type
            || arguments.Text("externalId") is not { Length: > 0 } externalId)
        {
            return ToolResult.Fail(ToolErrors.Validation, "source, type and externalId are required.");
        }

        var reference = await store.FindAsync(source.Trim(), type.Trim(), externalId.Trim(), cancellationToken).ConfigureAwait(false);
        if (reference is null)
        {
            return ToolResult.NotFound();
        }

        var records = await resolver.ResolveAsync(reference.EntityType, [reference.RecordId], context.User, cancellationToken).ConfigureAwait(false);
        if (!records.TryGetValue(reference.RecordId, out var record))
        {
            return ToolResult.NotFound();
        }

        var ar = context.Language == "ar";
        return ToolResult.Ok(new
        {
            resourceType = record.ResourceType,
            id = record.Id,
            system = record.SystemCode,
            typeName = ar ? record.TypeNameAr ?? record.TypeNameEn : record.TypeNameEn ?? record.TypeNameAr,
            number = record.Number,
            title = ar ? record.TitleAr ?? record.TitleEn : record.TitleEn ?? record.TitleAr,
            link = record.Link,
            source = reference.SourceSystem,
            externalId = reference.ExternalId,
            isOrigin = reference.IsOrigin,
            lastSyncedAt = reference.LastSyncedAt,
        });
    }
}
