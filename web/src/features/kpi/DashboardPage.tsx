import { Link } from '@tanstack/react-router';
import { Download, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSystemDashboard } from '@/features/reporting/SystemDashboard';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useAiFeature } from '@/features/ai/api';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  CountChart,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tile,
  useToast,
  DashboardFrame,
  OptionalPeriodSelector,
  periodToDates,
  type PeriodValue,
} from '@/ui';
import {
  currentYear,
  exportUrl,
  kpiApi,
  ragOf,
  useKpiDashboard,
  useKpiOptions,
  type KpiDashboard,
} from './api';
import { KpiPage, PerformanceValue, RagChip, ScoreBar, useKpiT, yearOptions } from './components';
import { DataQualityTable } from './ReportsPage';

/** KPI home dashboard (SPEC §2.5): totals, by type / department / objective, achievement distribution, backlog, top / bottom performers. */
export function KpiDashboardPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const [year, setYear] = useState(currentYear());
  const [period, setPeriod] = useState<PeriodValue | null>(null);
  const range = period ? periodToDates(period) : undefined;
  const dashboard = useKpiDashboard(year, range);
  const system = useSystemDashboard('kpi.scorecard', period);
  const canExport = useCan('kpi.report.export');
  const label = (row: { key: string; labelAr?: string | null; labelEn?: string | null }) =>
    pickText(language, row.labelAr, row.labelEn) || row.key;

  return (
    <KpiPage testId="kpi-dashboard">
      <DashboardFrame
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        exports={system.exports}
        filters={
          <>
            <Select
              aria-label={t('common.year')}
              value={String(year)}
              onValueChange={(v) => setYear(Number(v))}
              options={yearOptions(currentYear())}
              className="w-28"
            />
            <OptionalPeriodSelector value={period} onChange={setPeriod} allowCompare={false} />
          </>
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {system.action}
            {canExport && (
              <Button variant="outline" size="sm" asChild>
                <a href={exportUrl('scorecard', 'xlsx', language, { year: String(year) })}>
                  <Download aria-hidden />
                  {t('reports.exportScorecard')}
                </a>
              </Button>
            )}
          </div>
        }
      >
        {dashboard.isError && <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />}
        {dashboard.isPending ? (
          <Skeleton className="h-96 rounded-lg" />
        ) : (
          dashboard.data && (
            <>
              <Tiles data={dashboard.data} />
              <div className="grid gap-4 lg:grid-cols-2">
                <CountChart
                  title={t('dashboard.achievement')}
                  testId="kpi-achievement"
                  rows={dashboard.data.achievement.map((r) => ({
                    key: r.key,
                    label: k(`dashboard.achievementKeys.${r.key}`),
                    value: r.count,
                  }))}
                />
                <CountChart
                  title={t('dashboard.byType')}
                  rows={dashboard.data.byType.map((r) => ({ key: r.key, label: label(r), value: r.count }))}
                />
                <ScoredList
                  title={t('dashboard.byDepartment')}
                  rows={dashboard.data.byDepartment}
                  label={label}
                />
                <ScoredList
                  title={t('dashboard.byObjective')}
                  rows={dashboard.data.byObjective}
                  label={label}
                />
                <Performers title={t('dashboard.top')} rows={dashboard.data.top} />
                <Performers title={t('dashboard.bottom')} rows={dashboard.data.bottom} />
              </div>
              <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
                <h2 className="font-semibold">{t('reports.dataQuality')}</h2>
                <DataQualityTable year={year} />
              </section>
              <Narrative year={year} />
            </>
          )
        )}
      </DashboardFrame>
    </KpiPage>
  );
}

