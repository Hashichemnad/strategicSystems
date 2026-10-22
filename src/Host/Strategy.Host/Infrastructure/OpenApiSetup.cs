using Microsoft.OpenApi;
using Strategy.Platform.Http.Access;

namespace Strategy.Host.Infrastructure;

internal static class OpenApiSetup
{
    public const string DocumentName = "v1";
    public const string ProblemSchemaName = "Problem";

    public static IServiceCollection AddStrategyOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Integrated Strategy System API",
                    Version = DocumentName,
                    Description = "Errors are RFC 9457 Problem Details with a stable `code` and a translatable `messageKey`.",
                };
                return Task.CompletedTask;
            });

            // Every API operation can fail with Problem Details; one named schema gives the TS client a typed error.
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                document.Components.Schemas[ProblemSchemaName] = ProblemSchema();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true)
                {
                    operation.Responses ??= new OpenApiResponses();
                    operation.Responses.TryAdd("default", new OpenApiResponse
                    {
                        Description = "Problem Details (RFC 9457)",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference(ProblemSchemaName, context.Document) },
                        },
                    });
                }

                return Task.CompletedTask;
            });
        });

    private static OpenApiSchema ProblemSchema()
    {
        static OpenApiSchema Str() => new() { Type = JsonSchemaType.String };
        static OpenApiSchema Obj() => new() { Type = JsonSchemaType.Object, AdditionalPropertiesAllowed = true };

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "status", "code", "messageKey" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = Str(),
                ["title"] = Str(),
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = Str(),
                ["instance"] = Str(),
                ["code"] = Str(),
                ["messageKey"] = Str(),
                ["params"] = Obj(),
                ["traceId"] = Str(),
                ["correlationId"] = Str(),
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "field", "code", "messageKey" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["field"] = Str(),
                            ["code"] = Str(),
                            ["messageKey"] = Str(),
                            ["params"] = Obj(),
                        },
                    },
                },
            },
        };
    }

    /// <summary>Published at <c>/openapi/v1.json</c>; <c>/openapi</c> redirects there.</summary>
    public static void MapStrategyOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi("/openapi/{documentName}.json").AsPublic();
        endpoints.MapGet("/openapi", () => TypedResults.Redirect($"/openapi/{DocumentName}.json"))
            .ExcludeFromDescription()
            .AsPublic();
    }
}
