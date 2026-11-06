import { Download, UsersRound } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { useSystemDashboard } from '@/features/reporting/SystemDashboard';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  CountChart,
  DashboardFrame,
  OptionalPeriodSelector,
  periodToDates,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tile,
  type PeriodValue,
} from '@/ui';
import { exportUrl, useTeamDashboard, useTeamOptions } from './api';

/** Teams dashboard (SPEC §4.6): teams by type / status, committees, workload, evaluation progress, scores, grades and rewards. */
export function TeamDashboardPage() {
  const { t, i18n } = useTranslation('teams');
  const [period, setPeriod] = useState<PeriodValue | null>(null);
  const dashboard = useTeamDashboard(period ? periodToDates(period) : undefined);
  const system = useSystemDashboard('teams.overview', period);
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
    <DashboardFrame
      testId="teams-dashboard"
      title={t('dashboardPage.title')}
      exports={system.exports}
      filters={<OptionalPeriodSelector value={period} onChange={setPeriod} allowCompare={false} />}
      actions={
        <>
          {system.action}
          {canExport && (
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
          )}
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Tile label={t('dashboardPage.teams')} value={d.teams} />
        <Tile label={t('dashboardPage.committees')} value={d.activeCommittees} />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <CountChart
          title={t('dashboardPage.byType')}
          rows={d.byType.map((x) => ({ key: x.code, label: typeName(x.code), value: x.count }))}
        />
        <CountChart
          title={t('dashboardPage.byStatus')}
          rows={d.byStatus.map((x) => ({
            key: x.code,
            label: t(`status.${x.code as 'active'}`),
            value: x.count,
          }))}
        />
        <CountChart
          title={t('dashboardPage.progress')}
          rows={d.evaluationProgress.map((x) => ({
            key: x.code,
            label: t(`evaluations.statuses.${x.code as 'open'}`),
            value: x.count,
          }))}
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
          <CountChart
            title={t('dashboardPage.scores')}
            rows={d.scoreDistribution.map((x) => ({ key: x.code, label: x.code, value: x.count }))}
          />
        )}
        {d.gradeDistribution && (
          <CountChart
            title={t('dashboardPage.grades')}
            rows={d.gradeDistribution.map((x) => ({ key: x.code, label: gradeName(x.code), value: x.count }))}
          />
        )}
        {d.rewardsByYear && (
          <CountChart
            title={t('dashboardPage.rewardsByYear')}
            rows={d.rewardsByYear.map((x) => ({ key: x.code, label: x.code, value: x.amount }))}
            integer={false}
          />
        )}
      </div>
    </DashboardFrame>
  );
}
