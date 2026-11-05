using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Modules.Tasks.Domain;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>
/// Test-only bulk seeding of risks and tasks for the reporting performance tests (docs/design/reporting.md §14: 1 000 risks, 10 000 tasks).
/// Rows are written with binary <c>COPY</c> (no audit, no outbox: this is set-up, not business writes), with values the modules themselves would
/// hold: ratings and bands from the current risk framework, residual status from the band, overdue reviews / actions / tasks relative to now,
/// assessment history for movement. Employee <c>k</c> is taken to belong to unit <c>k % unitIds.Count</c> (pass aligned lists).
/// </summary>
internal static class RiskTaskVolume
{
    private sealed class Row(NpgsqlBinaryImporter writer)
    {
        public async Task Add(object? value, NpgsqlDbType type, CancellationToken ct)
        {
            if (value is null)
            {
                await writer.WriteNullAsync(ct);
            }
            else
            {
                await writer.WriteAsync(value, type, ct);
            }
        }
    }

    public static async Task SeedAsync(
        ApiFactory factory, IReadOnlyList<Guid> unitIds, IReadOnlyList<Guid> employeeIds, int risks = 1000, int tasks = 10000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(unitIds);
        ArgumentNullException.ThrowIfNull(employeeIds);
        if (unitIds.Count == 0 || employeeIds.Count == 0)
        {
            throw new ArgumentException("At least one unit and one employee are needed.");
        }

        Guid frameworkId;
        RiskScorer scorer;
        List<string> categories;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var store = scope.ServiceProvider.GetRequiredService<FrameworkStore>();
            var framework = await store.CurrentAsync(ct);
            frameworkId = framework.Id;
            scorer = await store.ScorerAsync(ct);
            categories = await scope.ServiceProvider.GetRequiredService<RiskDbContext>().Categories.AsNoTracking().Where(c => c.Active).Select(c => c.Code)
                .ToListAsync(ct);
        }

