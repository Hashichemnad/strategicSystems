using System.Text.Json;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.DocumentRegistry.Mcp;

/// <summary>Read tools of the Document Registry (ADR 0031): every result follows the sensitivity clearance of the token owner (ADR 0083).</summary>
internal static class DocRegToolResults
{
    public static object Item(DocumentListItemDto d) => new
    {
        documentId = d.Id,
        d.DocumentNo,
        name = d.NameEn ?? d.NameAr,
        nameAr = d.NameAr,
        d.Level,
        type = d.DocType,
        d.Sensitivity,
        d.Status,
        version = d.CurrentVersion,
        d.EffectiveDate,
        d.ExpiryDate,
        link = $"/documents/{d.Id:D}",
    };
}

internal sealed class SearchDocumentsTool(DocumentService documents) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "search_documents", DocRegPermissions.SystemCode, ToolKind.Read, "البحث في الوثائق", "Search documents",
        "Searches the published corporate documents (policies, procedures, manuals…) the user is cleared to see, by words in the title, number, description or file text.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"search":{"type":"string","maxLength":200},"type":{"type":"string"},"level":{"type":"string"}}}"""),
        DocRegPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var library = await documents.LibraryAsync(new PageRequest(1, 20), arguments.Text("level"), arguments.Text("type"), null, null, null, null, arguments.Text("search"),
            cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new { total = library.Page.TotalCount, documents = library.Page.Items.Select(DocRegToolResults.Item) });
    }
}

internal sealed class GetDocumentTool(DocumentService documents) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "get_document", DocRegPermissions.SystemCode, ToolKind.Read, "تفاصيل وثيقة", "Get document",
        "Returns one document the user may see: number, names, classification, validity, versions with change summaries.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"documentId":{"type":"string","format":"uuid"}},"required":["documentId"]}"""),
        DocRegPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("documentId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "documentId is required.");
        }

        var result = await documents.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? ToolResult.NotFound()
            : ToolResult.Ok(new
            {
                documentId = result.Value.Id,
                result.Value.DocumentNo,
                result.Value.NameAr,
                result.Value.NameEn,
                result.Value.Description,
                result.Value.Level,
                type = result.Value.DocType,
                result.Value.Sensitivity,
                result.Value.Status,
                version = result.Value.CurrentVersion,
                result.Value.EffectiveDate,
                result.Value.ExpiryDate,
                versions = result.Value.Versions.Select(v => new { v.Label, v.Status, v.ChangeSummary, v.PublishedAt }),
                link = $"/documents/{id:D}",
            });
    }
}

internal sealed class DocumentExpiryListTool(DocumentService documents) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "document_expiry_list", DocRegPermissions.SystemCode, ToolKind.Read, "الوثائق القريبة من انتهاء الصلاحية", "Documents expiring",
        "Lists published documents the user may see that expire within the given number of days (default 90), and expired ones.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":730}}}"""),
        DocRegPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var days = arguments.Number("days") ?? 90;
        var library = await documents.LibraryAsync(new PageRequest(1, 100), null, null, null, null, null, days, null, cancellationToken).ConfigureAwait(false);
        var expired = await documents.LibraryAsync(new PageRequest(1, 100), null, null, null, null, Domain.DocumentStatuses.Expired, null, null, cancellationToken)
            .ConfigureAwait(false);
        return ToolResult.Ok(new
        {
            expiring = library.Page.Items.Where(i => i.Status == Domain.DocumentStatuses.Published).Select(DocRegToolResults.Item),
            expired = expired.Page.Items.Select(DocRegToolResults.Item),
        });
    }
}
