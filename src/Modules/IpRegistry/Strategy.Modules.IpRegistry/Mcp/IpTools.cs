using System.Text.Json;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.IpRegistry.Mcp;

/// <summary>Read tools of the IP Registry (ADR 0031). Emirates IDs are never returned (masked only).</summary>
internal sealed class SearchIpRecordsTool(IpService ip) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "search_ip_records", IpPermissions.SystemCode, ToolKind.Read, "البحث في سجل الملكية الفكرية", "Search IP records",
        "Searches the intellectual property records the user may see by words, status or protection type.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"search":{"type":"string","maxLength":200},"status":{"type":"string"},"protectionType":{"type":"string"}}}"""),
        IpPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var page = await ip.ListAsync(new PageRequest(1, 25), null, arguments.Text("protectionType"), arguments.Text("status"), null, null, null, false, arguments.Text("search"),
            cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new
        {
            total = page.TotalCount,
            records = page.Items.Select(r => new { recordId = r.Id, r.Number, name = r.NameEn ?? r.NameAr, r.PropertyType, r.ProtectionType, r.Status, r.ExpiryDate, link = $"/ip/records/{r.Id:D}" }),
        });
    }
}

internal sealed class GetIpRecordTool(IpService ip) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "get_ip_record", IpPermissions.SystemCode, ToolKind.Read, "تفاصيل سجل ملكية فكرية", "Get IP record",
        "Returns one IP record the user may see with its registration data and contributors (Emirates IDs masked).",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"recordId":{"type":"string","format":"uuid"}},"required":["recordId"]}"""),
        IpPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("recordId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "recordId is required.");
        }

        var result = await ip.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ToolResult.NotFound();
        }

        var r = result.Value;
        return ToolResult.Ok(new
        {
            recordId = r.Id,
            r.Number,
            r.NameAr,
            r.NameEn,
            r.PropertyType,
            r.ProtectionType,
            r.Status,
            r.ApplicationNo,
            r.RegistrationNo,
            r.RegistrationDate,
            r.ExpiryDate,
            contributors = r.Contributors.Select(c => new
            {
                name = c.Employee?.NameEn ?? c.ExternalName,
                c.Role,
                c.SharePercent,
                emiratesId = c.EmiratesIdMasked,
            }),
            link = $"/ip/records/{r.Id:D}",
        });
    }
}
