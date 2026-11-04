import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { kpiApi, resultTone, targetText, useKpiMutation, useKpiPlan, type KpiPlan } from './api';
import { KpiPage, PerformanceValue, RagChip, useKpiT, useNumber } from './components';

const route = getRouteApi('/app/kpi/plans/$planId');

/** Annual plan (SPEC §2.3 B): the year's targets per period, editable while draft, then confirmed by the Strategy Office. */
export function KpiPlanPage() {
  const { t: tc } = useTranslation();
  const { planId } = route.useParams();
  const plan = useKpiPlan(planId);
  if (plan.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (plan.isError) return <StatusBanner tone="danger" title={errorMessage(tc, plan.error)} />;
  return <PlanView key={plan.data.plan.version} data={plan.data} />;
}

function PlanView({ data }: { data: KpiPlan }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const num = useNumber();
  const toast = useToast();
  const p = data.plan;
  const editable = data.canManage && p.status === 'draft';
  const [targets, setTargets] = useState(() =>
    Object.fromEntries(
      data.periods.map((x) => [x.resultId, { target: x.target, rangeMin: x.rangeMin, rangeMax: x.rangeMax }]),
    ),
  );
  const [annual, setAnnual] = useState({ target: p.target, rangeMin: p.rangeMin, rangeMax: p.rangeMax });
  const save = useKpiMutation(kpiApi.updatePlan);
  const confirm = useKpiMutation(kpiApi.confirmPlans);
  const n = (v: string) => (v === '' ? null : Number(v));
  const range = p.targetType === 'range';
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });

  return (
    <KpiPage testId="kpi-plan-page">
      <Link
        to="/kpi/cards/$cardId"
        params={{ cardId: p.cardId }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        <bdi>{p.cardCode}</bdi>
      </Link>
      <PageHeader
        title={t('plan.title', { year: p.year, name: pickText(language, p.cardNameAr, p.cardNameEn) })}
        description={`${k(`measuringPeriod.${p.measuringPeriod}`)} · ${k(`direction.${p.direction}`)} · ${k(`aggregation.${data.aggregationRule}`)}`}
        meta={
          <>
            <StatusChip tone={p.status === 'confirmed' ? 'success' : 'warning'}>
              {k(`planStatus.${p.status}`)}
            </StatusChip>
            <PerformanceValue capped={p.capped} actual={p.actual} />
            <RagChip rag={p.rag} />
          </>
        }
        actions={
          editable && (
            <div className="flex gap-2">
              <Button
                variant="outline"
                loading={save.isPending}
                onClick={() =>
                  save.mutate(
                    {
                      id: p.id,
                      input: {
                        targetType: p.targetType,
                        target: annual.target,
                        rangeMin: annual.rangeMin,
                        rangeMax: annual.rangeMax,
                        unitCode: p.unitCode,
                        direction: p.direction,
                        isAccumulated: data.isAccumulated,
                        responsibleEmployeeId: null,
                        periods: Object.entries(targets).map(([resultId, x]) => ({ resultId, ...x })),
                        version: p.version,
                      },
                    },
                    { onSuccess: () => toast({ title: t('plan.saved') }), onError },
                  )
                }
              >
                {tc('actions.save')}
              </Button>
              <Button
                loading={confirm.isPending}
                onClick={() => confirm.mutate([p.id], { onError })}
                data-testid="kpi-plan-confirm"
              >
                {t('plan.confirm')}
              </Button>
            </div>
          )
        }
      />
      <section className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-3">
        {range ? (
          <>
            <FormField label={t('fields.rangeMin')}>
              <Input
                type="number"
                disabled={!editable}
                value={annual.rangeMin ?? ''}
                onChange={(e) => setAnnual({ ...annual, rangeMin: n(e.target.value) })}
              />
            </FormField>
            <FormField label={t('fields.rangeMax')}>
              <Input
                type="number"
                disabled={!editable}
                value={annual.rangeMax ?? ''}
                onChange={(e) => setAnnual({ ...annual, rangeMax: n(e.target.value) })}
              />
            </FormField>
          </>
        ) : (
          <FormField label={t('plan.annualTarget')}>
            <Input
              type="number"
              disabled={!editable}
              value={annual.target ?? ''}
              onChange={(e) => setAnnual({ ...annual, target: n(e.target.value) })}
            />
          </FormField>
        )}
        <div>
          <p className="text-sm text-muted-foreground">{t('plan.achieved')}</p>
          <p className="text-xl font-semibold tabular-nums">{num(p.achieved)}</p>
        </div>
      </section>
      <Table data-testid="kpi-plan-periods">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('fields.period')}</TableHeaderCell>
            <TableHeaderCell>{t('plan.window')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.achieve')}</TableHeaderCell>
            <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.periods.map((x) => {
            const value = targets[x.resultId] ?? { target: null, rangeMin: null, rangeMax: null };
            const set = (patch: Partial<typeof value>) =>
              setTargets((s) => ({ ...s, [x.resultId]: { ...value, ...patch } }));
            const canEdit = editable && x.status !== 'approved';
            return (
              <TableRow key={x.resultId}>
                <TableCell>
                  <Link
                    to="/kpi/results/$resultId"
                    params={{ resultId: x.resultId }}
                    className="text-primary hover:underline"
                  >
                    {k(`period.${x.periodCode}`)}
                  </Link>
                </TableCell>
                <TableCell className="text-xs">
                  {x.opensOn ? formatDate(x.opensOn, language) : '—'} →{' '}
                  {x.dueOn ? formatDate(x.dueOn, language) : '—'}
                </TableCell>
                <TableCell className="min-w-40">
                  {canEdit ? (
                    range ? (
                      <span className="flex gap-1">
                        <Input
                          type="number"
                          aria-label={t('fields.rangeMin')}
                          value={value.rangeMin ?? ''}
                          onChange={(e) => set({ rangeMin: n(e.target.value) })}
                        />
                        <Input
                          type="number"
                          aria-label={t('fields.rangeMax')}
                          value={value.rangeMax ?? ''}
                          onChange={(e) => set({ rangeMax: n(e.target.value) })}
                        />
                      </span>
                    ) : (
                      <Input
                        type="number"
                        aria-label={t('fields.target')}
                        value={value.target ?? ''}
                        onChange={(e) => set({ target: n(e.target.value) })}
                      />
                    )
                  ) : (
                    targetText(num, p.targetType, x.target, x.rangeMin, x.rangeMax)
                  )}
                </TableCell>
                <TableCell className="tabular-nums">
                  {x.notApplicable ? t('result.na') : num(x.achieve)}
                </TableCell>
                <TableCell>
                  <PerformanceValue capped={x.capped} actual={x.actual} />
                </TableCell>
                <TableCell>
                  <span className="flex flex-wrap gap-1">
                    <StatusChip tone={resultTone[x.status] ?? 'neutral'}>
                      {k(`resultStatus.${x.status}`)}
                    </StatusChip>
                    {x.late && <StatusChip tone="warning">{t('result.late')}</StatusChip>}
                  </span>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </KpiPage>
  );
}
