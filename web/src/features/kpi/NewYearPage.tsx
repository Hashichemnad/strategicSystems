import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Input,
  PageHeader,
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
  Wizard,
  useToast,
} from '@/ui';
import {
  currentYear,
  kpiApi,
  targetText,
  useKpiMutation,
  useKpiOptions,
  useKpiPlans,
  useKpiYearPreview,
  type KpiYearItemInput,
} from './api';
import { KpiPage, PerformanceValue, useKpiT, useNumber, yearOptions } from './components';

interface Row extends KpiYearItemInput {
  code: string;
  name: string;
  hasPlan: boolean;
  source: string;
}

/** New measuring year (SPEC K1): choose the year, untick cards, adjust period / unit / direction / targets, submit; then confirm the plans. */
export function KpiNewYearPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const toast = useToast();
  const options = useKpiOptions();
  const [year, setYear] = useState(currentYear() + 1);
  const preview = useKpiYearPreview(year);
  const [rows, setRows] = useState<Row[]>([]);
  const [summary, setSummary] = useState<Awaited<ReturnType<typeof kpiApi.startYear>>>();
  const start = useKpiMutation(kpiApi.startYear);

  useEffect(() => {
    if (!preview.data) return;
    setRows(
      preview.data.cards.map((c) => ({
        cardId: c.cardId,
        include: !c.hasPlan,
        measuringPeriod: c.measuringPeriod,
        unitCode: c.unitCode,
        direction: c.direction,
        targetType: c.targetType,
        target: c.target,
        rangeMin: c.rangeMin,
        rangeMax: c.rangeMax,
        isAccumulated: c.isAccumulated,
        code: c.code,
        name: pickText(language, c.nameAr, c.nameEn),
        hasPlan: c.hasPlan,
        source: c.targetSource,
      })),
    );
  }, [preview.data, language]);

  const update = (i: number, patch: Partial<Row>) =>
    setRows((r) => r.map((row, j) => (j === i ? { ...row, ...patch } : row)));
  const n = (v: string) => (v === '' ? null : Number(v));
  const select = (
    value: string,
    onChange: (v: string) => void,
    values: readonly string[],
    prefix: string,
    label: string,
  ) => (
    <Select
      aria-label={label}
      value={value}
      onValueChange={onChange}
      options={values.map((v) => ({ value: v, label: k(`${prefix}.${v}`) }))}
    />
  );
  const units = (options.data?.lookups['kpi.unit'] ?? []).map((o) => ({
    value: o.code,
    label: pickText(language, o.labelAr, o.labelEn) || o.code,
  }));

  if (options.data && !options.data.canManagePlans)
    return (
      <KpiPage>
        <StatusBanner tone="warning" title={t('common.forbidden')} />
      </KpiPage>
    );

  return (
    <KpiPage testId="kpi-new-year">
      <PageHeader title={t('newYear.title')} description={t('newYear.description')} />
      {summary ? (
        <YearSummary year={year} summary={summary} />
      ) : (
        <Wizard
          busy={start.isPending}
          finishLabel={t('newYear.submit')}
          onFinish={() =>
            start.mutate(
              {
                year,
                items: rows.map((r) => ({
                  cardId: r.cardId,
                  include: r.include,
                  measuringPeriod: r.measuringPeriod,
                  unitCode: r.unitCode,
                  direction: r.direction,
                  targetType: r.targetType,
                  target: r.target,
                  rangeMin: r.rangeMin,
                  rangeMax: r.rangeMax,
                  isAccumulated: r.isAccumulated,
                })),
              },
              { onSuccess: setSummary, onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
            )
          }
          steps={[
            {
              key: 'year',
              title: t('newYear.steps.year'),
              content: (
                <div className="flex max-w-xs flex-col gap-2" data-testid="kpi-new-year-step-year">
                  <Select
                    aria-label={t('common.year')}
                    value={String(year)}
                    onValueChange={(v) => setYear(Number(v))}
                    options={yearOptions(currentYear(), 1, 3)}
                  />
                  {preview.data && (
                    <p className="text-sm text-muted-foreground">
                      {t('newYear.existing', { count: preview.data.existingPlans })}
                    </p>
                  )}
                </div>
              ),
            },
            {
              key: 'cards',
              title: t('newYear.steps.cards'),
              validate: () => rows.some((r) => r.include && !r.hasPlan),
              content: preview.isPending ? (
                <Skeleton className="h-64" />
              ) : preview.isError ? (
                <StatusBanner tone="danger" title={errorMessage(tc, preview.error)} />
              ) : (
                <div className="overflow-x-auto" data-testid="kpi-new-year-step-cards">
                  <Table>
                    <TableHead>
                      <TableRow>
                        <TableHeaderCell>{t('newYear.include')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.measuringPeriod')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.unitCode')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.direction')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.targetType')}</TableHeaderCell>
                        <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {rows.map((row, i) => (
                        <TableRow key={row.cardId} data-testid="kpi-new-year-row">
                          <TableCell>
                            <Checkbox
                              label={<span className="sr-only">{row.code}</span>}
                              checked={row.include}
                              disabled={row.hasPlan}
                              onChange={(e) => update(i, { include: e.target.checked })}
                            />
                          </TableCell>
                          <TableCell>
                            <span className="flex flex-col">
                              <bdi className="font-medium">{row.code}</bdi>
                              <span className="text-xs text-muted-foreground">{row.name}</span>
                              {row.hasPlan && <StatusChip tone="neutral">{t('newYear.hasPlan')}</StatusChip>}
                            </span>
                          </TableCell>
                          <TableCell className="min-w-36">
                            {select(
                              row.measuringPeriod,
                              (v) => update(i, { measuringPeriod: v }),
                              options.data?.measuringPeriods ?? [],
                              'measuringPeriod',
                              t('fields.measuringPeriod'),
                            )}
                          </TableCell>
                          <TableCell className="min-w-32">
                            <Select
                              aria-label={t('fields.unitCode')}
                              value={row.unitCode}
                              onValueChange={(v) => update(i, { unitCode: v })}
                              options={units}
                            />
                          </TableCell>
                          <TableCell className="min-w-36">
                            {select(
                              row.direction,
                              (v) => update(i, { direction: v }),
                              options.data?.directions ?? [],
                              'direction',
                              t('fields.direction'),
                            )}
                          </TableCell>
                          <TableCell className="min-w-32">
                            {select(
                              row.targetType,
                              (v) => update(i, { targetType: v }),
                              options.data?.targetTypes ?? [],
                              'targetType',
                              t('fields.targetType'),
                            )}
                          </TableCell>
                          <TableCell className="min-w-48">
                            {row.targetType === 'range' ? (
                              <span className="flex gap-1">
                                <Input
                                  type="number"
                                  aria-label={t('fields.rangeMin')}
                                  value={row.rangeMin ?? ''}
                                  onChange={(e) => update(i, { rangeMin: n(e.target.value) })}
                                />
                                <Input
                                  type="number"
                                  aria-label={t('fields.rangeMax')}
                                  value={row.rangeMax ?? ''}
                                  onChange={(e) => update(i, { rangeMax: n(e.target.value) })}
                                />
                              </span>
                            ) : (
                              <Input
                                type="number"
                                aria-label={t('fields.target')}
                                value={row.target ?? ''}
                                onChange={(e) => update(i, { target: n(e.target.value) })}
                                data-testid="kpi-new-year-target"
                              />
                            )}
                            <span className="text-xs text-muted-foreground">
                              {k(`newYear.source.${row.source}`)}
                            </span>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              ),
            },
            {
              key: 'review',
              title: t('newYear.steps.review'),
              content: (
                <p className="text-sm" data-testid="kpi-new-year-step-review">
                  {t('newYear.reviewText', {
                    count: rows.filter((r) => r.include && !r.hasPlan).length,
                    year,
                  })}
                </p>
              ),
            },
          ]}
        />
      )}
    </KpiPage>
  );
}

/** After the run: the year's plans, to review and confirm (K1 step 4). */
function YearSummary({
  year,
  summary,
}: {
  year: number;
  summary: Awaited<ReturnType<typeof kpiApi.startYear>>;
}) {
  const { t } = useTranslation('kpi');
  return (
    <div className="flex flex-col gap-4">
      <StatusBanner
        tone="success"
        title={t('newYear.done', { plans: summary.plansCreated, results: summary.resultsCreated, year })}
        description={summary.skipped > 0 ? t('newYear.skipped', { count: summary.skipped }) : undefined}
      />
      <YearPlans year={year} />
    </div>
  );
}

/** Plans of a year with bulk confirmation. */
export function YearPlans({ year }: { year: number }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const num = useNumber();
  const toast = useToast();
  const plans = useKpiPlans(year);
  const confirm = useKpiMutation(kpiApi.confirmPlans);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  useEffect(() => {
    if (plans.data) setSelected(new Set(plans.data.filter((p) => p.status === 'draft').map((p) => p.id)));
  }, [plans.data]);
  if (!plans.data) return <Skeleton className="h-40" />;
  const drafts = plans.data.filter((p) => p.status === 'draft');
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-year-plans">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('newYear.plansOf', { year })}</h2>
        {drafts.length > 0 && (
          <Button
            loading={confirm.isPending}
            disabled={selected.size === 0}
            data-testid="kpi-confirm-plans"
            onClick={() =>
              confirm.mutate([...selected], {
                onSuccess: (count) => toast({ title: t('newYear.confirmed', { count }) }),
                onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
              })
            }
          >
            {t('newYear.confirmSelected', { count: selected.size })}
          </Button>
        )}
      </header>
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>
              <span className="sr-only">{t('newYear.include')}</span>
            </TableHeaderCell>
            <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.measuringPeriod')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
            <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {plans.data.map((p) => (
            <TableRow key={p.id} data-testid="kpi-plan-row">
              <TableCell>
                {p.status === 'draft' && (
                  <Checkbox
                    label={<span className="sr-only">{p.cardCode}</span>}
                    checked={selected.has(p.id)}
                    onChange={(e) =>
                      setSelected((s) => {
                        const next = new Set(s);
                        if (e.target.checked) next.add(p.id);
                        else next.delete(p.id);
                        return next;
                      })
                    }
                  />
                )}
              </TableCell>
              <TableCell>
                <Link
                  to="/kpi/plans/$planId"
                  params={{ planId: p.id }}
                  className="text-primary hover:underline"
                >
                  <bdi>{p.cardCode}</bdi> · {pickText(language, p.cardNameAr, p.cardNameEn)}
                </Link>
              </TableCell>
              <TableCell>{k(`measuringPeriod.${p.measuringPeriod}`)}</TableCell>
              <TableCell className="tabular-nums">
                {targetText(num, p.targetType, p.target, p.rangeMin, p.rangeMax)}
              </TableCell>
              <TableCell>
                <PerformanceValue capped={p.capped} actual={p.actual} />
              </TableCell>
              <TableCell>
                <StatusChip tone={p.status === 'confirmed' ? 'success' : 'warning'}>
                  {k(`planStatus.${p.status}`)}
                </StatusChip>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </section>
  );
}
