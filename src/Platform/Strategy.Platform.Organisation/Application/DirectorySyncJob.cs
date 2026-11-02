using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Organisation.Application;

/// <summary>
/// Nightly directory (AD / HR) comparison (ADR 0014's last item): reads the configured <see cref="IDirectorySyncSource"/> and reports how
/// many records match an employee number, are new, or are no longer listed. Applying changes is the customer connector's job; with the
/// default source ("none") nothing is read.
/// </summary>
internal sealed class DirectorySyncJob(RequestContext requestContext, IDirectorySyncSource source, OrgDbContext db) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "org.directory-sync", "مقارنة الدليل (AD / الموارد البشرية)", "Directory comparison (AD / HR)", "0 50 1 * * ?", typeof(DirectorySyncJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        if (source.Name == "none")
        {
            return "no directory source configured";
        }

        var known = (await db.Employees.AsNoTracking().Select(e => e.EmployeeNo).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var employee in source.ReadEmployeesAsync(cancellationToken).ConfigureAwait(false))
        {
            seen.Add(employee.EmployeeNo);
        }

        var matched = seen.Count(known.Contains);
        return $"source={source.Name} read={seen.Count} matched={matched} new={seen.Count - matched} missing={known.Count(n => !seen.Contains(n))}";
    }
}
