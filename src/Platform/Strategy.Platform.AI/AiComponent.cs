using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Api;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Domain;
using Strategy.Platform.AI.Features;
using Strategy.Platform.AI.Infrastructure;
using Strategy.Platform.AI.Providers;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.AI;

public static class AiPermissions
{
    public const string SystemCode = "ai";
    public const string Extract = "ai.extract.use";
    public const string Brief = "ai.brief.use";
    public const string Translate = "ai.translate.use";
    public const string Similar = "ai.similar.use";
    public const string Ask = "ai.ask.use";
    public const string Manage = "ai.settings.manage";
    public const string UsageRead = "ai.usage.read";
}

/// <summary>AI platform (<c>/api/v1/ai</c>, schema <c>ai</c>, docs/design/ai.md, ADRs 0028–0030).</summary>
public sealed class AiComponent : IPlatformComponent
{
    public string Name => "ai";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(AiDbContext.SchemaName);
        services.AddHttpContextAccessor();
        services.AddSingleton<IAiProviderAdapter, AnthropicAdapter>();
        services.AddSingleton<IAiProviderAdapter>(new OpenAiFamilyAdapter(AiProviderTypes.OpenAi));
        services.AddSingleton<IAiProviderAdapter>(new OpenAiFamilyAdapter(AiProviderTypes.AzureOpenAi));
        services.AddSingleton<IAiProviderAdapter>(new OpenAiFamilyAdapter(AiProviderTypes.OpenAiCompatible));
        if (configuration.GetValue("AI:EnableDemoProvider", defaultValue: false))
        {
            services.AddSingleton<IAiProviderAdapter, DemoAdapter>();
        }

        services.AddSingleton<PromptLibrary>();
        services.AddScoped<AiGateway>();
        services.AddScoped<AiAdministration>();
        services.AddScoped<ExtractionService>();
        services.AddScoped<BriefService>();
        services.AddScoped<TranslationService>();
        services.AddScoped<SimilarService>();
        services.AddScoped<AskService>();
        services.AddScoped<ToolCatalog>();
        services.AddScoped<ToolExecutor>();
        services.AddAppTool<SearchRecordsTool>();
        services.AddAppTool<GetRecordTool>();
        services.AddAccessCatalogue(new AiCatalogue());
        services.AddAiFeature(new(AiFeatures.Extract, "تعبئة النماذج من المستندات", "Fill forms from documents", AiPermissions.Extract));
        services.AddAiFeature(new(AiFeatures.Brief, "ملخص المعتمد", "Approver brief", AiPermissions.Brief));
        services.AddAiFeature(new(AiFeatures.Translate, "الترجمة بين العربية والإنجليزية", "Arabic ⇄ English translation", AiPermissions.Translate));
        services.AddAiFeature(new(AiFeatures.Similar, "السجلات المشابهة", "Similar records", AiPermissions.Similar, NeedsProvider: false));
        services.AddAiFeature(new(AiFeatures.Ask, "اسأل النظام", "Ask the system", AiPermissions.Ask));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AiEndpoints.Map(endpoints.MapPlatformGroup(this));
}

internal sealed class AiCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(AiPermissions.SystemCode, SystemKind.Platform, "الذكاء الاصطناعي", "Artificial intelligence", 48);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(AiPermissions.Extract, "تعبئة النماذج من المستندات بالذكاء الاصطناعي", "Fill forms from documents with AI", Scopable: false),
        new(AiPermissions.Brief, "إنشاء ملخص المعتمد بالذكاء الاصطناعي", "Generate approver briefs with AI", Scopable: false),
        new(AiPermissions.Translate, "الترجمة بالذكاء الاصطناعي", "Translate with AI", Scopable: false),
        new(AiPermissions.Similar, "عرض السجلات المشابهة", "Find similar records", Scopable: false),
        new(AiPermissions.Ask, "اسأل النظام", "Ask the system", Scopable: false),
        new(AiPermissions.Manage, "إدارة إعدادات الذكاء الاصطناعي", "Manage AI settings", Scopable: false),
        new(AiPermissions.UsageRead, "عرض سجل استخدام الذكاء الاصطناعي", "View AI usage", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("user", "مستخدم الذكاء الاصطناعي", "AI user",
            [new(AiPermissions.Extract), new(AiPermissions.Brief), new(AiPermissions.Translate), new(AiPermissions.Similar), new(AiPermissions.Ask)],
            AssignToEveryone: true),
        new("administrator", "مسؤول الذكاء الاصطناعي", "AI administrator", [new(AiPermissions.Manage), new(AiPermissions.UsageRead)], RequiresMfa: true),
    ];
}
