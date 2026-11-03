import { getRouteApi } from '@tanstack/react-router';
import { ClipboardCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  EmptyState,
  FormField,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import {
  currentYear,
  kpiApi,
  periodCodes,
  useKpiMutation,
  useKpiOptions,
  useKpiQueue,
  type KpiResultListItem,
} from './api';
import { KpiPage, useKpiT, yearOptions } from './components';
import { ResultsTable } from './PendingPage';

const route = getRouteApi('/app/kpi/approvals');
const ALL = '__all';

/** KPI approval (SPEC K4): the reviewer's queue filtered by year, period and department, and bulk approval of a department's results. */
export function KpiApprovalsPage() {
  const { t } = useTranslation('kpi');
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  return (
    <KpiPage testId="kpi-approvals">
      <PageHeader title={t('approvals.title')} description={t('approvals.description')} />
      <Tabs
        value={tab ?? 'queue'}
        onValueChange={(value) => void navigate({ search: { tab: value as 'queue' } })}
      >
        <TabsList>
          <TabsTrigger value="queue">{t('approvals.queue')}</TabsTrigger>
          <TabsTrigger value="bulk" data-testid="kpi-bulk-tab">
            {t('bulk.title')}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="queue">
          <Queue />
        </TabsContent>
        <TabsContent value="bulk">
          <Bulk />
        </TabsContent>
      </Tabs>
    </KpiPage>
  );
}

function Filters({
  year,
  setYear,
  period,
  setPeriod,
  unitId,
  setUnitId,
}: {
  year: number;
  setYear: (v: number) => void;
  period: string | undefined;
  setPeriod: (v: string | undefined) => void;
  unitId: string | undefined;
  setUnitId: (v: string | undefined) => void;
}) {
  const { t } = useTranslation('kpi');
  const { k } = useKpiT();
  return (
    <div className="grid gap-3 md:grid-cols-3">
      <FormField label={t('common.year')}>
        <Select
          value={String(year)}
          onValueChange={(v) => setYear(Number(v))}
          options={yearOptions(currentYear())}
        />
      </FormField>
      <FormField label={t('fields.period')}>
        <Select
          value={period ?? ALL}
          onValueChange={(v) => setPeriod(v === ALL ? undefined : v)}
          options={[
            { value: ALL, label: t('approvals.allPeriods') },
            ...periodCodes.map((p) => ({ value: p, label: k(`period.${p}`) })),
          ]}
        />
      </FormField>
      <FormField label={t('approvals.department')}>
        <UnitSelect allowEmpty value={unitId} onChange={setUnitId} />
      </FormField>
    </div>
  );
}

function Queue() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const [year, setYear] = useState(currentYear());
  const [period, setPeriod] = useState<string>();
  const [unitId, setUnitId] = useState<string>();
  const [mine, setMine] = useState(false);
  const queue = useKpiQueue({ year, ...(period ? { period } : {}), ...(unitId ? { unitId } : {}), mine });
  return (
    <div className="flex flex-col gap-4">
      <Filters
        year={year}
        setYear={setYear}
        period={period}
        setPeriod={setPeriod}
        unitId={unitId}
        setUnitId={setUnitId}
      />
      <Switch label={t('approvals.mine')} checked={mine} onCheckedChange={setMine} />
      {queue.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, queue.error)} />
      ) : !queue.data ? (
        <Skeleton className="h-64" />
      ) : queue.data.length === 0 ? (
        <EmptyState icon={ClipboardCheck} title={t('approvals.empty')} />
      ) : (
        <ResultsTable items={queue.data} testId="kpi-queue-table" />
      )}
    </div>
  );
}

/** Bulk approval (SPEC K4.5): year + department, tick the results to approve (all ticked), untick to leave, confirm the checklist, Update. */
function Bulk() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { language } = useKpiT();
  const toast = useToast();
  const options = useKpiOptions();
  const [year, setYear] = useState(currentYear());
  const [period, setPeriod] = useState<string>();
  const [unitId, setUnitId] = useState<string>();
  const queue = useKpiQueue({
    year,
    ...(period ? { period } : {}),
    ...(unitId ? { unitId } : {}),
    mine: true,
  });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmed, setConfirmed] = useState(false);
  const [comment, setComment] = useState('');
  const [outcome, setOutcome] = useState<{
    approved: number;
    failed: number;
    errors: { item?: KpiResultListItem; key: string | null }[];
  }>();
  const bulk = useKpiMutation(kpiApi.bulkApprove);
  useEffect(() => {
    if (queue.data) setSelected(new Set(queue.data.map((r) => r.id)));
  }, [queue.data]);
  const items = queue.data ?? [];

  return (
    <div className="flex flex-col gap-4" data-testid="kpi-bulk">
      <Filters
        year={year}
        setYear={setYear}
        period={period}
        setPeriod={setPeriod}
        unitId={unitId}
        setUnitId={setUnitId}
      />
      {outcome && (
        <StatusBanner
          tone={outcome.failed ? 'warning' : 'success'}
          title={t('bulk.outcome', { approved: outcome.approved, failed: outcome.failed })}
          description={
            outcome.errors.length > 0
              ? outcome.errors
                  .map(
                    (e) =>
                      `${e.item?.cardCode ?? ''} ${e.item?.periodCode.toUpperCase() ?? ''}: ${e.key ? translateMessageKey(tc, e.key) : ''}`,
                  )
                  .join(' · ')
              : undefined
          }
        />
      )}
      {queue.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, queue.error)} />
      ) : !queue.data ? (
        <Skeleton className="h-64" />
      ) : items.length === 0 ? (
        <EmptyState icon={ClipboardCheck} title={t('bulk.empty')} />
      ) : (
        <>
          <ResultsTable
            items={items}
            testId="kpi-bulk-table"
            select={{
              selected,
              onToggle: (id, on) =>
                setSelected((s) => {
                  const next = new Set(s);
                  if (on) next.add(id);
                  else next.delete(id);
                  return next;
                }),
            }}
          />
          <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
            <p className="text-sm text-muted-foreground">{t('bulk.checklistIntro')}</p>
            <ul className="list-disc ps-5 text-sm">
              {(options.data?.checklist ?? []).map((item) => (
                <li key={item.code}>{pickText(language, item.labelAr, item.labelEn)}</li>
              ))}
            </ul>
            <Checkbox
              label={t('bulk.confirmChecklist')}
              checked={confirmed}
              onChange={(e) => setConfirmed(e.target.checked)}
              data-testid="kpi-bulk-confirm"
            />
            <FormField label={t('review.comment')}>
              <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
            </FormField>
            <Button
              className="w-fit"
              disabled={!confirmed || selected.size === 0}
              loading={bulk.isPending}
              data-testid="kpi-bulk-update"
              onClick={() =>
                bulk.mutate(
                  { resultIds: [...selected], comment: comment || null, checklistConfirmed: confirmed },
                  {
                    onSuccess: (r) => {
                      setOutcome({
                        approved: r.approved,
                        failed: r.failed,
                        errors: r.outcomes
                          .filter((o) => !o.success)
                          .map((o) => ({
                            item: items.find((i) => i.id === o.resultId),
                            key: o.errorKey ?? null,
                          })),
                      });
                      setConfirmed(false);
                    },
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {t('bulk.update', { count: selected.size })}
            </Button>
          </section>
        </>
      )}
    </div>
  );
}
