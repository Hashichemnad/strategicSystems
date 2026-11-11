import { getRouteApi, Link } from '@tanstack/react-router';
import { Gauge, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  Input,
  ListViewToggle,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SplitView,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useListView,
} from '@/ui';
import { SplitCard, SplitPreview } from '@/ui';
import { currentYear, useKpiCards, useKpiOptions } from './api';
import { KpiPage, PerformanceValue, RagChip, useKpiT, useNumber } from './components';

const route = getRouteApi('/app/kpi/cards');
const ALL = '__all';

/** KPI cards register (SPEC §2.5): filters by code / name, department, type, objective, status, GEM criterion and Global Star. */
export function KpiCardsPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('appearance');
  const { k, language } = useKpiT();
  const num = useNumber();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const options = useKpiOptions();
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    const handle = setTimeout(() => {
      if ((search.search ?? '') !== text)
        void navigate({ search: (p) => ({ ...p, search: text || undefined, page: undefined }) });
    }, 300);
    return () => clearTimeout(handle);
  }, [text, search.search, navigate]);
  const year = currentYear();
  const cards = useKpiCards({
    page: search.page ?? 1,
    pageSize: 25,
    year,
    ...(search.search ? { search: search.search } : {}),
    ...(search.unitId ? { unitId: search.unitId } : {}),
    ...(search.type ? { typeCode: search.type } : {}),
    ...(search.objective ? { objectiveId: search.objective } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.gem ? { gem: search.gem } : {}),
    ...(search.star ? { globalStar: true } : {}),
  });
  const lookup = (type: string) =>
    (options.data?.lookups[type] ?? []).map((o) => ({
      value: o.code,
      label: pickText(language, o.labelAr, o.labelEn) || o.code,
    }));
  const set = (patch: Record<string, unknown>) =>
    void navigate({ search: (p) => ({ ...p, ...patch, page: undefined }) });
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('kpi.cards');
  const [selectedId, setSelectedId] = useState<string>();
  // The first row is previewed until the user picks another.
  const selected = cards.data?.items.find((c) => c.id === selectedId) ?? cards.data?.items[0];
  const targetText = (card: KpiCardItem) =>
    card.targetType === 'range'
      ? card.rangeMin == null || card.rangeMax == null
        ? '—'
        : `${num(card.rangeMin)} – ${num(card.rangeMax)}`
      : card.target == null
        ? '—'
        : num(card.target);

  return (
    <KpiPage>
      <PageHeader
        title={t('cards.title')}
        description={t('cards.description')}
        actions={
          (options.data?.canManageCards || listView.available) && (
            <>
              {listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
              {options.data?.canManageCards && (
                <Button asChild>
                  <Link to="/kpi/cards/new" data-testid="kpi-new-card">
                    <Plus aria-hidden />
                    {t('cards.new')}
                  </Link>
                </Button>
              )}
            </>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('cards.search')}
            aria-label={t('cards.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
            data-testid="kpi-cards-search"
          />
        </div>
        <UnitSelect allowEmpty value={search.unitId} onChange={(unitId) => set({ unitId })} />
        <Select
          aria-label={t('fields.typeCode')}
          value={search.type ?? ALL}
          onValueChange={(v) => set({ type: v === ALL ? undefined : v })}
          options={[{ value: ALL, label: t('cards.allTypes') }, ...lookup('kpi.type')]}
        />
        <Select
          aria-label={t('fields.objectiveId')}
          value={search.objective ?? ALL}
          onValueChange={(v) => set({ objective: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('cards.allObjectives') },
            ...(options.data?.elements ?? [])
              .filter((e) => e.kind === 'objective')
              .map((e) => ({ value: e.id, label: `${e.code} · ${pickText(language, e.nameAr, e.nameEn)}` })),
          ]}
        />
        <Select
          aria-label={t('fields.status')}
          value={search.status ?? ALL}
          onValueChange={(v) => set({ status: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('cards.allStatuses') },
            { value: 'active', label: t('cardStatus.active') },
            { value: 'inactive', label: t('cardStatus.inactive') },
          ]}
        />
        <Select
          aria-label={t('fields.gemCriteriaCodes')}
          value={search.gem ?? ALL}
          onValueChange={(v) => set({ gem: v === ALL ? undefined : v })}
          options={[{ value: ALL, label: t('cards.allGem') }, ...lookup('kpi.gem-criterion')]}
        />
        <Select
          aria-label={t('fields.isGlobalStar')}
          value={search.star ? 'yes' : ALL}
          onValueChange={(v) => set({ star: v === 'yes' ? true : undefined })}
          options={[
            { value: ALL, label: t('cards.allStar') },
            { value: 'yes', label: t('fields.isGlobalStar') },
          ]}
        />
      </div>
      {cards.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, cards.error)} />
      ) : !cards.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : cards.data.items.length === 0 ? (
        <EmptyState icon={Gauge} title={t('cards.empty')} />
      ) : (
        <>
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="kpi-cards-cards">
                  {cards.data.items.map((card) => (
                    <SplitCard
                      key={card.id}
                      selected={card.id === selected?.id}
                      onPreview={() => setSelectedId(card.id)}
                      title={
                        <Link
                          to="/kpi/cards/$cardId"
                          params={{ cardId: card.id }}
                          className="min-w-0 font-medium hover:underline"
                        >
                          <bdi className="block text-xs text-muted-foreground">{card.code}</bdi>
                          <span dir="auto" className="block">
                            {pickText(language, card.nameAr, card.nameEn)}
                          </span>
                        </Link>
                      }
                      chips={
                        <>
                          <StatusChip tone={card.status === 'active' ? 'success' : 'neutral'}>
                            {k(`cardStatus.${card.status}`)}
                          </StatusChip>
                          <RagChip rag={card.rag} />
                          <span className="text-xs text-muted-foreground">
                            {k(`measuringPeriod.${card.measuringPeriod}`)}
                          </span>
                        </>
                      }
                      progress={
                        card.capped == null
                          ? undefined
                          : {
                              value: card.capped,
                              label: t('cards.performance'),
                              tone:
                                card.rag === 'red' ? 'danger' : card.rag === 'green' ? 'success' : undefined,
                            }
                      }
                    />
                  ))}
                </ul>
              }
              preview={
                selected && (
                  <SplitPreview
                    title={pickText(language, selected.nameAr, selected.nameEn)}
                    rows={[
                      [t('fields.code'), <bdi key="c">{selected.code}</bdi>],
                      [
                        t('fields.responsibleUnitId'),
                        selected.responsibleUnit
                          ? pickText(
                              language,
                              selected.responsibleUnit.nameAr,
                              selected.responsibleUnit.nameEn,
                            )
                          : '—',
                      ],
                      [t('fields.measuringPeriod'), k(`measuringPeriod.${selected.measuringPeriod}`)],
                      [t('cards.target', { year }), targetText(selected)],
                      [t('cards.achieved'), num(selected.achieved)],
                      [
                        t('cards.performance'),
                        <span key="p" className="flex flex-wrap items-center gap-2">
                          <PerformanceValue capped={selected.capped} actual={selected.actual} />
                          <RagChip rag={selected.rag} />
                        </span>,
                      ],
                      [t('fields.status'), k(`cardStatus.${selected.status}`)],
                    ]}
                    open={
                      <Link to="/kpi/cards/$cardId" params={{ cardId: selected.id }}>
                        {ta('split.open')}
                      </Link>
                    }
                  />
                )
              }
            />
          ) : (
            <Table data-testid="kpi-cards-table">
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.responsibleUnitId')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.measuringPeriod')}</TableHeaderCell>
                  <TableHeaderCell>{t('cards.target', { year })}</TableHeaderCell>
                  <TableHeaderCell>{t('cards.achieved')}</TableHeaderCell>
                  <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {cards.data.items.map((card) => (
                  <TableRow key={card.id} data-testid="kpi-card-row">
                    <TableCell>
                      <Link
                        to="/kpi/cards/$cardId"
                        params={{ cardId: card.id }}
                        className="font-medium text-primary hover:underline"
                      >
                        <bdi>{card.code}</bdi>
                      </Link>
                    </TableCell>
                    <TableCell>
                      <span className="flex flex-col">
                        <span>{pickText(language, card.nameAr, card.nameEn)}</span>
                        {card.objectiveId && (
                          <span className="text-xs text-muted-foreground">
                            {pickText(language, card.objectiveNameAr, card.objectiveNameEn)}
                          </span>
                        )}
                      </span>
                    </TableCell>
                    <TableCell>
                      {card.responsibleUnit
                        ? pickText(language, card.responsibleUnit.nameAr, card.responsibleUnit.nameEn)
                        : '—'}
                    </TableCell>
                    <TableCell>{k(`measuringPeriod.${card.measuringPeriod}`)}</TableCell>
                    <TableCell className="tabular-nums">{targetText(card)}</TableCell>
                    <TableCell className="tabular-nums">{num(card.achieved)}</TableCell>
                    <TableCell>
                      <span className="flex flex-wrap items-center gap-2">
                        <PerformanceValue capped={card.capped} actual={card.actual} />
                        <RagChip rag={card.rag} />
                      </span>
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={card.status === 'active' ? 'success' : 'neutral'}>
                        {k(`cardStatus.${card.status}`)}
                      </StatusChip>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <Pagination
            page={cards.data.page}
            totalPages={cards.data.totalPages ?? 1}
            totalCount={cards.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </KpiPage>
  );
}

type KpiCardItem = NonNullable<ReturnType<typeof useKpiCards>['data']>['items'][number];
