import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Pencil, Plus, Sparkles, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkBadge } from '@/features/links/LinkBadge';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  barOption,
  Button,
  Chart,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  RecordLayout,
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Section,
  token,
  useToast,
} from '@/ui';
import {
  currentYear,
  kpiApi,
  resultTone,
  targetText,
  useKpiCard,
  useKpiChanges,
  useKpiLinked,
  useKpiMutation,
  useKpiOptions,
  useKpiPerformance,
  useKpiPlans,
  useKpiVersions,
  type KpiCard,
  type KpiCardTarget,
  type ProposedChange,
} from './api';
import { KpiNav, PerformanceValue, RagChip, usePercent, useKpiT, useNumber } from './components';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/kpi/cards/$cardId');

export const cardTabs = [
  'details',
  'measuring',
  'alignment',
  'plans',
  'performance',
  'documents',
  'linked',
  'versions',
  'tasks',
  'history',
] as const;

/** KPI card page (SPEC §2.5): header, tabs Details, Measuring, Alignment, Plans & results, Performance, Documents, Linked, Versions. */
export function KpiCardPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const { cardId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const card = useKpiCard(cardId);
  const plans = useKpiPlans(currentYear());
  const toast = useToast();
  const status = useKpiMutation(kpiApi.setCardStatus);
  const [dialog, setDialog] = useState<'targets' | 'change' | 'smart' | 'suggest'>();
  const aiBuilder = useAiFeature('kpi-card-builder');
  const aiTargets = useAiFeature('kpi-target-suggestion');

  if (card.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (card.isError) return <StatusBanner tone="danger" title={errorMessage(tc, card.error)} />;
  const data = card.data;
  const plan = plans.data?.find((p) => p.cardId === data.id);

  const goTab = (value: (typeof cardTabs)[number]) => void navigate({ search: { tab: value } });
  // KpiPage's secondary navigation comes first (Classic keeps the same order); the record layout adds the theme's slots.
  return (
    <RecordLayout
      testId="kpi-card-page"
      back={
        <>
          <KpiNav />
          <Link to="/kpi/cards" className="flex items-center gap-1 text-sm text-primary hover:underline">
            <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
            {t('cards.title')}
          </Link>
        </>
      }
      header={
        <PageHeader
          title={pickText(language, data.values.nameAr, data.values.nameEn)}
          description={
            <>
              <bdi>{data.code}</bdi> · {k(`measuringPeriod.${data.values.measuringPeriod}`)} ·{' '}
              {k(`direction.${data.values.direction}`)}
            </>
          }
          meta={
            <>
              <OriginBadge type="kpi.card" id={data.id} />
              <StatusChip tone={data.status === 'active' ? 'success' : 'neutral'}>
                {k(`cardStatus.${data.status}`)}
              </StatusChip>
              {plan && (
                <span className="flex items-center gap-2">
                  <PerformanceValue capped={plan.capped} actual={plan.actual} />
                  <RagChip rag={plan.rag} />
                </span>
              )}
              {data.changeControlled && <StatusChip tone="info">{t('card.changeControlled')}</StatusChip>}
            </>
          }
          actions={
            <div className="flex flex-wrap gap-2">
              {data.canManage && (
                <>
                  <Button variant="outline" asChild>
                    <Link
                      to="/kpi/cards/$cardId/edit"
                      params={{ cardId: data.id }}
                      data-testid="kpi-card-edit"
                    >
                      <Pencil aria-hidden />
                      {t('actions.edit')}
                    </Link>
                  </Button>
                  <Button variant="outline" onClick={() => setDialog('targets')}>
                    {t('card.targets')}
                  </Button>
                  {data.yearStarted && (
                    <Button
                      variant="outline"
                      onClick={() => setDialog('change')}
                      data-testid="kpi-request-change"
                    >
                      {t('changes.request')}
                    </Button>
                  )}
                  <Button
                    variant="outline"
                    loading={status.isPending}
                    onClick={() =>
                      status.mutate(
                        { id: data.id, active: data.status !== 'active', version: data.version },
                        { onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
                      )
                    }
                  >
                    {data.status === 'active' ? t('actions.deactivate') : t('actions.activate')}
                  </Button>
                </>
              )}
              {aiBuilder && (
                <Button variant="ghost" onClick={() => setDialog('smart')}>
                  <Sparkles aria-hidden />
                  {t('ai.smart.action')}
                </Button>
              )}
              {aiTargets && (
                <Button variant="ghost" onClick={() => setDialog('suggest')}>
                  <Sparkles aria-hidden />
                  {t('ai.targets.action')}
                </Button>
              )}
            </div>
          }
        />
      }
      highlights={[
        { key: 'code', label: t('fields.code'), value: <bdi>{data.code}</bdi> },
        {
          key: 'period',
          label: t('fields.measuringPeriod'),
          value: k(`measuringPeriod.${data.values.measuringPeriod}`),
        },
        { key: 'direction', label: t('fields.direction'), value: k(`direction.${data.values.direction}`) },
        { key: 'level', label: t('fields.level'), value: k(`level.${data.values.level}`) },
        {
          key: 'unit',
          label: t('fields.responsibleUnitId'),
          value: data.responsibleUnit
            ? pickText(language, data.responsibleUnit.nameAr, data.responsibleUnit.nameEn)
            : '—',
        },
        {
          key: 'person',
          label: t('fields.responsibleEmployeeId'),
          value: data.responsiblePerson
            ? pickText(language, data.responsiblePerson.nameAr, data.responsiblePerson.nameEn)
            : '—',
        },
      ]}
      related={(
        ['plans', 'performance', 'alignment', 'linked', 'documents', 'versions', 'tasks'] as const
      ).map((key) => ({ key, label: t(`card.tabs.${key}`), onOpen: () => goTab(key) }))}
    >
      <Tabs
        value={tab ?? 'details'}
        onValueChange={(value) => void navigate({ search: { tab: value as 'details' } })}
      >
        <TabsList className="flex-wrap">
          {cardTabs.map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`card.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="details">
          <Details card={data} />
          <ExternalReferencesSection type="kpi.card" id={data.id} />
        </TabsContent>
        <TabsContent value="measuring">
          <Measuring card={data} />
        </TabsContent>
        <TabsContent value="alignment">
          <Alignment card={data} />
        </TabsContent>
        <TabsContent value="plans">
          <PlansTab card={data} />
        </TabsContent>
        <TabsContent value="performance">
          <PerformanceTab card={data} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel ownerType="kpi.card" ownerId={data.id} canEdit={data.canManage} />
        </TabsContent>
        <TabsContent value="linked">
          <LinkedTab cardId={data.id} />
        </TabsContent>
        <TabsContent value="versions">
          <VersionsTab card={data} />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type="kpi.card" id={data.id} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="kpiCard" id={data.id} />
        </TabsContent>
      </Tabs>
      {dialog === 'targets' && <TargetsDialog card={data} onClose={() => setDialog(undefined)} />}
      {dialog === 'change' && <ChangeDialog card={data} onClose={() => setDialog(undefined)} />}
      {dialog === 'smart' && <SmartDialog cardId={data.id} onClose={() => setDialog(undefined)} />}
      {dialog === 'suggest' && <SuggestDialog cardId={data.id} onClose={() => setDialog(undefined)} />}
    </RecordLayout>
  );
}

function Rows({ rows }: { rows: [string, ReactNode][] }) {
  return (
    <dl className="grid gap-4 rounded-lg border bg-card p-6 md:grid-cols-2">
      {rows.map(([label, value]) => (
        <div key={label}>
          <dt className="text-sm text-muted-foreground">{label}</dt>
          <dd className="mt-1 whitespace-pre-line">{value || '—'}</dd>
        </div>
      ))}
    </dl>
  );
}

function useLookupLabel() {
  const options = useKpiOptions();
  const { language } = useKpiT();
  return (type: string, code: string | null | undefined) => {
    if (!code) return '—';
    const option = options.data?.lookups[type]?.find((o) => o.code === code);
    return option ? pickText(language, option.labelAr, option.labelEn) || code : code;
  };
}

function Details({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { k, language } = useKpiT();
  const lookup = useLookupLabel();
  const v = card.values;
  return (
    <Rows
      rows={[
        [t('fields.code'), <bdi key="c">{card.code}</bdi>],
        [t('fields.nameAr'), v.nameAr],
        [t('fields.nameEn'), v.nameEn],
        [t('fields.descriptionAr'), v.descriptionAr],
        [t('fields.descriptionEn'), v.descriptionEn],
        [t('fields.processName'), v.processName],
        [t('fields.typeCode'), lookup('kpi.type', v.typeCode)],
        [t('fields.indexClassificationCode'), lookup('kpi.index-classification', v.indexClassificationCode)],
        [t('fields.effectivenessCode'), lookup('kpi.effectiveness', v.effectivenessCode)],
        [t('fields.nature'), k(`nature.${v.nature}`)],
        [t('fields.sourceCodes'), (v.sourceCodes ?? []).map((c) => lookup('kpi.source', c)).join('، ')],
        [t('fields.level'), k(`level.${v.level}`)],
        [t('fields.parentCardId'), card.parentCode],
        [t('fields.initiatedOn'), v.initiatedOn ? formatDate(v.initiatedOn, language) : '—'],
        [t('fields.closedOn'), card.closedOn ? formatDate(card.closedOn, language) : '—'],
        [
          t('fields.departmentUnitId'),
          card.departmentUnit
            ? pickText(language, card.departmentUnit.nameAr, card.departmentUnit.nameEn)
            : '—',
        ],
        [
          t('fields.responsibleUnitId'),
          card.responsibleUnit
            ? pickText(language, card.responsibleUnit.nameAr, card.responsibleUnit.nameEn)
            : '—',
        ],
        [
          t('fields.responsibleEmployeeId'),
          card.responsiblePerson
            ? pickText(language, card.responsiblePerson.nameAr, card.responsiblePerson.nameEn)
            : '—',
        ],
        [t('fields.definitionVersion'), String(card.definitionVersion)],
      ]}
    />
  );
}

function Measuring({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { k, language } = useKpiT();
  const lookup = useLookupLabel();
  const num = useNumber();
  const pct = usePercent();
  const v = card.values;
  return (
    <div className="flex flex-col gap-4">
      <Rows
        rows={[
          [t('fields.measuringEquation'), v.measuringEquation],
          [t('fields.unitCode'), lookup('kpi.unit', v.unitCode)],
          [t('fields.measuringPeriod'), k(`measuringPeriod.${v.measuringPeriod}`)],
          [t('fields.direction'), k(`direction.${v.direction}`)],
          [t('fields.targetType'), k(`targetType.${v.targetType}`)],
          [t('fields.measuringMethodCode'), lookup('kpi.measuring-method', v.measuringMethodCode)],
          [t('fields.targetSettingMechanism'), v.targetSettingMechanism],
          [t('fields.allowsNegative'), v.allowsNegative ? t('common.yes') : t('common.no')],
          [
            t('fields.baselineValue'),
            v.baselineValue == null
              ? '—'
              : `${num(v.baselineValue)}${v.baselineYear ? ` (${v.baselineYear})` : ''}`,
          ],
          [t('fields.benchmarkDescription'), v.benchmarkDescription],
          [t('fields.benchmarkResult'), pct(v.benchmarkResult)],
          [t('fields.maxAchieved'), pct(card.maxAchieved)],
          [t('fields.weight'), num(v.weight)],
          [
            t('fields.bands'),
            v.greenFrom != null ? `${pct(v.greenFrom)} / ${pct(v.amberFrom)}` : t('card.defaultBands'),
          ],
          [t('fields.formula'), v.formula],
        ]}
      />
      {(v.dataElements?.length ?? 0) > 0 && (
        <section className="rounded-lg border bg-card p-4">
          <h3 className="mb-2 font-semibold">{t('fields.dataElements')}</h3>
          <ul className="list-disc ps-6 text-sm">
            {v.dataElements?.map((e) => (
              <li key={e.code}>
                <code>{e.code}</code> — {pickText(language, e.nameAr, e.nameEn)}
              </li>
            ))}
          </ul>
        </section>
      )}
      {v.nature === 'qualitative' && (v.scale?.length ?? 0) > 0 && (
        <section className="rounded-lg border bg-card p-4">
          <h3 className="mb-2 font-semibold">{t('fields.scale')}</h3>
          <ul className="list-disc ps-6 text-sm">
            {v.scale?.map((s) => (
              <li key={s.value}>
                {num(s.value)} — {pickText(language, s.labelAr, s.labelEn)}
              </li>
            ))}
          </ul>
        </section>
      )}
      {card.targets.length > 0 && (
        <section className="rounded-lg border bg-card p-4">
          <h3 className="mb-2 font-semibold">{t('card.targets')}</h3>
          <ul className="flex flex-wrap gap-2 text-sm">
            {card.targets.map((tg) => (
              <li key={tg.year}>
                <StatusChip tone="neutral">
                  {tg.year}: {targetText(num, tg.targetType, tg.target, tg.rangeMin, tg.rangeMax)}
                </StatusChip>
              </li>
            ))}
          </ul>
        </section>
      )}
      {card.children.length > 0 && (
        <section className="rounded-lg border bg-card p-4" data-testid="kpi-cascade">
          <h3 className="mb-2 font-semibold">
            {t('card.cascade')} · <PerformanceValue capped={card.cascadedScore} />
          </h3>
          <ul className="flex flex-col divide-y text-sm">
            {card.children.map((child) => (
              <li key={child.id} className="flex items-center justify-between gap-2 py-2">
                <Link
                  to="/kpi/cards/$cardId"
                  params={{ cardId: child.id }}
                  className="text-primary hover:underline"
                >
                  <bdi>{child.code}</bdi> · {pickText(language, child.nameAr, child.nameEn)}
                </Link>
                <span className="flex items-center gap-2">
                  <PerformanceValue capped={child.capped} />
                  <RagChip rag={child.rag} />
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function Alignment({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { language } = useKpiT();
  const lookup = useLookupLabel();
  const v = card.values;
  return (
    <Rows
      rows={[
        [t('fields.perspective'), pickText(language, card.perspectiveNameAr, card.perspectiveNameEn)],
        [t('fields.objectiveId'), pickText(language, card.objectiveNameAr, card.objectiveNameEn)],
        [t('fields.sectorGoal'), v.sectorGoal],
        [t('fields.nationalGoalCode'), lookup('kpi.national-goal', v.nationalGoalCode)],
        [t('fields.visionLinkCode'), lookup('kpi.vision-link', v.visionLinkCode)],
        [
          t('fields.gemCriteriaCodes'),
          (v.gemCriteriaCodes ?? []).map((c) => lookup('kpi.gem-criterion', c)).join('، '),
        ],
        [t('fields.criteriaText'), v.criteriaText],
        [t('fields.excellenceLink'), v.excellenceLink],
        [t('fields.isGlobalStar'), v.isGlobalStar ? t('common.yes') : t('common.no')],
        [t('fields.competitivenessIndicator'), v.competitivenessIndicator ? t('common.yes') : t('common.no')],
      ]}
    />
  );
}

function PlansTab({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { k } = useKpiT();
  const num = useNumber();
  const performance = useKpiPerformance(card.id);
  if (!performance.data) return <Skeleton className="h-40 rounded-lg" />;
  const years = [...performance.data.years].sort((a, b) => b.year - a.year);
  if (years.length === 0) return <EmptyState title={t('card.noPlans')} />;
  return (
    <div className="flex flex-col gap-4">
      {years.map((y) => (
        <section key={y.year} className="rounded-lg border bg-card p-4" data-testid="kpi-card-year">
          <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h3 className="font-semibold">
              {y.year} · {t('fields.target')}:{' '}
              {targetText(num, y.targetType, y.target, y.rangeMin, y.rangeMax)}
            </h3>
            <span className="flex items-center gap-2">
              <PerformanceValue capped={y.capped} actual={y.actual} />
              <RagChip rag={y.rag} />
            </span>
          </header>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.period')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.achieve')}</TableHeaderCell>
                <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {performance.data.periods
                .filter((p) => p.year === y.year)
                .map((p) => (
                  <TableRow key={p.resultId}>
                    <TableCell>
                      <Link
                        to="/kpi/results/$resultId"
                        params={{ resultId: p.resultId }}
                        className="text-primary hover:underline"
                      >
                        {k(`period.${p.periodCode}`)}
                      </Link>
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {targetText(num, y.targetType, p.target, p.rangeMin, p.rangeMax)}
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {p.notApplicable ? t('result.na') : num(p.achieve)}
                    </TableCell>
                    <TableCell>
                      <PerformanceValue capped={p.capped} actual={p.actual} />
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={resultTone[p.status] ?? 'neutral'}>
                        {k(`resultStatus.${p.status}`)}
                      </StatusChip>
                    </TableCell>
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        </section>
      ))}
    </div>
  );
}

/** Yearly bars of actual and capped performance and the period trend (SPEC §2.5, §2.7 trend & comparison). */
function PerformanceTab({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { k } = useKpiT();
  const pct = usePercent();
  const num = useNumber();
  const year = currentYear();
  const performance = useKpiPerformance(card.id);
  if (!performance.data) return <Skeleton className="h-40 rounded-lg" />;
  const years = [...performance.data.years].sort((a, b) => a.year - b.year);
  const periods = performance.data.periods.filter((p) => p.year === year);
  const yearly = barOption({
    categories: years.map((y) => ({ key: String(y.year), label: String(y.year) })),
    series: [
      { name: t('card.actualShort'), values: years.map((y) => y.actual), color: token('info') },
      { name: t('card.cappedShort'), values: years.map((y) => y.capped), color: token('primary') },
    ],
    format: (v) => pct(v),
    categoryHeader: t('common.year'),
  });
  const targets = barOption({
    categories: periods.map((p) => ({ key: p.resultId, label: k(`period.${p.periodCode}`) })),
    series: [
      {
        name: t('fields.target'),
        values: periods.map((p) => p.target ?? p.rangeMax ?? null),
        color: token('neutral'),
      },
      { name: t('fields.achieve'), values: periods.map((p) => p.achieve ?? null), color: token('success') },
    ],
    format: (v) => num(v),
  });
  return (
    <div className="grid gap-4 lg:grid-cols-2" data-testid="kpi-performance-chart">
      <Section title={t('card.yearlyPerformance')}>
        {years.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('common.noData')}</p>
        ) : (
          <Chart {...yearly} height={240} label={t('card.yearlyPerformance')} />
        )}
      </Section>
      <Section title={t('card.targetVsAchieved', { year })}>
        {periods.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('common.noData')}</p>
        ) : (
          <Chart {...targets} height={240} label={t('card.targetVsAchieved', { year })} />
        )}
      </Section>
    </div>
  );
}

function LinkedTab({ cardId }: { cardId: string }) {
  const { t } = useTranslation('kpi');
  const linked = useKpiLinked(cardId);
  if (!linked.data) return <Skeleton className="h-24 rounded-lg" />;
  if (linked.data.length === 0)
    return <EmptyState title={t('card.noLinked')} description={t('card.linkedHint')} />;
  return (
    <ul className="flex flex-wrap gap-2" data-testid="kpi-linked">
      {linked.data.map((view) => (
        <li key={`${view.type}:${view.id}`}>
          <LinkBadge view={view} />
        </li>
      ))}
    </ul>
  );
}

function VersionsTab({ card }: { card: KpiCard }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const versions = useKpiVersions(card.id);
  const changes = useKpiChanges(card.id);
  const toast = useToast();
  const withdraw = useKpiMutation(kpiApi.withdrawChange);
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-changes">
        <h3 className="font-semibold">{t('changes.title')}</h3>
        {changes.data?.length === 0 && <p className="text-sm text-muted-foreground">{t('changes.empty')}</p>}
        <ul className="flex flex-col divide-y">
          {changes.data?.map((c) => (
            <li key={c.id} className="flex flex-col gap-1 py-3 text-sm">
              <span className="flex flex-wrap items-center gap-2">
                <StatusChip
                  tone={c.status === 'approved' ? 'success' : c.status === 'pending' ? 'info' : 'neutral'}
                >
                  {k(`changeStatus.${c.status}`)}
                </StatusChip>
                <span>{formatDateTime(c.createdAt, language)}</span>
              </span>
              <ChangeSummary proposed={c.proposed} current={c.current} />
              <span className="text-muted-foreground">{c.reason}</span>
              {c.decisionComment && <span className="text-muted-foreground">↳ {c.decisionComment}</span>}
              {c.status === 'pending' && card.canManage && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="w-fit"
                  onClick={() =>
                    withdraw.mutate(
                      { id: c.id, reason: null },
                      { onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
                    )
                  }
                >
                  {t('changes.withdraw')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-versions">
        <h3 className="font-semibold">{t('versions.title')}</h3>
        <ul className="flex flex-col divide-y">
          {versions.data?.map((v) => (
            <li key={v.versionNo} className="flex flex-col gap-0.5 py-2 text-sm">
              <span className="font-medium">
                {t('versions.version', { number: v.versionNo })} · {formatDateTime(v.createdAt, language)}
              </span>
              {v.reason && <span className="text-muted-foreground">{v.reason}</span>}
              {v.changeRequestId && <StatusChip tone="info">{t('versions.fromChange')}</StatusChip>}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function ChangeSummary({ proposed, current }: { proposed: ProposedChange; current: ProposedChange }) {
  const { t } = useTranslation('kpi');
  const num = useNumber();
  const items: string[] = [];
  const fields = [
    'measuringEquation',
    'formula',
    'unitCode',
    'direction',
    'targetType',
    'target',
    'rangeMin',
    'rangeMax',
  ] as const;
  for (const field of fields) {
    const next = proposed[field];
    if (next == null) continue;
    const before = current[field];
    const show = (v: string | number | null) => (typeof v === 'number' ? num(v) : (v ?? '—'));
    items.push(`${t(`changes.fields.${field}`)}: ${show(before)} → ${show(next)}`);
  }
  return (
    <ul className="list-disc ps-5">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  );
}

function TargetsDialog({ card, onClose }: { card: KpiCard; onClose: () => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k } = useKpiT();
  const toast = useToast();
  const save = useKpiMutation(kpiApi.setTargets);
  const [rows, setRows] = useState<KpiCardTarget[]>(
    card.targets.length
      ? card.targets
      : [
          {
            year: currentYear() + 1,
            targetType: card.values.targetType,
            target: null,
            rangeMin: null,
            rangeMax: null,
          },
        ],
  );
  const update = (i: number, patch: Partial<KpiCardTarget>) =>
    setRows((r) => r.map((row, j) => (j === i ? { ...row, ...patch } : row)));
  const n = (v: string) => (v === '' ? null : Number(v));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('card.targets')}
        description={t('card.targetsHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: card.id, targets: rows },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          {rows.map((row, i) => (
            <div key={i} className="grid grid-cols-[5rem_8rem_1fr_1fr_auto] items-end gap-2">
              <FormField label={t('common.year')}>
                <Input
                  type="number"
                  value={row.year}
                  onChange={(e) => update(i, { year: Number(e.target.value) })}
                />
              </FormField>
              <FormField label={t('fields.targetType')}>
                <Select
                  value={row.targetType}
                  onValueChange={(v) => update(i, { targetType: v })}
                  options={['single', 'range'].map((v) => ({ value: v, label: k(`targetType.${v}`) }))}
                />
              </FormField>
              {row.targetType === 'range' ? (
                <>
                  <FormField label={t('fields.rangeMin')}>
                    <Input
                      type="number"
                      value={row.rangeMin ?? ''}
                      onChange={(e) => update(i, { rangeMin: n(e.target.value) })}
                    />
                  </FormField>
                  <FormField label={t('fields.rangeMax')}>
                    <Input
                      type="number"
                      value={row.rangeMax ?? ''}
                      onChange={(e) => update(i, { rangeMax: n(e.target.value) })}
                    />
                  </FormField>
                </>
              ) : (
                <FormField label={t('fields.target')} className="col-span-2">
                  <Input
                    type="number"
                    value={row.target ?? ''}
                    onChange={(e) => update(i, { target: n(e.target.value) })}
                  />
                </FormField>
              )}
              <Button
                variant="ghost"
                size="icon"
                aria-label={t('actions.remove')}
                onClick={() => setRows((r) => r.filter((_, j) => j !== i))}
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          ))}
          <Button
            variant="outline"
            size="sm"
            className="w-fit"
            onClick={() =>
              setRows((r) => [
                ...r,
                {
                  year: (r.at(-1)?.year ?? currentYear()) + 1,
                  targetType: card.values.targetType,
                  target: null,
                  rangeMin: null,
                  rangeMax: null,
                },
              ])
            }
          >
            <Plus aria-hidden />
            {t('card.addYear')}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ChangeDialog({ card, onClose }: { card: KpiCard; onClose: () => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k } = useKpiT();
  const toast = useToast();
  const options = useKpiOptions();
  const request = useKpiMutation(kpiApi.requestChange);
  const [p, setP] = useState<ProposedChange>({
    measuringEquation: null,
    formula: null,
    unitCode: null,
    direction: null,
    targetType: null,
    target: null,
    rangeMin: null,
    rangeMax: null,
  });
  const [reason, setReason] = useState('');
  const n = (v: string) => (v === '' ? null : Number(v));
  const KEEP = '__keep';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('changes.request')}
        description={t('changes.hint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={request.isPending}
              data-testid="kpi-change-submit"
              onClick={() =>
                request.mutate(
                  { id: card.id, proposed: p, reason },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {t('changes.submit')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('fields.measuringEquation')} className="md:col-span-2">
            <Input
              value={p.measuringEquation ?? ''}
              placeholder={card.values.measuringEquation ?? ''}
              onChange={(e) => setP({ ...p, measuringEquation: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('fields.unitCode')}>
            <Select
              value={p.unitCode ?? KEEP}
              onValueChange={(v) => setP({ ...p, unitCode: v === KEEP ? null : v })}
              options={[
                { value: KEEP, label: t('changes.keep') },
                ...(options.data?.lookups['kpi.unit'] ?? []).map((o) => ({
                  value: o.code,
                  label: o.labelEn ?? o.code,
                })),
              ]}
            />
          </FormField>
          <FormField label={t('fields.direction')}>
            <Select
              value={p.direction ?? KEEP}
              onValueChange={(v) => setP({ ...p, direction: v === KEEP ? null : v })}
              options={[
                { value: KEEP, label: t('changes.keep') },
                ...['increase', 'decrease', 'matching'].map((v) => ({
                  value: v,
                  label: k(`direction.${v}`),
                })),
              ]}
            />
          </FormField>
          <FormField label={t('fields.targetType')}>
            <Select
              value={p.targetType ?? KEEP}
              onValueChange={(v) => setP({ ...p, targetType: v === KEEP ? null : v })}
              options={[
                { value: KEEP, label: t('changes.keep') },
                ...['single', 'range'].map((v) => ({ value: v, label: k(`targetType.${v}`) })),
              ]}
            />
          </FormField>
          <FormField label={t('fields.target')}>
            <Input
              type="number"
              value={p.target ?? ''}
              onChange={(e) => setP({ ...p, target: n(e.target.value) })}
              data-testid="kpi-change-target"
            />
          </FormField>
          <FormField label={t('fields.rangeMin')}>
            <Input
              type="number"
              value={p.rangeMin ?? ''}
              onChange={(e) => setP({ ...p, rangeMin: n(e.target.value) })}
            />
          </FormField>
          <FormField label={t('fields.rangeMax')}>
            <Input
              type="number"
              value={p.rangeMax ?? ''}
              onChange={(e) => setP({ ...p, rangeMax: n(e.target.value) })}
            />
          </FormField>
          <FormField label={t('changes.reason')} required className="md:col-span-2">
            <Textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              data-testid="kpi-change-reason"
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function SmartDialog({ cardId, onClose }: { cardId: string; onClose: () => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const smart = useQuery({
    queryKey: ['kpi', 'smart', cardId],
    queryFn: () => kpiApi.smart(cardId),
    retry: false,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent title={t('ai.smart.title')} description={t('ai.smart.hint')}>
        {smart.isError && <StatusBanner tone="danger" title={errorMessage(tc, smart.error)} />}
        {smart.isPending && <Skeleton className="h-24" />}
        {smart.data && <SmartList checks={smart.data} />}
      </DialogContent>
    </Dialog>
  );
}

export function SmartList({ checks }: { checks: { code: string; passed: boolean }[] }) {
  const { k } = useKpiT();
  return (
    <ul className="flex flex-col gap-1 text-sm" data-testid="kpi-smart">
      {checks.map((c) => (
        <li key={c.code} className="flex items-center gap-2">
          <StatusChip tone={c.passed ? 'success' : 'warning'}>{c.passed ? '✓' : '!'}</StatusChip>
          {k(`ai.smart.checks.${c.code}`)}
        </li>
      ))}
    </ul>
  );
}

function SuggestDialog({ cardId, onClose }: { cardId: string; onClose: () => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const num = useNumber();
  const [year, setYear] = useState(currentYear() + 1);
  const [result, setResult] = useState<Awaited<ReturnType<typeof kpiApi.targetSuggestion>>>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent title={t('ai.targets.title')} description={t('ai.targets.hint')}>
        <div className="flex flex-col gap-3">
          <div className="flex items-end gap-2">
            <FormField label={t('common.year')}>
              <Input type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} />
            </FormField>
            <Button
              loading={busy}
              onClick={() => {
                setBusy(true);
                setError(undefined);
                kpiApi
                  .targetSuggestion({ id: cardId, year })
                  .then(setResult)
                  .catch((e: unknown) => setError(errorMessage(tc, e)))
                  .finally(() => setBusy(false));
              }}
            >
              {t('ai.targets.run')}
            </Button>
          </div>
          {error && <StatusBanner tone="danger" title={error} />}
          {result && (
            <div className="flex flex-col gap-2 text-sm" data-testid="kpi-target-suggestion">
              <StatusChip tone="info" className="w-fit">
                {t('ai.draftLabel')}
              </StatusChip>
              <p className="text-lg font-semibold">
                {t('ai.targets.suggested')}: {num(result.suggested)}
              </p>
              <ul className="list-disc ps-5">
                {result.reasons.map((r) => (
                  <li key={`${r.key}-${r.year ?? ''}`}>
                    {translateMessageKey(tc, r.key, {
                      value: r.value == null ? '—' : num(r.value),
                      year: r.year ?? '',
                    })}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
