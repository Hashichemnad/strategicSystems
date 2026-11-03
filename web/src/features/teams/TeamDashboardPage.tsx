import { Download, UsersRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { formatNumber, pickText } from '@/lib/format';
import { Button, PageHeader, Skeleton, StatusBanner, StatusChip } from '@/ui';
import { exportUrl, useTeamDashboard, useTeamOptions } from './api';

interface Row {
  key: string;
  value: number;
}

/** Accessible CSS bars from the design tokens (as the task dashboard; ECharts arrives with the reporting phase). */
function Bars({ title, rows, label }: { title: string; rows: Row[]; label: (key: string) => string }) {
  const { i18n } = useTranslation();
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title}>
      <h2 className="font-semibold">{title}</h2>
      <ul className="flex flex-col gap-2">
        {rows.map((row) => (
          <li
            key={row.key}
            className="grid grid-cols-[minmax(6rem,12rem)_1fr_auto] items-center gap-2 text-sm"
          >
            <span className="truncate" title={label(row.key)}>
              {label(row.key)}
            </span>
            <span className="flex h-4 overflow-hidden rounded bg-muted" aria-hidden>
              <span className="h-full bg-sky-500" style={{ inlineSize: `${(row.value / max) * 100}%` }} />
            </span>
            <span className="tabular-nums text-muted-foreground">
              {formatNumber(row.value, i18n.language)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Teams dashboard (SPEC §4.6): teams by type / status, committees, workload, evaluation progress, scores, grades and rewards. */
export function TeamDashboardPage() {
  const { t, i18n } = useTranslation('teams');
  const dashboard = useTeamDashboard();
  const options = useTeamOptions();
  const canExport = useCan('teams.report.export');
  if (dashboard.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (dashboard.isError) return <StatusBanner tone="danger" title={t('dashboardPage.title')} />;
  const d = dashboard.data;
  const typeName = (code: string) => {
    const type = options.data?.types.find((x) => x.code === code);
    return type ? pickText(i18n.language, type.nameAr, type.nameEn) : code;
  };
  const gradeName = (code: string) => {
    const band = options.data?.grades.find((g) => g.code === code);
    return band ? pickText(i18n.language, band.nameAr, band.nameEn) : code;
  };
  return (
    <div className="flex flex-col gap-6" data-testid="teams-dashboard">
      <PageHeader
        title={t('dashboardPage.title')}
        actions={
          canExport && (
            <div className="flex flex-wrap gap-2">
              {(['register', 'membership', 'evaluations', 'rewards'] as const).map((kind) => (
                <Button key={kind} variant="outline" size="sm" asChild>
                  <a href={exportUrl(kind, 'xlsx', i18n.language)}>
                    <Download aria-hidden />
                    {t(`dashboardPage.exports.${kind}`)}
                  </a>
                </Button>
              ))}
            </div>
          )
        }
      />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[
          [t('dashboardPage.teams'), d.teams],
          [t('dashboardPage.committees'), d.activeCommittees],
        ].map(([label, value]) => (
          <div key={String(label)} className="rounded-lg border bg-card p-5">
            <p className="text-3xl font-semibold tabular-nums">
              {formatNumber(Number(value), i18n.language)}
            </p>
            <p className="text-sm text-muted-foreground">{label}</p>
          </div>
        ))}
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <Bars
          title={t('dashboardPage.byType')}
          rows={d.byType.map((x) => ({ key: x.code, value: x.count }))}
          label={typeName}
        />
        <Bars
          title={t('dashboardPage.byStatus')}
          rows={d.byStatus.map((x) => ({ key: x.code, value: x.count }))}
          label={(k) => t(`status.${k as 'active'}`)}
        />
        <Bars
          title={t('dashboardPage.progress')}
          rows={d.evaluationProgress.map((x) => ({ key: x.code, value: x.count }))}
          label={(k) => t(`evaluations.statuses.${k as 'open'}`)}
        />
        <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
          <h2 className="flex items-center gap-2 font-semibold">
            <UsersRound className="size-4" aria-hidden />
            {t('dashboardPage.workload')}
          </h2>
          <ul className="flex flex-col gap-2 text-sm">
            {d.workload.map((w) => (
              <li key={w.employee.id} className="flex items-center justify-between gap-2">
                <span>{pickText(i18n.language, w.employee.nameAr, w.employee.nameEn)}</span>
                <span className="flex items-center gap-2 tabular-nums">
                  {formatNumber(w.activeMemberships, i18n.language)}
                  {w.overAssigned && <StatusChip tone="warning">{t('form.overAssigned')}</StatusChip>}
                </span>
              </li>
            ))}
          </ul>
        </section>
        {d.scoreDistribution && (
          <Bars
            title={t('dashboardPage.scores')}
            rows={d.scoreDistribution.map((x) => ({ key: x.code, value: x.count }))}
            label={(k) => k}
          />
        )}
        {d.gradeDistribution && (
          <Bars
            title={t('dashboardPage.grades')}
            rows={d.gradeDistribution.map((x) => ({ key: x.code, value: x.count }))}
            label={gradeName}
          />
        )}
        {d.rewardsByYear && (
          <Bars
            title={t('dashboardPage.rewardsByYear')}
            rows={d.rewardsByYear.map((x) => ({ key: x.code, value: x.amount }))}
            label={(k) => k}
          />
        )}
      </div>
    </div>
  );
}
