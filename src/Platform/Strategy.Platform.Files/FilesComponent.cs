using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Files.Api;
using Strategy.Platform.Files.Application;
using Strategy.Platform.Files.Infrastructure;
using Strategy.Platform.Files.Storage;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Files;

/// <summary>Files (<c>/api/v1/files</c>, schema <c>files</c>). docs/design/audit-files-notifications-jobs.md §3.</summary>
public sealed class FilesComponent : IPlatformComponent
{
    public string Name => "files";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FilesDbContext>(FilesDbContext.SchemaName);
        var storage = configuration.GetSection("Files").Get<FileStorageOptions>() ?? new FileStorageOptions();
        services.AddSingleton(storage);
        if (string.Equals(storage.Provider, "s3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStore, S3FileStore>();
        }
        else
        {
            services.AddSingleton<IFileStore, LocalFileStore>();
        }

        services.AddSingleton<IVirusScanner, ClamAvScanner>();
        services.AddScoped<IFileService, FileService>();
        services.AddAdminHealthContributor<FilesHealthContributor>();
        services.AddScoped<IDatabaseSeeder, FilesSeeder>();
        services.AddScheduledJob(RescanJob.Definition);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => FileEndpoints.Map(endpoints.MapPlatformGroup(this));
}
