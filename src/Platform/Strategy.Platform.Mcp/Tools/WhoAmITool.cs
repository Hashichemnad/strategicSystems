using System.Text.Json;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Mcp.Tools;

/// <summary><c>whoami</c>: the token owner — user, employee and unit — so the client knows whose permissions apply.</summary>
internal sealed class WhoAmITool(IEmployeeDirectory employees) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "whoami", "mcp", ToolKind.Read, "من أنا", "Who am I",
        "Returns the signed-in user this connection acts as (user name, employee, unit). Every other tool returns only what this user may see.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var employee = context.User.EmployeeIdOf() is { } id ? await employees.GetAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return ToolResult.Ok(new
        {
            userId = context.User.UserId(),
            userName = context.User.Identity?.Name,
            employee = employee is null ? null : new { employee.Id, employee.EmployeeNo, employee.NameAr, employee.NameEn, employee.UnitId },
            channel = context.Channel,
        });
    }
}
