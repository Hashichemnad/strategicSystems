using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.AI;

/// <summary>
/// A switchable AI feature (Settings → AI). Access per role is the permission <see cref="Permission"/> (<c>ai.&lt;key&gt;.use</c>), granted
/// through roles like any other permission (ADR 0028). <see cref="NeedsProvider"/> false: the feature works without a model (similar records).
/// </summary>
public sealed record AiFeatureDefinition(string Key, string NameAr, string NameEn, string Permission, bool NeedsProvider = true);

/// <summary>The shared features of Phase 5. Modules may register more.</summary>
public static class AiFeatures
{
    public const string Extract = "extract";
    public const string Brief = "brief";
    public const string Translate = "translate";
    public const string Similar = "similar";
    public const string Ask = "ask";

    public static string PermissionOf(string key) => $"ai.{key}.use";
}

public static class AiRegistrationExtensions
{
    public static IServiceCollection AddAiFeature(this IServiceCollection services, AiFeatureDefinition feature) => services.AddSingleton(feature);

    /// <summary>A form other people can fill from a document (document-to-form extraction).</summary>
    public static IServiceCollection AddAiForm(this IServiceCollection services, AiFormDefinition form) => services.AddSingleton(form);

    /// <summary>Run-time choices of an AI form's fields (lookups, ADR 0050).</summary>
    public static IServiceCollection AddAiFormChoiceSource<TSource>(this IServiceCollection services)
        where TSource : class, IAiFormChoiceSource => services.AddScoped<IAiFormChoiceSource, TSource>();

    public static IServiceCollection AddAiRecordSource<TSource>(this IServiceCollection services)
        where TSource : class, IAiRecordSource => services.AddScoped<IAiRecordSource, TSource>();

    public static IServiceCollection AddRecordSearchSource<TSource>(this IServiceCollection services)
        where TSource : class, IRecordSearchSource => services.AddScoped<IRecordSearchSource, TSource>();

    /// <summary>A prompt template assembly: embedded resources named <c>…Prompts.&lt;feature&gt;.v&lt;n&gt;.md</c>.</summary>
    public static IServiceCollection AddAiPromptAssembly(this IServiceCollection services, System.Reflection.Assembly assembly) =>
        services.AddSingleton(new AiPromptAssembly(assembly));
}

public sealed record AiPromptAssembly(System.Reflection.Assembly Assembly);