function Tiles({ data }: { data: KpiDashboard }) {
  const { t, i18n } = useTranslation('kpi');
  const n = (v: number) => formatNumber(v, i18n.language);
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" data-testid="kpi-tiles">
      <Tile label={t('dashboard.totalCards')} value={`${n(data.activeCards)} / ${n(data.totalCards)}`} />
      <Tile
        label={t('dashboard.organisationScore')}
        value={
          <span className="flex items-center gap-2">
            <PerformanceValue capped={data.organisationScore} />
            <RagChip rag={data.organisationRag} />
          </span>
        }
      />
      <Tile label={t('dashboard.pendingUpdates')} value={data.pendingUpdates} to="/kpi/pending" />
      <Tile label={t('dashboard.overdue')} value={data.overdue} />
      <Tile label={t('dashboard.approvalBacklog')} value={data.approvalBacklog} to="/kpi/approvals" />
      <Tile label={t('dashboard.approvedResults')} value={data.approvedResults} />
    </div>
  );
}

function ScoredList({
  title,
  rows,
  label,
}: {
  title: string;
  rows: KpiDashboard['byDepartment'];
  label: (row: KpiDashboard['byDepartment'][number]) => string;
}) {
  const { t } = useTranslation('kpi');
  const { i18n } = useTranslation();
  const options = useKpiOptions();
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title}>
      <h2 className="font-semibold">{title}</h2>
      {rows.length === 0 && <p className="text-sm text-muted-foreground">{t('common.noData')}</p>}
      <ul className="flex flex-col gap-2">
        {rows.map((row) => {
          const rag = ragOf(row.score, options.data?.greenFrom ?? 1, options.data?.amberFrom ?? 1);
          return (
            <li
              key={row.key}
              className="grid grid-cols-[minmax(6rem,12rem)_1fr_auto] items-center gap-2 text-sm"
            >
              <span className="truncate" title={label(row)}>
                {label(row)}
              </span>
              <ScoreBar score={row.score} rag={rag} />
              <span className="flex items-center gap-2 text-muted-foreground">
                <PerformanceValue capped={row.score} />
                <span>
                  ·{' '}
                  {t('common.kpiCount', {
                    count: row.count,
                    formatted: formatNumber(row.count, i18n.language),
                  })}
                </span>
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function Performers({ title, rows }: { title: string; rows: KpiDashboard['top'] }) {
  const { t } = useTranslation('kpi');
  const { language } = useKpiT();
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title}>
      <h2 className="font-semibold">{title}</h2>
      {rows.length === 0 && <p className="text-sm text-muted-foreground">{t('common.noData')}</p>}
      <ul className="flex flex-col divide-y">
        {rows.map((row) => (
          <li key={row.cardId} className="flex items-center justify-between gap-2 py-2 text-sm">
            <Link
              to="/kpi/cards/$cardId"
              params={{ cardId: row.cardId }}
              className="truncate text-primary hover:underline"
            >
              <bdi>{row.code}</bdi> · {pickText(language, row.nameAr, row.nameEn)}
            </Link>
            <span className="flex items-center gap-2">
              <PerformanceValue capped={row.capped} actual={row.actual} />
              <RagChip rag={row.rag} />
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** AI performance narrative (SPEC §2.9): a bilingual draft the user copies into their report. */
export function Narrative({ year, unitId }: { year: number; unitId?: string }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const enabled = useAiFeature('kpi-narrative');
  const [draft, setDraft] = useState<{ reportAr: string; reportEn: string }>();
  const [busy, setBusy] = useState(false);
  if (!enabled) return null;
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-narrative">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('ai.narrative.title')}</h2>
        <Button
          variant="outline"
          size="sm"
          loading={busy}
          onClick={() => {
            setBusy(true);
            kpiApi
              .narrative({ year, unitId: unitId ?? null })
              .then(setDraft)
              .catch((e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) }))
              .finally(() => setBusy(false));
          }}
        >
          <Sparkles aria-hidden />
          {t('ai.narrative.action')}
        </Button>
      </header>
      {draft && (
        <div className="grid gap-4 md:grid-cols-2">
          <StatusChip tone="info" className="md:col-span-2 w-fit">
            {t('ai.draftLabel')}
          </StatusChip>
          <article dir="rtl" lang="ar" className="whitespace-pre-line rounded-md border p-3 text-sm">
            {draft.reportAr}
          </article>
          <article dir="ltr" lang="en" className="whitespace-pre-line rounded-md border p-3 text-sm">
            {draft.reportEn}
          </article>
        </div>
      )}
    </section>
  );
}
