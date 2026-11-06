using System.Net;
using System.Text.Json;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>Asserts the Problem Details contract: status, content type, stable code and message key.</summary>
public static class ProblemAssertions
{
    public static async Task<JsonElement> ShouldBeProblemAsync(
        this HttpResponseMessage response, HttpStatusCode status, string code, string? messageKey = null)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
        if (messageKey is not null)
        {
            problem.GetProperty("messageKey").GetString().ShouldBe(messageKey);
        }

        return problem;
    }

    /// <summary>A 400 <c>validation.failed</c> problem with an error on <paramref name="field"/> carrying <paramref name="messageKey"/>.</summary>
    public static async Task ShouldBeValidationProblemAsync(this HttpResponseMessage response, string field, string messageKey)
    {
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("field").GetString(), e.GetProperty("messageKey").GetString()))
            .ShouldContain((field, messageKey));
    }
}
