using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Reporting;

public static class ReportingSourceServiceCollectionExtensions
{
    public static IServiceCollection AddReportingMetricSource<TSource>(this IServiceCollection services)
        where TSource : class, IReportingMetricSource =>
        services.AddScoped<IReportingMetricSource, TSource>();

    public static IServiceCollection AddReportingListSource<TSource>(this IServiceCollection services)
        where TSource : class, IReportingListSource =>
        services.AddScoped<IReportingListSource, TSource>();

    public static IServiceCollection AddReportingDatasetSource<TSource>(this IServiceCollection services)
        where TSource : class, IReportingDatasetSource =>
        services.AddScoped<IReportingDatasetSource, TSource>();

    public static IServiceCollection AddMyWorkCardSource<TSource>(this IServiceCollection services)
        where TSource : class, IMyWorkCardSource =>
        services.AddScoped<IMyWorkCardSource, TSource>();
}
