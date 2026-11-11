import { getRouteApi, Link } from '@tanstack/react-router';
import { Plus, Scale, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useNames } from '@/features/cap/components';
import { cn } from '@/lib/cn';
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
import {
  benchLookups,
  studyStages,
  studyViews,
  useBenchOptions,
  useBenchStudies,
  type BenchStudyListItem,
} from './api';
import { BenchPageLayout, StageChip, useBenchLabels, useBenchT } from './components';

const route = getRouteApi('/app/bench/studies');
const ALL = '__all';

export function StudiesTable({ rows }: { rows: readonly BenchStudyListItem[] }) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const names = useNames();
  const labels = useBenchLabels();
  return (
    <Table data-testid="bench-studies-table">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.responsibleUnit')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.coordinator')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.comparisonType')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.stage')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((s) => (
          <TableRow key={s.id} data-testid="bench-study-row">
            <TableCell>
              <Link
                to="/bench/studies/$studyId"
                params={{ studyId: s.id }}
                className="font-medium text-primary hover:underline"
              >
                <bdi>{s.number}</bdi>
              </Link>
            </TableCell>
            <TableCell>{names.text(s.titleAr, s.titleEn)}</TableCell>
            <TableCell>{names.unit(s.responsibleUnit)}</TableCell>
            <TableCell>{names.person(s.responsibleEmployee)}</TableCell>
            <TableCell>
              <bdi>
                Q{s.targetQuarter} {s.targetYear}
              </bdi>
            </TableCell>
            <TableCell>{labels.lookup(benchLookups.comparisonType, s.comparisonTypeCode)}</TableCell>
            <TableCell>
              <span className="flex flex-wrap items-center gap-1">
                <StageChip stage={s.stage} status={s.status} />
                {s.pendingProcess && <StatusChip tone="info">{k(`process.${s.pendingProcess}`)}</StatusChip>}
                {s.returned && <StatusChip tone="warning">{t('study.returned')}</StatusChip>}
              </span>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** Studies as list + preview (workspace themes): the first study is previewed until the user picks another. */
function StudiesSplit({ rows }: { rows: readonly BenchStudyListItem[] }) {
  const { t } = useTranslation('bench');
  const { t: ta } = useTranslation('appearance');
  const { k } = useBenchT();
  const names = useNames();
  const labels = useBenchLabels();
  const [selectedId, setSelectedId] = useState<string>();
  const selected = rows.find((s) => s.id === selectedId) ?? rows[0];
  const target = (s: BenchStudyListItem) => (
    <bdi>
      Q{s.targetQuarter} {s.targetYear}
    </bdi>
  );
  return (
    <SplitView
      hasSelection={!!selected}
      list={
        <ul className="flex flex-col gap-2" data-testid="bench-studies-cards">
          {rows.map((s) => (
            <SplitCard
              key={s.id}
              selected={s.id === selected?.id}
              onPreview={() => setSelectedId(s.id)}
              title={
                <Link
                  to="/bench/studies/$studyId"
                  params={{ studyId: s.id }}
                  className="min-w-0 font-medium hover:underline"
                >
                  <bdi className="block text-xs text-muted-foreground">{s.number}</bdi>
                  <span dir="auto" className="block">
                    {names.text(s.titleAr, s.titleEn)}
                  </span>
                </Link>
              }
              chips={
                <>
                  <StageChip stage={s.stage} status={s.status} />
                  {s.pendingProcess && (
                    <StatusChip tone="info">{k(`process.${s.pendingProcess}`)}</StatusChip>
                  )}
                  {s.returned && <StatusChip tone="warning">{t('study.returned')}</StatusChip>}
                  <span className="text-xs text-muted-foreground">{target(s)}</span>
                </>
              }
            />
          ))}
        </ul>
      }
      preview={
        selected && (
          <SplitPreview
            title={names.text(selected.titleAr, selected.titleEn)}
            rows={[
              [t('fields.number'), <bdi key="n">{selected.number}</bdi>],
              [t('fields.responsibleUnit'), names.unit(selected.responsibleUnit)],
              [t('fields.coordinator'), names.person(selected.responsibleEmployee)],
              [t('fields.target'), target(selected)],
              [
                t('fields.comparisonType'),
                labels.lookup(benchLookups.comparisonType, selected.comparisonTypeCode),
              ],
              [t('fields.stage'), <StageChip key="s" stage={selected.stage} status={selected.status} />],
            ]}
            open={
              <Link to="/bench/studies/$studyId" params={{ studyId: selected.id }}>
                {ta('split.open')}
              </Link>
            }
          />
        )
      }
    />
  );
}

/** Studies (BMS-NNNN): views and filters, scoped on the server. A new study starts as a request (Plan stage). */
export function BenchStudiesPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const { k } = useBenchT();
  const options = useBenchOptions();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    const handle = setTimeout(() => {
      if ((search.search ?? '') !== text)
        void navigate({ search: (p) => ({ ...p, search: text || undefined, page: undefined }) });
    }, 300);
    return () => clearTimeout(handle);
  }, [text, search.search, navigate]);
  const view = search.view ?? 'all';
  const studies = useBenchStudies({
    page: search.page ?? 1,
    pageSize: 25,
    ...(view !== 'all' ? { view } : {}),
    ...(search.search ? { search: search.search } : {}),
    ...(search.stage ? { stage: search.stage } : {}),
    ...(search.year ? { year: search.year } : {}),
  });
  const set = (patch: Record<string, unknown>) =>
    void navigate({ search: (p) => ({ ...p, ...patch, page: undefined }) });
  const year0 = new Date().getFullYear();
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('bench.studies');

  return (
    <BenchPageLayout testId="bench-studies">
      <PageHeader
        title={t('studies.title')}
        description={t('studies.description')}
        actions={
          (options.data?.canCreate || listView.available) && (
            <>
              {listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
              {options.data?.canCreate && (
                <Button asChild>
                  <Link
                    to="/requests/new/$typeCode"
                    params={{ typeCode: options.data.requestTypeCode }}
                    data-testid="bench-new-study"
                  >
                    <Plus aria-hidden />
                    {t('studies.new')}
                  </Link>
                </Button>
              )}
            </>
          )
        }
      />
      <div role="tablist" aria-label={t('studies.views')} className="flex flex-wrap gap-1">
        {studyViews.map((v) => (
          <button
            key={v}
            type="button"
            role="tab"
            aria-selected={view === v}
            onClick={() => set({ view: v === 'all' ? undefined : v })}
            className={cn(
              'rounded-full border px-3 py-1 text-sm',
              view === v ? 'border-primary bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
          >
            {k(`view.${v}`)}
          </button>
        ))}
      </div>
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('common.search')}
            aria-label={t('common.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
        </div>
        <Select
          aria-label={t('fields.stage')}
          value={search.stage ?? ALL}
          onValueChange={(v) => set({ stage: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('common.allStages') },
            ...studyStages.map((s) => ({ value: s, label: k(`stage.${s}`) })),
          ]}
        />
        <Select
          aria-label={t('fields.year')}
          value={search.year ? String(search.year) : ALL}
          onValueChange={(v) => set({ year: v === ALL ? undefined : Number(v) })}
          options={[
            { value: ALL, label: t('common.allYears') },
            ...[year0 + 1, year0, year0 - 1].map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
      </div>
      {studies.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, studies.error)} />
      ) : !studies.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : studies.data.items.length === 0 ? (
        <EmptyState icon={Scale} title={t('studies.empty')} />
      ) : (
        <>
          {listView.view === 'split' ? (
            <StudiesSplit rows={studies.data.items} />
          ) : (
            <StudiesTable rows={studies.data.items} />
          )}
          <Pagination
            page={studies.data.page}
            totalPages={studies.data.totalPages ?? 1}
            totalCount={studies.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </BenchPageLayout>
  );
}
