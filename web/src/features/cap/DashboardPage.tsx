import { Link } from '@tanstack/react-router';
import { Download, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber } from '@/lib/format';
import { UnitSelect } from '@/features/access/pickers';
import { AiBadge } from '@/features/ai/AiDraft';
import { useCan } from '@/features/access/session';
import { useAiFeature } from '@/features/ai/api';
import {
  barOption,
  Button,
  Chart,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  ProgressBar,
  Section,
  Tile,
} from '@/ui';
import {
  capApi,
  capReportUrl,
  useCapDashboard,
  useCapOptions,
  type CapCount,
  type CapSummaryDraft,
} from './api';
import {
  ActionStatusChip,
  CapPageLayout,
  lookupTypes,
  useCapLabels,
  useCapT,
  useDate,
  useNames,
  usePct,
  useToastMutation,
} from './components';

/** CAP dashboard (SPEC §9.7): totals, by source type / status / priority, ageing, by department, budget-dependent actions, AI summary. */
export function CapDashboardPage() {
  const { t } = useTranslation('cap');
  const { t: tc, i18n } = useTranslation();
  const { k, language } = useCapT();
  const options = useCapOptions();
  const labels = useCapLabels();
  const names = useNames();
  const pct = usePct();
  const date = useDate();
  const year0 = new Date().getFullYear();
  const [unitId, setUnitId] = useState<string>();
  const [year, setYear] = useState<number>();
  const dashboard = useCapDashboard({ ...(unitId ? { unitId } : {}), ...(year ? { year } : {}) });
  const aiFeature = useAiFeature('cap-summary');
  const aiAllowed = useCan('cap.ai.summary');
  const aiOn = aiFeature && aiAllowed;
  const summary = useToastMutation(capApi.summary);
  const [draft, setDraft] = useState<CapSummaryDraft>();

  const bars = (rows: readonly CapCount[], label: (code: string) => string, name: string) => (
    <Chart
      label={name}
      height={220}
      {...barOption({
        categories: rows.map((r) => ({ key: r.code, label: label(r.code) })),
        series: [{ name, values: rows.map((r) => r.count) }],
        integer: true,
        format: (v) => formatNumber(v, i18n.language),
      })}
    />
  );

  return (
    <CapPageLayout testId="cap-dashboard">
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            {options.data?.canExport &&
              (['register', 'departmental', 'overdue'] as const).map((kind) => (
                <Button key={kind} variant="outline" size="sm" asChild>
                  <a
                    href={capReportUrl(kind, 'xlsx', language, {
                      ...(unitId ? { unitId } : {}),
                      ...(year ? { year: String(year) } : {}),
                    })}
                    data-testid={`cap-export-${kind}`}
                  >
                    <Download aria-hidden />
                    {k(`reports.${kind}`)}
                  </a>
                </Button>
              ))}
            {aiOn && (
              <Button
                variant="outline"
                size="sm"
                loading={summary.busy}
                onClick={() => summary.run({ unitId: unitId ?? null, year: year ?? null }, setDraft)}
                data-testid="cap-ai-summary"
              >
                <Sparkles aria-hidden />
                {t('ai.summary')}
              </Button>
            )}
          </div>
        }
      />
      <div className="grid gap-3 md:grid-cols-3">
        <UnitSelect allowEmpty value={unitId} onChange={(v) => setUnitId(v === '-' ? undefined : v)} />
        <Select
          aria-label={t('dashboard.year')}
          value={year ? String(year) : 'all'}
          onValueChange={(v) => setYear(v === 'all' ? undefined : Number(v))}
          options={[
            { value: 'all', label: t('dashboard.allYears') },
            ...[year0 + 1, year0, year0 - 1, year0 - 2].map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
      </div>
      {draft && (
        <Section
          title={
            <span className="flex items-center gap-2">
              {t('ai.summary')} <AiBadge />
            </span>
          }
          testId="cap-ai-summary-draft"
        >
          <p className="whitespace-pre-line text-sm">
            {language === 'ar' ? draft.summaryAr : draft.summaryEn}
          </p>
          <p className="whitespace-pre-line text-sm text-muted-foreground">
            {language === 'ar' ? draft.lateUnitsAr : draft.lateUnitsEn}
          </p>
        </Section>
      )}
      {dashboard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />
      ) : !dashboard.data ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" data-testid="cap-tiles">
            <Tile label={t('dashboard.remarks')} value={dashboard.data.remarks} testId="cap-tile-remarks" />
            <Tile label={t('dashboard.openRemarks')} value={dashboard.data.openRemarks} />
            <Tile
              label={t('dashboard.completedRemarks')}
              value={dashboard.data.completedRemarks}
              tone="success"
            />
            <Tile label={t('dashboard.overdueRemarks')} value={dashboard.data.overdueRemarks} tone="danger" />
            <Tile label={t('dashboard.actions')} value={dashboard.data.actions} />
            <Tile label={t('dashboard.lateActions')} value={dashboard.data.lateActions} tone="danger" />
            <Tile
              label={t('dashboard.completedActions')}
              value={dashboard.data.completedActions}
              tone="success"
            />
            <Tile label={t('dashboard.budgetActions')} value={dashboard.data.budgetActions} />
          </div>
          <div className="grid gap-4 xl:grid-cols-3">
            <Section title={t('dashboard.bySourceType')}>
              {bars(
                dashboard.data.bySourceType,
                (c) => labels.lookup(lookupTypes.sourceType, c),
                t('dashboard.bySourceType'),
              )}
            </Section>
            <Section title={t('dashboard.byStatus')}>
              {bars(dashboard.data.byStatus, (c) => k(`remarkStatus.${c}`), t('dashboard.byStatus'))}
            </Section>
            <Section title={t('dashboard.byPriority')}>
              {bars(dashboard.data.byPriority, (c) => k(`priority.${c}`), t('dashboard.byPriority'))}
            </Section>
            <Section title={t('dashboard.actionsByStatus')}>
              {bars(
                dashboard.data.actionsByStatus,
                (c) => k(`actionStatus.${c}`),
                t('dashboard.actionsByStatus'),
              )}
            </Section>
            <Section title={t('dashboard.ageing')}>
              {bars(dashboard.data.ageing, (c) => t('dashboard.days', { range: c }), t('dashboard.ageing'))}
            </Section>
            <Section title={t('dashboard.effectiveness')}>
              <dl className="grid grid-cols-3 gap-2 text-center">
                <div>
                  <dt className="text-xs text-muted-foreground">{t('dashboard.effective')}</dt>
                  <dd className="text-xl font-semibold text-success">{dashboard.data.effective}</dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">{t('dashboard.notEffective')}</dt>
                  <dd className="text-xl font-semibold text-danger">{dashboard.data.notEffective}</dd>
                </div>
                <div>
                  <dt className="text-xs text-muted-foreground">{t('dashboard.awaitingVerification')}</dt>
                  <dd className="text-xl font-semibold">{dashboard.data.awaitingVerification}</dd>
                </div>
              </dl>
            </Section>
          </div>
          <Section title={t('dashboard.byDepartment')} testId="cap-by-department">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('fields.department')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.remarks')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.openRemarks')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.completedRemarks')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.overdueRemarks')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.lateActions')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.closureRate')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {dashboard.data.byDepartment.map((d, i) => (
                  <TableRow key={d.unit?.id ?? i}>
                    <TableCell>{names.unit(d.unit)}</TableCell>
                    <TableCell>{d.remarks}</TableCell>
                    <TableCell>{d.open}</TableCell>
                    <TableCell>{d.completed}</TableCell>
                    <TableCell className={d.overdue ? 'text-danger' : undefined}>{d.overdue}</TableCell>
                    <TableCell className={d.lateActions ? 'text-danger' : undefined}>
                      {d.lateActions}
                    </TableCell>
                    <TableCell>{pct(d.closureRate)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Section>
          <Section title={t('dashboard.budgetDependent')} testId="cap-budget-dependent">
            {dashboard.data.budgetDependent.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('dashboard.noBudget')}</p>
            ) : (
              <Table>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.details')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.programRequest')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.achieved')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.plannedEnd')}</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {dashboard.data.budgetDependent.map((a) => (
                    <TableRow key={a.id}>
                      <TableCell>
                        <Link
                          to="/cap/actions/$actionId"
                          params={{ actionId: a.id }}
                          className="text-primary hover:underline"
                        >
                          <bdi>{a.number}</bdi>
                        </Link>
                      </TableCell>
                      <TableCell>{names.text(a.detailsAr, a.detailsEn)}</TableCell>
                      <TableCell>
                        {a.programRequestId ? (
                          <Link
                            to="/portfolio/requests/$requestId"
                            params={{ requestId: a.programRequestId }}
                            search={{ tab: 'overview' }}
                            className="text-primary hover:underline"
                          >
                            <bdi>{a.programRequestNumber}</bdi>
                          </Link>
                        ) : (
                          '—'
                        )}
                      </TableCell>
                      <TableCell>
                        <ActionStatusChip status={a.status} />
                      </TableCell>
                      <TableCell>
                        <ProgressBar
                          value={a.achievedPct}
                          tone={a.status === 'late' ? 'danger' : 'primary'}
                          testId="cap-pct"
                        />
                      </TableCell>
                      <TableCell>{date(a.effectiveEnd)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </Section>
        </>
      )}
    </CapPageLayout>
  );
}
