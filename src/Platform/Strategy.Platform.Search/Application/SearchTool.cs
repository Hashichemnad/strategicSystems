using System.Text.Json;
using Strategy.Platform.AI.Tools;

namespace Strategy.Platform.Search.Application;

/// <summary>
/// MCP / Ask read tool <c>search</c>: the global search with the caller's permissions (same service, same visibility rules as the top bar).
/// Enabled by default (read tool, ADR 0031 / 0160).
/// </summary>
internal sealed class SearchTool(SearchService search) : IAppTool
{
    public const string Name = "search";

    public ToolDefinition Definition { get; } = new(
        Name, "search", ToolKind.Read, "البحث الشامل", "Global search",
        "Searches every system the signed-in user may open, by record number, title (Arabic or English), external ID and key fields. "
        + "Returns results grouped by system: type, id, number, titles, status, link and what matched.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "query":{"type":"string","description":"A record number or words to look for (at least 2 characters)","minLength":2,"maxLength":200},
              "system":{"type":"string","description":"Optional system code to search only, e.g. risk"},
              "limit":{"type":"integer","description":"Most results per system (default 5)","minimum":1,"maximum":25}},
             "required":["query"]}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var limit = arguments.Number("limit") is { } n ? Math.Clamp(n, 1, 25) : SearchService.DefaultLimit;
        var result = await search.SearchAsync(context.User, arguments.Text("query"), arguments.Text("system"), limit, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error.Kind == Kernel.Results.ErrorKind.Forbidden
                ? ToolResult.Fail(ToolErrors.Forbidden, "Search is available to internal users only.")
                : ToolResult.Fail(ToolErrors.Validation, "query must have 2 to 200 characters.");
        }

        return ToolResult.Ok(new
        {
            query = result.Value.Query,
            groups = result.Value.Groups.Select(g => new
            {
                system = g.SystemCode,
                g.NameAr,
                g.NameEn,
                results = g.Items.Select(i => new
                {
                    resourceType = i.Type,
                    i.Id,
                    i.Number,
                    i.TitleAr,
                    i.TitleEn,
                    status = i.StatusCode,
                    i.Link,
                    i.MatchedOn,
                    i.MatchedText,
                }),
            }),
        });
    }
}
