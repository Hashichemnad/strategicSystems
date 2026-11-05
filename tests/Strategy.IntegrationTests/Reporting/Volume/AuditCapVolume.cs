using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>
/// Test-only volume seeding for the executive dashboard performance test (Phase 17): audit findings with recommendations over a few issued
/// reports, CAP remarks with actions over a few sources — set-based SQL (generate_series) in one transaction per module, a few seconds for
/// 2 000 + 2 000. Rows keep the modules' invariants (status ↔ dates / completion, action ↔ remark links, numbers unique per call) so the
/// reporting sources and dashboards compute on them as on real data. Business audit entries are not written (setup data, not user actions).
/// </summary>
internal static class AuditCapVolume
{
    public static async Task SeedAsync(
        ApiFactory factory, IReadOnlyList<Guid> unitIds, IReadOnlyList<Guid> employeeIds, int findings = 2000, int remarks = 2000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfZero(unitIds.Count);
        ArgumentOutOfRangeException.ThrowIfZero(employeeIds.Count);
        var tag = "V" + Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var scope = TestAccess.SystemScope(factory);
        if (findings > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditMgmtDbContext>();
            await RunAsync(db, AuditSql(Table<AuditReport>(db), Table<Finding>(db), Table<Recommendation>(db)), tag, today, unitIds, employeeIds, findings, ct);
        }

        if (remarks > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<CapDbContext>();
            await RunAsync(db, CapSql(Table<CapSource>(db), Table<Remark>(db), Table<CapAction>(db), Table<RemarkAction>(db)), tag, today, unitIds, employeeIds, remarks, ct);
        }
    }

