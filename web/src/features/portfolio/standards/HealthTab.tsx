import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { useFeature } from '../featuresApi';
import { toPercent, useProjectMutation, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  standardsApi,
  useBaselines,
  useEvm,
  useHealth,
  useVariance,
  type Health,
  type HealthDimension,
  type HealthValue,
  type VarianceRow,
} from './standardsApi';
import { dayVarianceTone, deltaTone, isBelowOne, ragCode, ragTone, signed } from './standardsLogic';

const dimensions: HealthDimension[] = ['schedule', 'cost', 'scope', 'risk', 'overall'];

/** Health tab (docs/design/portfolio.md §33–§34): RAG per dimension with override, baseline variance, earned value. */
export function HealthTab({ project }: { project: ProjectFull }) {
  const health = useFeature('health');
  const baselines = useFeature('baselines');
  const earnedValue = useFeature('earnedValue');
  return (
    <div className="flex flex-col gap-8">
      {health && <HealthPanel project={project} />}
      {baselines && <BaselinePanel project={project} />}
      {earnedValue && !project.delivery.costsHidden && <EvmPanel project={project} />}
    </div>
  );
}

function RagChip({ value }: { value: string }) {
  const { t } = useTranslation('pmStandards');
  const code = ragCode(value);
  return <StatusChip tone={ragTone(code)}>{t(`rag.${code}`)}</StatusChip>;
}

function HealthPanel({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const health = useHealth(project.id);
  const [editing, setEditing] = useState<HealthDimension | null>(null);
  const lang = i18n.language;
  const pct = (ratio: number | null) =>
    ratio === null ? '—' : `${formatNumber(toPercent(ratio) ?? 0, lang)} %`;
  return (
    <section className="flex flex-col gap-3" data-testid="health-panel">
      <header>
        <h2 className="font-semibold">{t('health.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('health.hint')}</p>
      </header>
      {health.isPending ? (
        <Skeleton className="h-32" />
      ) : health.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, health.error)} />
      ) : (
        <>
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {dimensions.map((d) => {
              const value: HealthValue = health.data.health[d];
              return (
                <li
                  key={d}
                  className="flex flex-col gap-2 rounded-lg border bg-card p-4"
                  data-testid={`health-${d}`}
                >
                  <h3 className="text-sm font-medium">{t(`health.dimensions.${d}`)}</h3>
                  <div className="flex flex-wrap items-center gap-1">
                    <RagChip value={value.effective} />
                    {value.override && <StatusChip tone="info">{t('health.overridden')}</StatusChip>}
                  </div>
                  {value.override && (
                    <div className="text-xs text-muted-foreground">
                      <p>
                        {t('health.calculated')}: {t(`rag.${ragCode(value.auto)}`)}
                      </p>
                      <p className="whitespace-pre-line" dir="auto">
                        {t('health.justification')}: {value.override.justification}
                      </p>
                      <p>{formatDateTime(value.override.at, lang)}</p>
                    </div>
                  )}
                  {health.data.canOverride && (
                    <Button
                      size="sm"
                      variant="outline"
                      className="mt-auto"
                      onClick={() => setEditing(d)}
                      data-testid={`health-override-${d}`}
                    >
                      {t('health.override')}
                    </Button>
                  )}
                </li>
              );
            })}
          </ul>
          <dl className="grid gap-2 text-sm sm:grid-cols-3 lg:grid-cols-5">
            <Fact label={t('health.facts.progress')} value={pct(health.data.progress)} />
            <Fact label={t('health.facts.planned')} value={pct(health.data.planned)} />
            <Fact
              label={t('health.facts.costVariance')}
              value={project.delivery.costsHidden ? '—' : pct(health.data.costVariance)}
            />
            <Fact
              label={t('health.facts.daysPastDue')}
              value={formatNumber(health.data.maxDaysPastDue, lang)}
            />
            <Fact
              label={t('health.facts.overdueTasks')}
              value={formatNumber(health.data.overdueTasks, lang)}
            />
          </dl>
          {editing && (
            <OverrideDialog
              project={project}
              health={health.data}
              dimension={editing}
              onClose={() => setEditing(null)}
            />
          )}
        </>
      )}
    </section>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border bg-card px-3 py-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="font-medium tabular-nums">{value}</dd>
    </div>
  );
}

