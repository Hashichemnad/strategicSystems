namespace Strategy.Platform.Scheduling.Infrastructure;

/// <summary>The embedded Quartz.NET PostgreSQL table script (Infrastructure/Sql/quartz_tables_postgres.sql).</summary>
internal static class QuartzScript
{
    public static string Read()
    {
        var assembly = typeof(QuartzScript).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("quartz_tables_postgres.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