        if (categories.Count == 0)
        {
            categories.Add("operational");
        }

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        var prefix = "VOL" + Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        await SeedRisksAsync(connection, prefix, now, today, frameworkId, scorer, categories, unitIds, employeeIds, risks, ct);
        await SeedTasksAsync(connection, prefix, now, today, unitIds, employeeIds, tasks, ct);
    }

    private static DateTimeOffset Noon(DateOnly day) => new(day.ToDateTime(new TimeOnly(11, 30)), TimeSpan.Zero);

    private static async Task SeedRisksAsync(
        NpgsqlConnection connection, string prefix, DateTimeOffset now, DateOnly today, Guid frameworkId, RiskScorer scorer, List<string> categories,
        IReadOnlyList<Guid> unitIds, IReadOnlyList<Guid> employeeIds, int count, CancellationToken ct)
    {
        var random = new Random(1701);
        var created = now.AddDays(-90);
        var actions = new List<(Guid RiskId, int Index, Guid? Owner, string Status, DateOnly End, decimal Pct)>();
        var history = new List<(Guid RiskId, DateTimeOffset At, int L, int I, decimal Score, string Band, string Status, string Residual, decimal ResidualScore,
            string ResidualBand, int Rl, int Ri)>();
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY risk.risk (id, number, register_year, title_ar, title_en, cause, event_text, consequence, identified_on, risk_type, category_codes, status, " +
            "emerging, opportunity, unit_id, owner_employee_id, objective_codes, inherent_likelihood, inherent_impact, inherent_score, inherent_band, " +
            "residual_likelihood, residual_impact, residual_score, residual_band, residual_status, framework_version_id, response_code, within_appetite, " +
            "needs_follow_up, top10, escalated, last_review_on, next_review_on, next_review_due_at, reassessment_required, returned, closed_at, " +
            "created_at, updated_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            var row = new Row(writer);
            for (var i = 0; i < count; i++)
            {
                var id = Guid.CreateVersion7();
                var employee = i % employeeIds.Count;
                var unit = unitIds[employee % unitIds.Count];
                var status = (i % 10) switch
                {
                    7 => RiskStatuses.Draft,
                    8 => RiskStatuses.UnderReview,
                    9 => RiskStatuses.Closed,
                    _ => RiskStatuses.Active,
                };
                int l = random.Next(1, 6), im = random.Next(1, 6);
                int rl = Math.Max(1, l - random.Next(0, 3)), ri = Math.Max(1, im - random.Next(0, 2));
                var inherent = scorer.Score(l, im);
                var inherentBand = scorer.Band(inherent).Code;
                var residual = scorer.Score(rl, ri);
                var residualBand = scorer.Band(residual).Code;
                var residualStatus = scorer.ResidualStatusOf(residualBand);
                var identified = today.AddDays(-(i % 400));
                var active = status == RiskStatuses.Active;
                var nextReview = active ? (i % 10 == 0 ? today.AddDays(-5) : today.AddDays(20 + (i % 60))) : (DateOnly?)null;
                await writer.StartRowAsync(ct);
                await row.Add(id, NpgsqlDbType.Uuid, ct);
                await row.Add($"{prefix}-R{i:D5}", NpgsqlDbType.Varchar, ct);
                await row.Add(identified.Year, NpgsqlDbType.Integer, ct);
                await row.Add($"خطر {i}", NpgsqlDbType.Varchar, ct);
                await row.Add($"Volume risk {i}", NpgsqlDbType.Varchar, ct);
                await row.Add("Cause", NpgsqlDbType.Varchar, ct);
                await row.Add("Event", NpgsqlDbType.Varchar, ct);
                await row.Add("Consequence", NpgsqlDbType.Varchar, ct);
                await row.Add(identified, NpgsqlDbType.Date, ct);
                await row.Add(i % 4 == 0 ? RiskTypes.External : RiskTypes.Internal, NpgsqlDbType.Varchar, ct);
                await row.Add($"[\"{categories[i % categories.Count]}\"]", NpgsqlDbType.Jsonb, ct);
                await row.Add(status, NpgsqlDbType.Varchar, ct);
                await row.Add(false, NpgsqlDbType.Boolean, ct);
                await row.Add(false, NpgsqlDbType.Boolean, ct);
                await row.Add(unit, NpgsqlDbType.Uuid, ct);
                await row.Add(employeeIds[employee], NpgsqlDbType.Uuid, ct);
                await row.Add("[]", NpgsqlDbType.Jsonb, ct);
                await row.Add(l, NpgsqlDbType.Integer, ct);
                await row.Add(im, NpgsqlDbType.Integer, ct);
                await row.Add(inherent, NpgsqlDbType.Numeric, ct);
                await row.Add(inherentBand, NpgsqlDbType.Varchar, ct);
                await row.Add(rl, NpgsqlDbType.Integer, ct);
                await row.Add(ri, NpgsqlDbType.Integer, ct);
                await row.Add(residual, NpgsqlDbType.Numeric, ct);
                await row.Add(residualBand, NpgsqlDbType.Varchar, ct);
                await row.Add(residualStatus, NpgsqlDbType.Varchar, ct);
                await row.Add(frameworkId, NpgsqlDbType.Uuid, ct);
                await row.Add("reduce", NpgsqlDbType.Varchar, ct);
                await row.Add((i % 5) switch { 0 => false, 1 => true, _ => (bool?)null }, NpgsqlDbType.Boolean, ct);
                await row.Add(true, NpgsqlDbType.Boolean, ct);
                await row.Add(active && i % 97 == 0, NpgsqlDbType.Boolean, ct);
                await row.Add(false, NpgsqlDbType.Boolean, ct);
                await row.Add(active ? today.AddDays(-30) : null, NpgsqlDbType.Date, ct);
                await row.Add(nextReview, NpgsqlDbType.Date, ct);
                await row.Add(nextReview is { } d ? Noon(d) : null, NpgsqlDbType.TimestampTz, ct);
                await row.Add(false, NpgsqlDbType.Boolean, ct);
                await row.Add(false, NpgsqlDbType.Boolean, ct);
                await row.Add(status == RiskStatuses.Closed ? now.AddDays(-3) : null, NpgsqlDbType.TimestampTz, ct);
                await row.Add(created, NpgsqlDbType.TimestampTz, ct);
                await row.Add(created, NpgsqlDbType.TimestampTz, ct);

                if (status != RiskStatuses.Closed && i % 3 == 0)
                {
                    var (actionStatus, end, pct) = (i % 4) switch
                    {
                        0 => (ActionStatuses.InProgress, today.AddDays(-10), 0.4m),
                        1 => (ActionStatuses.NotStarted, today.AddDays(30), 0m),
                        2 => (ActionStatuses.Completed, today.AddDays(-20), 1m),
                        _ => (ActionStatuses.InProgress, today.AddDays(45), 0.6m),
                    };
                    actions.Add((id, i, employeeIds[(employee + 1) % employeeIds.Count], actionStatus, end, pct));
                }

                // Registration snapshot two months ago; every fifth active risk is re-assessed this month (movement).
                var reassessed = active && i % 5 == 0;
                var before = !reassessed ? rl : i % 2 == 0 ? (rl < 5 ? rl + 1 : rl - 1) : (rl > 1 ? rl - 1 : rl + 1);
                var beforeScore = scorer.Score(before, ri);
                history.Add((id, now.AddDays(-60), l, im, inherent, inherentBand, status, scorer.ResidualStatusOf(scorer.Band(beforeScore).Code), beforeScore,
                    scorer.Band(beforeScore).Code, before, ri));
                if (reassessed)
                {
                    history.Add((id, now.AddHours(-1), l, im, inherent, inherentBand, status, residualStatus, residual, residualBand, rl, ri));
                }
            }

            await writer.CompleteAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY risk.treatment_action (id, number, risk_id, title_ar, title_en, owner_employee_id, responsible_unit_ids, planned_start, planned_end, " +
            "actual_end, completion_pct, status, created_at, updated_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            var row = new Row(writer);
            foreach (var a in actions)
            {
                await writer.StartRowAsync(ct);
                await row.Add(Guid.CreateVersion7(), NpgsqlDbType.Uuid, ct);
                await row.Add($"{prefix}-A{a.Index:D5}", NpgsqlDbType.Varchar, ct);
                await row.Add(a.RiskId, NpgsqlDbType.Uuid, ct);
                await row.Add($"إجراء {a.Index}", NpgsqlDbType.Varchar, ct);
                await row.Add($"Volume action {a.Index}", NpgsqlDbType.Varchar, ct);
                await row.Add(a.Owner, NpgsqlDbType.Uuid, ct);
                await row.Add("[]", NpgsqlDbType.Jsonb, ct);
                await row.Add(today.AddDays(-60), NpgsqlDbType.Date, ct);
                await row.Add(a.End, NpgsqlDbType.Date, ct);
                await row.Add(a.Status == ActionStatuses.Completed ? a.End : null, NpgsqlDbType.Date, ct);
                await row.Add(a.Pct, NpgsqlDbType.Numeric, ct);
                await row.Add(a.Status, NpgsqlDbType.Varchar, ct);
                await row.Add(now.AddDays(-60), NpgsqlDbType.TimestampTz, ct);
                await row.Add(now.AddDays(-60), NpgsqlDbType.TimestampTz, ct);
            }

            await writer.CompleteAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY risk.assessment (id, risk_id, kind, inherent_likelihood, inherent_impact, inherent_score, inherent_band, residual_likelihood, residual_impact, " +
            "residual_score, residual_band, residual_status, status, framework_version_no, at, created_at, updated_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            var row = new Row(writer);
            foreach (var h in history)
            {
                await writer.StartRowAsync(ct);
                await row.Add(Guid.CreateVersion7(), NpgsqlDbType.Uuid, ct);
                await row.Add(h.RiskId, NpgsqlDbType.Uuid, ct);
                await row.Add(h.At < now.AddDays(-1) ? AssessmentKinds.Registration : AssessmentKinds.Review, NpgsqlDbType.Varchar, ct);
                await row.Add(h.L, NpgsqlDbType.Integer, ct);
                await row.Add(h.I, NpgsqlDbType.Integer, ct);
                await row.Add(h.Score, NpgsqlDbType.Numeric, ct);
                await row.Add(h.Band, NpgsqlDbType.Varchar, ct);
                await row.Add(h.Rl, NpgsqlDbType.Integer, ct);
                await row.Add(h.Ri, NpgsqlDbType.Integer, ct);
                await row.Add(h.ResidualScore, NpgsqlDbType.Numeric, ct);
                await row.Add(h.ResidualBand, NpgsqlDbType.Varchar, ct);
                await row.Add(h.Residual, NpgsqlDbType.Varchar, ct);
                await row.Add(h.Status, NpgsqlDbType.Varchar, ct);
                await row.Add(1, NpgsqlDbType.Integer, ct);
                await row.Add(h.At, NpgsqlDbType.TimestampTz, ct);
                await row.Add(h.At, NpgsqlDbType.TimestampTz, ct);
                await row.Add(h.At, NpgsqlDbType.TimestampTz, ct);
            }

            await writer.CompleteAsync(ct);
        }
    }

    private static async Task SeedTasksAsync(
        NpgsqlConnection connection, string prefix, DateTimeOffset now, DateOnly today, IReadOnlyList<Guid> unitIds, IReadOnlyList<Guid> employeeIds, int count,
        CancellationToken ct)
    {
        string[] priorities = [TaskPriorities.High, TaskPriorities.Medium, TaskPriorities.Low];
        string[] sources = ["manager-assignment", "dg-office", "committee-decision", "project", "other"];
        string[] open = [TaskStatuses.Assigned, TaskStatuses.InProgress, TaskStatuses.InProgress, TaskStatuses.OnHold, TaskStatuses.Submitted, TaskStatuses.ToDo];
        var created = now.AddDays(-70);
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY tasks.task (id, number, title_ar, title_en, type_code, template, source_code, priority, status, confidential, creator_employee_id, " +
            "assignee_employee_id, unit_id, due_date, due_at, due_soon_at, assigned_at, accepted_at, completed_at, approved_at, closed_at, " +
            "actual_seconds, time_to_accept_seconds, time_to_complete_seconds, completed_on_time, feedback_point, occurrence_count, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)", ct);
        var row = new Row(writer);
        for (var j = 0; j < count; j++)
        {
            var assignee = j % employeeIds.Count;
            var creator = (assignee + 1) % employeeIds.Count;
            var unit = unitIds[assignee % unitIds.Count];

            // ~20 % closed (two thirds on time), 2 % cancelled, 10 % overdue, the rest open and on time.
            var bucket = j % 50;
            var closed = bucket < 10;
            var cancelled = bucket is 10;
            var overdue = bucket is >= 11 and < 16;
            string status;
            if (closed)
            {
                status = j % 4 == 0 ? TaskStatuses.Done : TaskStatuses.Approved;
            }
            else if (cancelled)
            {
                status = TaskStatuses.Cancelled;
            }
            else
            {
                status = overdue ? (j % 2 == 0 ? TaskStatuses.InProgress : TaskStatuses.Assigned) : open[j % open.Length];
            }

            var template = status is TaskStatuses.ToDo or TaskStatuses.Done ? TaskTemplates.Simple : TaskTemplates.Full;
            var closedAt = closed ? now.AddDays(-(j % 60)).AddHours(-1) : (DateTimeOffset?)null;
            var onTime = closed ? j % 3 != 0 : (bool?)null;
            var due = closed
                ? DateOnly.FromDateTime(closedAt!.Value.UtcDateTime).AddDays(onTime == true ? 3 : -4)
                : overdue ? today.AddDays(-7 - (j % 20)) : today.AddDays(7 + (j % 90));
            var dueAt = Noon(due);
            var accepted = status is TaskStatuses.Assigned or TaskStatuses.Cancelled ? (DateTimeOffset?)null : created.AddHours(2 + (j % 30));
            await writer.StartRowAsync(ct);
            await row.Add(Guid.CreateVersion7(), NpgsqlDbType.Uuid, ct);
            await row.Add($"{prefix}-T{j:D6}", NpgsqlDbType.Varchar, ct);
            await row.Add($"مهمة {j}", NpgsqlDbType.Varchar, ct);
            await row.Add($"Volume task {j}", NpgsqlDbType.Varchar, ct);
            await row.Add("manager-assignment", NpgsqlDbType.Varchar, ct);
            await row.Add(template, NpgsqlDbType.Varchar, ct);
            await row.Add(sources[j % sources.Length], NpgsqlDbType.Varchar, ct);
            await row.Add(priorities[j % priorities.Length], NpgsqlDbType.Varchar, ct);
            await row.Add(status, NpgsqlDbType.Varchar, ct);
            await row.Add(j % 47 == 0, NpgsqlDbType.Boolean, ct);
            await row.Add(employeeIds[creator], NpgsqlDbType.Uuid, ct);
            await row.Add(employeeIds[assignee], NpgsqlDbType.Uuid, ct);
            await row.Add(unit, NpgsqlDbType.Uuid, ct);
            await row.Add(due, NpgsqlDbType.Date, ct);
            await row.Add(dueAt, NpgsqlDbType.TimestampTz, ct);
            await row.Add(dueAt.AddDays(-2), NpgsqlDbType.TimestampTz, ct);
            await row.Add(created, NpgsqlDbType.TimestampTz, ct);
            await row.Add(accepted, NpgsqlDbType.TimestampTz, ct);
            await row.Add(closedAt, NpgsqlDbType.TimestampTz, ct);
            await row.Add(status == TaskStatuses.Approved ? closedAt : null, NpgsqlDbType.TimestampTz, ct);
            await row.Add(closedAt, NpgsqlDbType.TimestampTz, ct);
            await row.Add(0L, NpgsqlDbType.Bigint, ct);
            await row.Add(accepted is { } a ? (long)(a - created).TotalSeconds : null, NpgsqlDbType.Bigint, ct);
            await row.Add(closedAt is { } c && accepted is { } a2 ? (long)(c - a2).TotalSeconds : null, NpgsqlDbType.Bigint, ct);
            await row.Add(onTime, NpgsqlDbType.Boolean, ct);
            await row.Add(status == TaskStatuses.Approved ? 1 + (j % 5) : null, NpgsqlDbType.Integer, ct);
            await row.Add(0, NpgsqlDbType.Integer, ct);
            await row.Add(created, NpgsqlDbType.TimestampTz, ct);
            await row.Add(closedAt ?? created, NpgsqlDbType.TimestampTz, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