function OverrideDialog({
  project,
  health,
  dimension,
  onClose,
}: {
  project: ProjectFull;
  health: Health;
  dimension: HealthDimension;
  onClose: () => void;
}) {
  const { t } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.overrideHealth);
  const current = health.health[dimension];
  const [value, setValue] = useState<string>(current.override?.value ?? current.auto);
  const [justification, setJustification] = useState(current.override?.justification ?? '');
  const send = (clear: boolean) =>
    save.mutate(
      {
        id: project.id,
        dimension,
        value: clear ? null : (ragCode(value) as 'green' | 'amber' | 'red'),
        justification: clear ? null : justification.trim(),
        version: health.version,
      },
      {
        onSuccess: () => {
          toast({ title: t('saved') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('health.override')} — ${t(`health.dimensions.${dimension}`)}`}
        description={t('health.overrideHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            {current.override && (
              <Button variant="outline" loading={save.isPending} onClick={() => send(true)}>
                {t('health.clear')}
              </Button>
            )}
            <Button
              disabled={justification.trim() === '' || ragCode(value) === 'grey'}
              loading={save.isPending}
              onClick={() => send(false)}
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="health-override-dialog">
          <p className="text-sm">
            {t('health.calculated')}: <RagChip value={current.auto} />
          </p>
          <FormField label={t('health.value')} required>
            <Select
              value={ragCode(value) === 'grey' ? undefined : value}
              onValueChange={setValue}
              options={(['green', 'amber', 'red'] as const).map((v) => ({ value: v, label: t(`rag.${v}`) }))}
            />
          </FormField>
          <FormField label={t('health.justification')} required>
            <Textarea value={justification} onChange={(e) => setJustification(e.target.value)} />
          </FormField>
          {save.isError && <FieldErrors error={save.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function BaselinePanel({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const baselines = useBaselines(project.id);
  const [version, setVersion] = useState<number | undefined>(undefined);
  const has = (baselines.data ?? []).length > 0;
  const variance = useVariance(project.id, version, has);
  const lang = i18n.language;
  const date = (d: string | null | undefined) => (d ? formatDate(d, lang) : '—');
  const money = (n: number | null | undefined) =>
    n === null || n === undefined ? '—' : formatNumber(n, lang);
  const days = (n: number | null | undefined) =>
    n === null || n === undefined ? (
      '—'
    ) : (
      <StatusChip tone={dayVarianceTone(n)}>
        {t('baseline.days', { value: signed(n, (x) => formatNumber(x, lang)) })}
      </StatusChip>
    );
  const pct = (ratio: number) => `${formatNumber(toPercent(ratio) ?? 0, lang)} %`;
  return (
    <section className="flex flex-col gap-3" data-testid="baseline-variance">
      <header>
        <h2 className="font-semibold">{t('baseline.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('baseline.hint')}</p>
      </header>
      {baselines.isPending ? (
        <Skeleton className="h-24" />
      ) : baselines.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, baselines.error)} />
      ) : !has ? (
        <EmptyState title={t('baseline.none')} description={t('baseline.noneHint')} />
      ) : (
        <>
          <ul className="flex flex-col gap-2" data-testid="baseline-versions">
            {baselines.data.map((b) => (
              <li
                key={b.id}
                className="flex flex-wrap items-center gap-2 rounded-md border bg-card px-3 py-2 text-sm"
              >
                <StatusChip tone="info">{t('baseline.version', { value: b.versionNo })}</StatusChip>
                <span>{formatDateTime(b.takenAt, lang)}</span>
                {b.changeRequestNumber && (
                  <bdi className="text-muted-foreground">{b.changeRequestNumber}</bdi>
                )}
                {b.reason && (
                  <span className="text-muted-foreground" dir="auto">
                    {b.reason}
                  </span>
                )}
                <span className="text-muted-foreground">
                  {date(b.plannedStart)} – {date(b.plannedEnd)}
                </span>
                {!project.delivery.costsHidden && b.budget !== null && (
                  <span className="text-muted-foreground">
                    {t('baseline.budget')}: {money(b.budget)}
                  </span>
                )}
              </li>
            ))}
          </ul>
          <FormField label={t('baseline.compareWith')} className="max-w-xs">
            <Select
              value={version === undefined ? 'latest' : String(version)}
              onValueChange={(v) => setVersion(v === 'latest' ? undefined : Number(v))}
              options={[
                { value: 'latest', label: t('baseline.latest') },
                ...baselines.data.map((b) => ({
                  value: String(b.versionNo),
                  label: t('baseline.version', { value: b.versionNo }),
                })),
              ]}
            />
          </FormField>
          {variance.isPending ? (
            <Skeleton className="h-32" />
          ) : variance.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, variance.error)} />
          ) : (
            <>
              <dl className="grid gap-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
                <Fact label={t('baseline.baselineEnd')} value={date(variance.data.baselineEnd)} />
                <Fact label={t('baseline.currentEnd')} value={date(variance.data.currentEnd)} />
                <div className="rounded-md border bg-card px-3 py-2">
                  <dt className="text-xs text-muted-foreground">{t('baseline.endVariance')}</dt>
                  <dd>{days(variance.data.endVarianceDays)}</dd>
                </div>
                {!variance.data.costsHidden && (
                  <Fact
                    label={t('baseline.budgetVariance')}
                    value={`${money(variance.data.baselineBudget)} → ${money(variance.data.currentBudget)}`}
                  />
                )}
              </dl>
              {variance.data.rows.length === 0 ? (
                <EmptyState title={t('baseline.noRows')} />
              ) : (
                <div className="overflow-x-auto rounded-lg border bg-card">
                  <Table>
                    <TableHead>
                      <TableRow>
                        <TableHeaderCell>{t('baseline.item')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.change')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.baselineDates')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.currentDates')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.actualEnd')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.startDelta')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.endDelta')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.actualDelta')}</TableHeaderCell>
                        <TableHeaderCell>{t('baseline.weight')}</TableHeaderCell>
                        {!variance.data.costsHidden && (
                          <TableHeaderCell>{t('baseline.cost')}</TableHeaderCell>
                        )}
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {variance.data.rows.map((r: VarianceRow) => (
                        <TableRow key={r.id} data-testid="variance-row">
                          <TableCell dir="auto">
                            <bdi className="me-1 font-medium">{r.code}</bdi>
                            {pickText(lang, r.titleAr, r.titleEn)}
                          </TableCell>
                          <TableCell>
                            <StatusChip
                              tone={
                                r.change === 'added'
                                  ? 'info'
                                  : r.change === 'removed'
                                    ? 'danger'
                                    : r.change === 'changed'
                                      ? 'warning'
                                      : 'neutral'
                              }
                            >
                              {t(`baseline.changes.${changeCode(r.change)}`)}
                            </StatusChip>
                          </TableCell>
                          <TableCell className="text-sm">
                            {date(r.baselineStart)} – {date(r.baselineEnd)}
                          </TableCell>
                          <TableCell className="text-sm">
                            {date(r.currentStart)} – {date(r.currentEnd)}
                          </TableCell>
                          <TableCell className="text-sm">{date(r.actualEnd)}</TableCell>
                          <TableCell>{days(r.startVarianceDays)}</TableCell>
                          <TableCell>{days(r.endVarianceDays)}</TableCell>
                          <TableCell>{days(r.actualEndVarianceDays)}</TableCell>
                          <TableCell className="tabular-nums">
                            {pct(r.baselineWeight)} → {pct(r.currentWeight)}{' '}
                            {r.currentWeight !== r.baselineWeight && (
                              <StatusChip tone={deltaTone(r.currentWeight - r.baselineWeight)}>
                                {signed(
                                  toPercent(r.currentWeight - r.baselineWeight) ?? 0,
                                  (x) => `${formatNumber(x, lang)} %`,
                                )}
                              </StatusChip>
                            )}
                          </TableCell>
                          {!variance.data.costsHidden && (
                            <TableCell className="tabular-nums">
                              {money(r.baselineCost)} → {money(r.currentCost)}{' '}
                              {r.baselineCost !== null &&
                                r.currentCost !== null &&
                                r.currentCost !== r.baselineCost && (
                                  <StatusChip tone={deltaTone(r.currentCost - r.baselineCost)}>
                                    {signed(r.currentCost - r.baselineCost, (x) => formatNumber(x, lang))}
                                  </StatusChip>
                                )}
                            </TableCell>
                          )}
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </>
          )}
        </>
      )}
    </section>
  );
}

function changeCode(change: string): 'added' | 'removed' | 'changed' | 'unchanged' {
  return change === 'added' || change === 'removed' || change === 'changed' ? change : 'unchanged';
}

const evmMetrics = ['bac', 'pv', 'ev', 'ac', 'sv', 'cv', 'spi', 'cpi', 'eac', 'etc', 'vac', 'tcpi'] as const;

function EvmPanel({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const evm = useEvm(project.id);
  const lang = i18n.language;
  const show = (value: number | null) =>
    value === null ? '—' : formatNumber(Math.round(value * 100) / 100, lang);
  return (
    <section className="flex flex-col gap-3" data-testid="evm-panel">
      <header>
        <h2 className="font-semibold">{t('evm.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('evm.hint')}</p>
      </header>
      {evm.isPending ? (
        <Skeleton className="h-32" />
      ) : evm.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, evm.error)} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            {t('evm.asOf', { date: formatDate(evm.data.asOf, lang) })}
            {evm.data.baselineVersion !== null &&
              ` · ${t('baseline.version', { value: evm.data.baselineVersion })}`}
            {' · '}
            {t('evm.ratios', {
              planned: formatNumber(toPercent(evm.data.values.plannedRatio) ?? 0, lang),
              progress: formatNumber(toPercent(evm.data.values.progressRatio) ?? 0, lang),
            })}
          </p>
          <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {evmMetrics.map((m) => {
              const value = evm.data.values[m];
              const low = (m === 'spi' || m === 'cpi') && isBelowOne(value);
              const negative = (m === 'sv' || m === 'cv' || m === 'vac') && value !== null && value < 0;
              return (
                <div
                  key={m}
                  className={
                    low || negative
                      ? 'rounded-lg border border-danger/40 bg-danger-soft p-3'
                      : 'rounded-lg border bg-card p-3'
                  }
                  data-testid={`evm-${m}`}
                >
                  <dt className="flex items-center gap-2 text-xs text-muted-foreground">
                    <abbr title={t(`evm.names.${m}`)} className="font-semibold no-underline">
                      {t(`evm.short.${m}`)}
                    </abbr>
                    {low && <StatusChip tone="danger">{t('evm.below')}</StatusChip>}
                  </dt>
                  <dd className="text-lg font-semibold tabular-nums">{show(value)}</dd>
                  <dd className="text-xs text-muted-foreground">{t(`evm.explain.${m}`)}</dd>
                </div>
              );
            })}
          </dl>
        </>
      )}
    </section>
  );
}