    private static string Table<T>(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(T))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    /// <summary>Runs the statements on the context's connection in one transaction (test-only setup; no EF formatting of the SQL).</summary>
    private static async Task RunAsync(DbContext db, string sql, string tag, DateOnly today, IReadOnlyList<Guid> units, IReadOnlyList<Guid> employees, int count, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
            command.Parameters.AddWithValue("tag", tag);
            command.Parameters.AddWithValue("today", today);
            command.Parameters.AddWithValue("units", units.ToArray());
            command.Parameters.AddWithValue("emps", employees.ToArray());
            command.Parameters.AddWithValue("n", count);
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Five issued reports; finding i: status by i % 20 (open 40 %, late 20 %, closed 20 %, risk accepted 10 %, cancelled 5 %, open 5 %), priority by
    /// i % 4, finding date spread over ~700 days (every age bucket); 1–3 recommendations matching the finding (late → late and past due, closed →
    /// completed, risk accepted → risk accepted, cancelled → cancelled; open → not started / continuing, every 7th past due, every 11th with a
    /// follow-up due within the week).
    /// </summary>
    private static string AuditSql(string report, string finding, string recommendation) => $$"""
        INSERT INTO {{report}} (id, number, title_ar, title_en, type_code, auditor_body, lead_auditor_employee_id, audited_unit_ids, period_from, period_to,
            report_date, distribution_employee_ids, status, issued_at, created_at, updated_at)
        SELECT gen_random_uuid(), @tag || '-R' || g, 'تقرير تدقيق ' || g, 'Audit report ' || g,
            (ARRAY['internal-audit','external-audit','special-report','revenue-audit','consultancy'])[g], 'Internal Audit Office',
            @emps[1 + (g % cardinality(@emps))], '[]'::jsonb, @today - 730, @today - 30, @today - 20, '[]'::jsonb, 'under_follow_up', now(), now(), now()
        FROM generate_series(1, 5) g;

        WITH reports AS (SELECT array_agg(id ORDER BY number) AS ids FROM {{report}} WHERE number LIKE @tag || '-R%'),
        src AS (
            SELECT i, gen_random_uuid() AS id, @today - ((i * 37) % 700) AS on_date,
                CASE WHEN i % 20 IN (0, 1, 2, 3, 4, 5, 6, 7) THEN 'open' WHEN i % 20 IN (8, 9, 10, 11) THEN 'late' WHEN i % 20 IN (12, 13, 14, 15) THEN 'closed'
                     WHEN i % 20 IN (16, 17) THEN 'risk_accepted' WHEN i % 20 = 18 THEN 'cancelled' ELSE 'open' END AS status
            FROM generate_series(1, @n) i),
        f AS (
            INSERT INTO {{finding}} (id, number, report_id, title_ar, title_en, type_code, condition, criteria, cause, effect, finding_date, year,
                responsible_unit_id, department_unit_id, priority, repeat, expected_closing_on, actual_closed_on, status, completion, auditor_employee_id,
                cancel_reason, created_at, updated_at)
            SELECT s.id, @tag || '-F' || s.i, (SELECT ids FROM reports)[1 + s.i % 5], 'ملاحظة ' || s.i, 'Finding ' || s.i, 'finding', 'Condition', 'Criteria',
                'Cause', 'Effect', s.on_date, extract(year FROM s.on_date)::int, @units[1 + s.i % cardinality(@units)], @units[1 + s.i % cardinality(@units)],
                (ARRAY['low','medium','high','critical'])[1 + s.i % 4], s.i % 15 = 0, s.on_date + 90,
                CASE WHEN s.status IN ('closed', 'risk_accepted') THEN least(s.on_date + 45, @today) END, s.status,
                CASE WHEN s.status = 'closed' THEN 1 WHEN s.status = 'open' THEN 0.3 ELSE 0.5 END, @emps[1 + s.i % cardinality(@emps)],
                CASE WHEN s.status = 'cancelled' THEN 'Volume seed' END, now(), now()
            FROM src s)
        INSERT INTO {{recommendation}} (id, number, finding_id, action_ar, action_en, management_response_code, implementation_plan, due_on, status, latest_pct,
            completed_on, next_follow_up_on, risk_acceptance_justification, risk_accepted_on, cancel_reason, created_at, updated_at)
        SELECT gen_random_uuid(), @tag || '-A' || s.i || '-' || k, s.id, 'توصية ' || s.i || '-' || k, 'Recommendation ' || s.i || '-' || k, 'agree', 'Plan',
            CASE WHEN s.status = 'late' OR (s.status = 'open' AND s.i % 7 = 0 AND k = 1) THEN @today - 5 - (s.i % 60)
                 WHEN s.status IN ('closed', 'risk_accepted') THEN s.on_date + 40 ELSE @today + 10 + (s.i % 120) END,
            CASE s.status WHEN 'late' THEN 'late' WHEN 'closed' THEN 'completed' WHEN 'risk_accepted' THEN 'risk_accepted' WHEN 'cancelled' THEN 'cancelled'
                 ELSE CASE WHEN (s.i + k) % 2 = 0 THEN 'continuing' ELSE 'not_started' END END,
            CASE s.status WHEN 'closed' THEN 1 WHEN 'late' THEN 0.4 WHEN 'open' THEN CASE WHEN (s.i + k) % 2 = 0 THEN 0.5 ELSE 0 END ELSE 0 END,
            CASE WHEN s.status = 'closed' THEN least(s.on_date + 45, @today) END,
            CASE WHEN s.status IN ('open', 'late') THEN CASE WHEN s.i % 11 = 0 THEN @today + 3 ELSE @today + 30 END END,
            CASE WHEN s.status = 'risk_accepted' THEN 'Accepted by the committee' END, CASE WHEN s.status = 'risk_accepted' THEN least(s.on_date + 45, @today) END,
            CASE WHEN s.status = 'cancelled' THEN 'Volume seed' END, now(), now()
        FROM src s CROSS JOIN LATERAL generate_series(1, 1 + s.i % 3) k;
        """;

    /// <summary>
    /// Four sources (rotating source types); remark i: status by i % 10 (assigned, planning, validation, implementation ×2, verification, completed ×2,
    /// rejected, cancelled), priority / type by i, assigned over ~600 days (old open ones overdue). Planned remarks get 1–2 actions linked to them:
    /// draft (planning), pending (validation), approved and not started / in progress / late (implementation), completed — on time (k = 1) or
    /// late (k = 2) — for verification and completed remarks.
    /// </summary>
    private static string CapSql(string source, string remark, string action, string link) => $$"""
        INSERT INTO {{source}} (id, number, title_ar, title_en, source_type_code, description, received_on, owner_employee_id, origin, viewer_employee_ids, status,
            created_at, updated_at)
        SELECT gen_random_uuid(), @tag || '-S' || g, 'مصدر ' || g, 'Source ' || g,
            (ARRAY['{{CapSourceTypes.Workshop}}','{{CapSourceTypes.InternalAudit}}','{{CapSourceTypes.CustomerComplaint}}','{{CapSourceTypes.ManagementReview}}'])[g],
            'Volume seed', @today - 650, @emps[1 + (g % cardinality(@emps))], 'manual', '[]'::jsonb, 'active', now(), now()
        FROM generate_series(1, 4) g;

        WITH sources AS (SELECT array_agg(id ORDER BY number) AS ids FROM {{source}} WHERE number LIKE @tag || '-S%'),
        src AS (
            SELECT i, gen_random_uuid() AS id, @today - ((i * 41) % 600) AS assigned_on,
                (ARRAY['assigned','planning','validation','implementation','implementation','verification','completed','completed','rejected','cancelled'])[1 + i % 10]
                    AS status
            FROM generate_series(1, @n) i),
        r AS (
            INSERT INTO {{remark}} (id, number, source_id, text_ar, text_en, type_code, priority, department_unit_id, owner_employee_id, assigned_on, due_on, due_at,
                responded_at, status, rejection_reason, root_cause, action_seq, all_rejected, reopened_count, completed_at, cancel_reason, created_at, updated_at)
            SELECT s.id, @tag || '-C' || s.i, (SELECT ids FROM sources)[1 + s.i % 4], 'ملاحظة ' || s.i, 'Remark ' || s.i,
                (ARRAY['{{CapRemarkTypes.Nonconformity}}','{{CapRemarkTypes.Observation}}','{{CapRemarkTypes.Improvement}}'])[1 + s.i % 3],
                (ARRAY['low','medium','high','critical'])[1 + (s.i / 3) % 4], @units[1 + s.i % cardinality(@units)], @emps[1 + s.i % cardinality(@emps)],
                s.assigned_on, s.assigned_on + 90, (s.assigned_on + 91)::timestamp AT TIME ZONE 'UTC',
                CASE WHEN s.status <> 'assigned' THEN (s.assigned_on + 3)::timestamp AT TIME ZONE 'UTC' END, s.status,
                CASE WHEN s.status = 'rejected' THEN 'Not applicable' END, '{}'::jsonb,
                CASE WHEN s.status IN ('planning','validation','implementation','verification','completed') THEN 1 + s.i % 2 ELSE 0 END, false, 0,
                CASE WHEN s.status = 'completed' THEN least(s.assigned_on + 60, @today)::timestamp AT TIME ZONE 'UTC' END,
                CASE WHEN s.status = 'cancelled' THEN 'Volume seed' END, now(), now()
            FROM src s),
        a AS (
            SELECT s.i, k, s.id AS remark_id, gen_random_uuid() AS id, s.assigned_on,
                CASE s.status WHEN 'planning' THEN 'draft' WHEN 'validation' THEN 'pending' ELSE 'approved' END AS validation,
                CASE WHEN s.status IN ('verification', 'completed') THEN 'completed'
                     WHEN s.status = 'implementation' THEN (ARRAY['not_started','in_progress','late'])[1 + (s.i + k) % 3] ELSE 'not_started' END AS status
            FROM src s CROSS JOIN LATERAL generate_series(1, 1 + s.i % 2) k
            WHERE s.status IN ('planning','validation','implementation','verification','completed')),
        ins AS (
            INSERT INTO {{action}} (id, number, home_remark_id, details_ar, details_en, type_code, budget_required, budget_available, estimated_cost, currency_code,
                assigned_employee_id, planned_start, planned_end, validation_status, actual_start, actual_end, achieved_pct, status, completed_at, created_at, updated_at)
            SELECT a.id, @tag || '-C' || a.i || '-' || a.k, a.remark_id, 'إجراء ' || a.i || '-' || a.k, 'Action ' || a.i || '-' || a.k, 'corrective',
                a.i % 6 = 0, CASE WHEN a.i % 6 = 0 THEN a.i % 12 = 0 END, CASE WHEN a.i % 6 = 0 THEN 10000 + a.i END, 'AED',
                @emps[1 + (a.i + a.k) % cardinality(@emps)], a.assigned_on + 5,
                CASE WHEN a.status = 'late' THEN @today - 10 WHEN a.status = 'completed' THEN a.assigned_on + 50 ELSE @today + 60 END,
                a.validation, CASE WHEN a.status <> 'not_started' THEN a.assigned_on + 5 END,
                CASE WHEN a.status = 'completed' THEN a.assigned_on + 50 + CASE WHEN a.k = 1 THEN -5 ELSE 10 END END,
                CASE a.status WHEN 'completed' THEN 1 WHEN 'in_progress' THEN 0.5 WHEN 'late' THEN 0.3 ELSE 0 END, a.status,
                CASE WHEN a.status = 'completed' THEN (a.assigned_on + 50 + CASE WHEN a.k = 1 THEN -5 ELSE 10 END)::timestamp AT TIME ZONE 'UTC' END, now(), now()
            FROM a)
        INSERT INTO {{link}} (remark_id, action_id, linked_at)
        SELECT a.remark_id, a.id, now() FROM a;
        """;
}
