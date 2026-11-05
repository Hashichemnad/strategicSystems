namespace Strategy.Modules.Benchmarking.Contracts;

/// <summary>Codes other modules may use to refer to benchmarking records (Links registry, CAP sources).</summary>
public static class BenchLinkTypes
{
    public const string SystemCode = "bench";

    /// <summary>A benchmarking study (<c>BMS-NNNN</c>): task-able, team-able and a CAP remark source.</summary>
    public const string Study = "bench.study";
}
