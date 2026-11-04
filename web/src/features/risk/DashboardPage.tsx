import { Link } from '@tanstack/react-router';
import { Sparkles } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { formatDate, formatNumber } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  residualStatuses,
  riskApi,
  useHeatCell,
  useRiskDashboard,
  useRiskOptions,
  type ReportDraft,
  type RiskDashboard,
  type RiskFilterQuery,
  type RiskListItem,
} from './api';
import { HeatMap, ProgressBar, RatingChip, RiskPageLayout, useAxisLabels, useBands, useNames, useNum, useRiskLabels, useRiskT } from './components';

const ALL = '__all';

/** Risk dashboard (docs/design/risk.md §12): tiles, inherent vs residual heat-maps, breakdowns, Top-10, appetite, overdue, movement, KRIs. */
export function RiskDashboardPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const options = useRiskOptions();
  const labels = useRiskLabels();
  const canReport = useCan('risk.ai.report');
  const [unitId, setUnitId] = useState<string>();
  const [category, setCategory] = useState<string>();
  const [residualStatus, setResidualStatus] = useState<string>();
  const [year, setYear] = useState<number>();
  const [includeProject, setIncludeProject] = useState<boolean>();
  const include = includeProject ?? options.data?.includeProjectRisksByDefault ?? true;
  const filter: RiskFilterQuery = {
    ...(unitId ? { unitId } : {}),
    ...(category ? { category } : {}),
    ...(residualStatus ? { residualStatus } : {}),
    ...(year ? { year } : {}),
    includeProjectRisks: include,
  };
  const dashboard = useRiskDashboard(filter, options.isSuccess);
  const [cell, setCell] = useState<{ kind: 'inherent' | 'residual'; likelihood: number; impact: number }>();
  const [report, setReport] = useState(false);
  const thisYear = new Date().getFullYear();

  return (
    <RiskPageLayout testId="risk-dashboard">
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={
          canReport && (
            <Button variant="outline" onClick={() => setReport(true)} data-testid="risk-ai-report">
              <Sparkles aria-hidden />
              {t('ai.report.action')}
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5" data-testid="risk-dashboard-filters">
        <UnitSelect allowEmpty value={unitId} onChange={setUnitId} />
        <Select
          aria-label={t('fields.category')}
          value={category ?? ALL}
          onValueChange={(v) => setCategory(v === ALL ? undefined : v)}
          options={[
            { value: ALL, label: t('register.allCategories') },
            ...(options.data?.categories ?? []).map((c) => ({ value: c.code, label: labels.category(c.code) })),
          ]}
        />
        <Select
          aria-label={t('fields.residualStatus')}
          value={residualStatus ?? ALL}
          onValueChange={(v) => setResidualStatus(v === ALL ? undefined : v)}
          options={[
            { value: ALL, label: t('dashboard.allResidualStatuses') },
            ...residualStatuses.map((s) => ({ value: s, label: k(`residualStatus.${s}`) })),
          ]}
        />
        <Select
          aria-label={t('dashboard.year')}
          value={year ? String(year) : ALL}
          onValueChange={(v) => setYear(v === ALL ? undefined : Number(v))}
          options={[
            { value: ALL, label: t('dashboard.allYears') },
            ...[0, 1, 2, 3, 4].map((d) => ({ value: String(thisYear - d), label: String(thisYear - d) })),
          ]}
        />
        <Switch
          checked={include}
          onCheckedChange={setIncludeProject}
          label={t('dashboard.includeProjectRisks')}
        />
      </div>
      {dashboard.isError && <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />}
      {!dashboard.data ? (
        !dashboard.isError && <Skeleton className="h-96 rounded-lg" />
      ) : (
        <DashboardBody
          data={dashboard.data}
          cell={cell}
          onCell={(kind, likelihood, impact) => setCell({ kind, likelihood, impact })}
          filter={filter}
        />
      )}
      {report && <ReportDialog unitId={unitId} year={year} onClose={() => setReport(false)} />}
    </RiskPageLayout>
  );
}

function DashboardBody({
  data,
  cell,
  onCell,
  filter,
}: {
  data: RiskDashboard;
  cell: { kind: 'inherent' | 'residual'; likelihood: number; impact: number } | undefined;
  onCell: (kind: 'inherent' | 'residual', likelihood: number, impact: number) => void;
  filter: RiskFilterQuery;
}) {
  const { t } = useTranslation('risk');
  const { k } = useRiskT();
  const labels = useRiskLabels();
  const { label: bandLabel } = useBands(data.bands);
  const names = useNames();
  const axis = useAxisLabels();
  const { i18n } = useTranslation();
  const n = (v: number) => formatNumber(v, i18n.language);
  const tiles: [string, string, number, string?][] = [
    ['total', t('dashboard.tiles.total'), data.total],
    ['active', t('dashboard.tiles.active'), data.active],
    ['underReview', t('dashboard.tiles.underReview'), data.underReview],
    ['projectRisks', t('dashboard.tiles.projectRisks'), data.projectRisks],
    ['outsideAppetite', t('dashboard.tiles.outsideAppetite'), data.outsideAppetiteCount, 'danger'],
    ['overdueActions', t('dashboard.tiles.overdueActions'), data.overdueActionsCount, 'danger'],
    ['overdueReviews', t('dashboard.tiles.overdueReviews'), data.overdueReviewsCount, 'danger'],
  ];
  return (
    <>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7" data-testid="risk-tiles">
        {tiles.map(([key, label, value, tone]) => (
          <div key={key} className="flex flex-col gap-1 rounded-lg border bg-card p-4" data-testid={`risk-tile-${key}`}>
            <span className="text-sm text-muted-foreground">{label}</span>
            <span className={tone === 'danger' && value > 0 ? 'text-2xl font-semibold text-danger' : 'text-2xl font-semibold'}>
              {n(value)}
            </span>
          </div>
        ))}
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        {(['inherent', 'residual'] as const).map((kind) => (
          <section key={kind} className="flex flex-col gap-3 rounded-lg border bg-card p-5">
            <h2 className="font-semibold">{t(`heatmap.${kind}`)}</h2>
            <HeatMap
              kind={kind}
              cells={data[kind]}
              bands={data.bands}
              axis={axis}
              selected={cell?.kind === kind ? cell : undefined}
              onSelect={(l, i) => onCell(kind, l, i)}
            />
          </section>
        ))}
      </div>
      {cell && <CellRisks cell={cell} filter={filter} />}
      <div className="grid gap-4 lg:grid-cols-2">
        <CountBars
          title={t('dashboard.byResidualBand')}
          testId="risk-by-band"
          rows={data.byResidualBand.map((r) => ({ key: r.code, label: bandLabel(r.code), value: r.count }))}
        />
        <CountBars
          title={t('dashboard.byCategory')}
          testId="risk-by-category"
          rows={data.byCategory.map((r) => ({ key: r.code, label: labels.category(r.code), value: r.count }))}
        />
        <CountBars
          title={t('dashboard.byUnit')}
          rows={data.byUnit.map((r) => ({ key: r.code, label: names.unit(data.units[r.code]), value: r.count }))}
        />
        <CountBars
          title={t('dashboard.byResidualStatus')}
          rows={data.byResidualStatus.map((r) => ({ key: r.code, label: k(`residualStatus.${r.code}`), value: r.count }))}
        />
      </div>
      <Panel title={t('dashboard.top10')} testId="risk-top10">
        <RiskTable rows={data.top10} bands={data.bands} rank />
      </Panel>
      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title={t('dashboard.outsideAppetite')} testId="risk-outside-appetite">
          <RiskTable rows={data.outsideAppetite} bands={data.bands} />
        </Panel>
        <Panel title={t('dashboard.overdueReviews')} testId="risk-overdue-reviews">
          <RiskTable rows={data.overdueReviews} bands={data.bands} review />
        </Panel>
        <Panel title={t('dashboard.overdueActions')} testId="risk-overdue-actions">
          {data.overdueActions.length === 0 ? (
            <Empty />
          ) : (
            <ul className="flex flex-col divide-y">
              {data.overdueActions.map((a) => (
                <li key={a.id} className="flex flex-col gap-1 py-2 text-sm">
                  <Link
                    to="/risk/risks/$riskId"
                    params={{ riskId: a.riskId }}
                    search={{ tab: 'actions' }}
                    className="font-medium text-primary hover:underline"
                  >
                    <bdi>{a.number}</bdi> · {names.text(a.titleAr, a.titleEn)}
                  </Link>
                  <span className="flex flex-wrap items-center gap-3 text-muted-foreground">
                    <bdi>{a.riskNumber}</bdi>
                    <span>{names.person(a.owner)}</span>
                    {a.plannedEnd && (
                      <span className="text-danger">
                        {t('fields.plannedEnd')}: {formatDate(a.plannedEnd, i18n.language)}
                      </span>
                    )}
                    <ProgressBar ratio={a.completionPct} label={t('treatment.progress')} />
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title={t('dashboard.movement')} testId="risk-movement">
          <div className="flex flex-wrap gap-4 text-sm">
            <span>
              {t('dashboard.improved')}: <strong className="text-success">{n(data.movement.improved)}</strong>
            </span>
            <span>
              {t('dashboard.worsened')}: <strong className="text-danger">{n(data.movement.worsened)}</strong>
            </span>
            <span>
              {t('dashboard.unchanged')}: <strong>{n(data.movement.unchanged)}</strong>
            </span>
          </div>
          {data.movement.worsenedRisks.length > 0 && <RiskTable rows={data.movement.worsenedRisks} bands={data.bands} />}
        </Panel>
      </div>
      <Panel title={t('dashboard.kriBreaches')} testId="risk-kri-breaches">
        {data.kriBreaches.length === 0 ? (
          <Empty />
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.risk')}</TableHeaderCell>
                <TableHeaderCell>{t('kris.card')}</TableHeaderCell>
                <TableHeaderCell>{t('kris.lastValue')}</TableHeaderCell>
                <TableHeaderCell>{t('kris.breachThreshold')}</TableHeaderCell>
                <TableHeaderCell>{t('kris.period')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.kriBreaches.map((b) => (
                <TableRow key={b.kriId}>
                  <TableCell>
                    <Link
                      to="/risk/risks/$riskId"
                      params={{ riskId: b.riskId }}
                      search={{ tab: 'kris' }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{b.riskNumber}</bdi> · {names.text(b.riskTitleAr, b.riskTitleEn)}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <bdi>{b.cardCode}</bdi> · {names.text(b.cardNameAr, b.cardNameEn)}
                  </TableCell>
                  <TableCell className="tabular-nums">{b.value == null ? '—' : n(b.value)}</TableCell>
                  <TableCell className="tabular-nums">
                    {k(`kriDirection.${b.direction}`)} {n(b.threshold)}
                  </TableCell>
                  <TableCell>{b.period ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Panel>
    </>
  );
}

function Empty() {
  const { t } = useTranslation('risk');
  return <p className="text-sm text-muted-foreground">{t('common.noData')}</p>;
}

function Panel({ title, children, testId }: { title: string; children: ReactNode; testId?: string }) {
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title} data-testid={testId}>
      <h2 className="font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function CountBars({
  title,
  rows,
  testId,
}: {
  title: string;
  rows: { key: string; label: string; value: number }[];
  testId?: string;
}) {
  const num = useNum();
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <Panel title={title} testId={testId}>
      {rows.length === 0 ? (
        <Empty />
      ) : (
        <ul className="flex flex-col gap-2">
          {rows.map((row) => (
            <li key={row.key} className="grid grid-cols-[minmax(6rem,12rem)_1fr_auto] items-center gap-2 text-sm">
              <span className="truncate" title={row.label}>
                {row.label}
              </span>
              <span className="flex h-4 overflow-hidden rounded bg-muted" aria-hidden>
                <span className="h-full bg-primary" style={{ inlineSize: `${(row.value / max) * 100}%` }} />
              </span>
              <span className="tabular-nums text-muted-foreground">{num(row.value)}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

/** A compact register table (dashboard lists). */
export function RiskTable({
  rows,
  bands,
  rank,
  review,
}: {
  rows: readonly RiskListItem[];
  bands?: RiskDashboard['bands'];
  rank?: boolean;
  review?: boolean;
}) {
  const { t } = useTranslation('risk');
  const { i18n } = useTranslation();
  const names = useNames();
  if (rows.length === 0) return <Empty />;
  return (
    <Table>
      <TableHead>
        <TableRow>
          {rank && <TableHeaderCell>{t('fields.rank')}</TableHeaderCell>}
          <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.residual')}</TableHeaderCell>
          {review && <TableHeaderCell>{t('fields.nextReviewOn')}</TableHeaderCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((r, index) => (
          <TableRow key={r.id}>
            {rank && <TableCell className="tabular-nums">{r.top10Rank ?? index + 1}</TableCell>}
            <TableCell>
              <Link to="/risk/risks/$riskId" params={{ riskId: r.id }} className="font-medium text-primary hover:underline">
                <bdi>{r.number}</bdi>
              </Link>
            </TableCell>
            <TableCell>{names.text(r.titleAr, r.titleEn)}</TableCell>
            <TableCell>{names.unit(r.unit)}</TableCell>
            <TableCell>
              <RatingChip band={r.residualBand} score={r.residualScore} bands={bands} />
            </TableCell>
            {review && (
              <TableCell className={r.reviewOverdue ? 'text-danger' : undefined}>
                {r.nextReviewOn ? formatDate(r.nextReviewOn, i18n.language) : '—'}
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function CellRisks({
  cell,
  filter,
}: {
  cell: { kind: 'inherent' | 'residual'; likelihood: number; impact: number };
  filter: RiskFilterQuery;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const names = useNames();
  const result = useHeatCell(cell, filter);
  return (
    <Panel
      title={t('heatmap.cellRisks', { kind: t(`heatmap.${cell.kind}`), likelihood: cell.likelihood, impact: cell.impact })}
      testId="risk-heat-cell-list"
    >
      {result.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, result.error)} />
      ) : !result.data ? (
        <Skeleton className="h-24" />
      ) : result.data.risks.length + result.data.projectRisks.length === 0 ? (
        <Empty />
      ) : (
        <ul className="flex flex-col divide-y">
          {result.data.risks.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center gap-3 py-2 text-sm">
              <Link to="/risk/risks/$riskId" params={{ riskId: r.id }} className="font-medium text-primary hover:underline">
                <bdi>{r.number}</bdi> · {names.text(r.titleAr, r.titleEn)}
              </Link>
              <span className="text-muted-foreground">{names.unit(r.unit)}</span>
              <RatingChip band={r.inherentBand} score={r.inherentScore} prefix={t('rating.inherentShort')} />
              <RatingChip band={r.residualBand} score={r.residualScore} prefix={t('rating.residualShort')} />
            </li>
          ))}
          {result.data.projectRisks.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center gap-3 py-2 text-sm">
              <a href={r.link} className="font-medium text-primary hover:underline">
                <bdi>{r.projectNumber}</bdi> · {r.title ?? '—'}
              </a>
              <span className="text-muted-foreground">{names.text(r.projectNameAr, r.projectNameEn)}</span>
              <span className="rounded bg-muted px-1.5 text-xs">{t('common.projectRisk')}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function ReportDialog({ unitId, year, onClose }: { unitId?: string; year?: number; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { language } = useRiskT();
  const toast = useToast();
  const [draft, setDraft] = useState<ReportDraft>();
  const [busy, setBusy] = useState(false);
  async function run() {
    setBusy(true);
    try {
      setDraft(await riskApi.report({ unitId: unitId ?? null, year: year ?? null }));
    } catch (e) {
      toast({ tone: 'error', title: errorMessage(tc, e) });
    } finally {
      setBusy(false);
    }
  }
  const ar = language === 'ar';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('ai.report.title')}
        description={t('ai.report.hint')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button onClick={() => void run()} loading={busy} data-testid="risk-ai-report-run">
              <Sparkles aria-hidden />
              {draft ? t('ai.again') : t('ai.report.run')}
            </Button>
          </>
        }
      >
        {draft && (
          <div className="flex max-h-[60vh] flex-col gap-4 overflow-auto" data-testid="risk-ai-report-draft">
            <AiBadge label={t('ai.generatedDraft')} className="self-start" />
            {(ar ? [draft.reportAr, draft.top10Ar, draft.reportEn, draft.top10En] : [draft.reportEn, draft.top10En, draft.reportAr, draft.top10Ar]).map(
              (text, index) => (
                <section key={index} dir={(index < 2) === ar ? 'rtl' : 'ltr'} className="rounded border p-3">
                  <h3 className="mb-1 text-sm font-semibold">
                    {index % 2 === 0 ? t('ai.report.report') : t('ai.report.top10')} ·{' '}
                    {(index < 2) === ar ? 'العربية' : 'English'}
                  </h3>
                  <p className="whitespace-pre-line text-sm">{text}</p>
                </section>
              ),
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
