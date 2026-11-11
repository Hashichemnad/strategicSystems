import { Link } from '@tanstack/react-router';
import { FileText, Search, Sparkles, X } from 'lucide-react';
import { useEffect, useState, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useListView,
} from '@/ui';
import { SplitCard, SplitPreview } from '@/ui';
import { AiBadge } from '@/features/ai/AiDraft';
import {
  docregApi,
  isAiUnavailable,
  useDocRegAi,
  useLibrary,
  type Answer,
  type Facet,
  type LibraryQuery,
  type UnitFacet,
} from './api';
import {
  DocRegPage,
  DocStatusChip,
  documentName,
  SensitivityChip,
  useFormatDate,
  useLookupLabel,
  useNumber,
  ValidityChips,
} from './components';

const ALL = '__all';
const PAGE_SIZE = 25;
const expiringOptions = [30, 60, 90, 180] as const;

interface Filters {
  level?: string;
  type?: string;
  unitId?: string;
  status?: string;
  sensitivity?: string;
  expiringWithin?: number;
  search?: string;
  page: number;
}

/** Document library (docs/design/requests.md §3.6): browse level → type → owner unit, search, filters and "Ask the documents". */
export function DocumentLibraryPage() {
  const { t } = useTranslation('docreg');
  const { t: tc, i18n } = useTranslation();
  const [filters, setFilters] = useState<Filters>({ page: 1 });
  const [text, setText] = useState('');
  useEffect(() => {
    const handle = setTimeout(() => {
      const search = text.trim() || undefined;
      setFilters((f) => (f.search === search ? f : { ...f, search, page: 1 }));
    }, 300);
    return () => clearTimeout(handle);
  }, [text]);

  const query: LibraryQuery = {
    page: filters.page,
    pageSize: PAGE_SIZE,
    ...(filters.level ? { level: filters.level } : {}),
    ...(filters.type ? { type: filters.type } : {}),
    ...(filters.unitId ? { unitId: filters.unitId } : {}),
    ...(filters.status ? { status: filters.status } : {}),
    ...(filters.sensitivity ? { sensitivity: filters.sensitivity } : {}),
    ...(filters.expiringWithin ? { expiringWithin: filters.expiringWithin } : {}),
    ...(filters.search ? { search: filters.search } : {}),
  };
  const library = useLibrary(query);
  const levelLabel = useLookupLabel('docreg.level');
  const typeLabel = useLookupLabel('docreg.type');
  const sensitivityLabel = useLookupLabel('docreg.sensitivity');
  const set = (patch: Partial<Filters>) => setFilters((f) => ({ ...f, ...patch, page: 1 }));
  const filtered =
    !!filters.level ||
    !!filters.type ||
    !!filters.unitId ||
    !!filters.status ||
    !!filters.sensitivity ||
    !!filters.expiringWithin ||
    !!filters.search;
  const sensitivityCodes = ['public', 'internal', 'confidential', 'secret'];
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('docreg.library');

  return (
    <DocRegPage testId="docreg-library">
      <PageHeader
        title={t('library.title')}
        description={t('library.description')}
        actions={listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
      />
      <AskBox />
      <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
        <aside className="flex flex-col gap-4" aria-label={t('library.browse')}>
          <FacetList
            title={t('library.levels')}
            facets={library.data?.levels ?? []}
            label={levelLabel}
            selected={filters.level}
            onSelect={(level) => set({ level })}
          />
          <FacetList
            title={t('library.types')}
            facets={library.data?.types ?? []}
            label={typeLabel}
            selected={filters.type}
            onSelect={(type) => set({ type })}
          />
          <UnitFacets
            units={library.data?.units ?? []}
            selected={filters.unitId}
            onSelect={(unitId) => set({ unitId })}
          />
        </aside>
        <div className="flex min-w-0 flex-col gap-4">
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <div className="relative md:col-span-2 xl:col-span-1">
              <Search
                className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
                aria-hidden
              />
              <Input
                className="ps-9"
                placeholder={t('library.search')}
                aria-label={t('library.search')}
                value={text}
                onChange={(e) => setText(e.target.value)}
                data-testid="library-search"
              />
            </div>
            <Select
              aria-label={t('fields.status')}
              value={filters.status ?? ALL}
              onValueChange={(v) => set({ status: v === ALL ? undefined : v })}
              options={[
                { value: ALL, label: t('library.allStatuses') },
                { value: 'published', label: t('status.published') },
                { value: 'expired', label: t('status.expired') },
              ]}
            />
            <Select
              aria-label={t('fields.sensitivity')}
              value={filters.sensitivity ?? ALL}
              onValueChange={(v) => set({ sensitivity: v === ALL ? undefined : v })}
              options={[
                { value: ALL, label: t('library.allSensitivities') },
                ...sensitivityCodes.map((code) => ({ value: code, label: sensitivityLabel(code) })),
              ]}
            />
            <Select
              aria-label={t('library.expiring')}
              value={filters.expiringWithin ? String(filters.expiringWithin) : ALL}
              onValueChange={(v) => set({ expiringWithin: v === ALL ? undefined : Number(v) })}
              options={[
                { value: ALL, label: t('library.anyExpiry') },
                ...expiringOptions.map((days) => ({
                  value: String(days),
                  label: t('library.expiringWithin', { days }),
                })),
              ]}
            />
          </div>
          {filtered && (
            <div>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setText('');
                  setFilters({ page: 1 });
                }}
              >
                <X aria-hidden />
                {t('library.clear')}
              </Button>
            </div>
          )}
          {library.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, library.error)} />
          ) : !library.data ? (
            <Skeleton className="h-64 rounded-lg" />
          ) : library.data.page.items.length === 0 ? (
            <EmptyState icon={FileText} title={t('library.empty')} description={t('library.emptyHint')} />
          ) : (
            <>
              <p className="text-sm text-muted-foreground" aria-live="polite">
                {t('library.results', { count: library.data.page.totalCount })}
              </p>
              {listView.view === 'split' ? (
                <LibrarySplit items={library.data.page.items} language={i18n.language} />
              ) : (
                <LibraryTable items={library.data.page.items} language={i18n.language} />
              )}
              <Pagination
                page={library.data.page.page}
                totalPages={
                  library.data.page.totalPages ??
                  Math.max(1, Math.ceil(library.data.page.totalCount / library.data.page.pageSize))
                }
                totalCount={library.data.page.totalCount}
                onPageChange={(page) => setFilters((f) => ({ ...f, page }))}
              />
            </>
          )}
        </div>
      </div>
    </DocRegPage>
  );
}

type LibraryItem = NonNullable<ReturnType<typeof useLibrary>['data']>['page']['items'][number];

/** Library as list + preview (workspace themes): the first document is previewed until the user picks another. */
function LibrarySplit({ items, language }: { items: LibraryItem[]; language: string }) {
  const { t } = useTranslation('docreg');
  const { t: ta } = useTranslation('appearance');
  const levelLabel = useLookupLabel('docreg.level');
  const typeLabel = useLookupLabel('docreg.type');
  const date = useFormatDate();
  const [selectedId, setSelectedId] = useState<string>();
  const selected = items.find((d) => d.id === selectedId) ?? items[0];
  const expiry = (doc: LibraryItem) => (doc.expiryDate ? date(doc.expiryDate) : t('common.untilSuperseded'));
  return (
    <SplitView
      hasSelection={!!selected}
      list={
        <ul className="flex flex-col gap-2" data-testid="library-cards">
          {items.map((doc) => (
            <SplitCard
              key={doc.id}
              selected={doc.id === selected?.id}
              onPreview={() => setSelectedId(doc.id)}
              title={
                <Link
                  to="/documents/$documentId"
                  params={{ documentId: doc.id }}
                  search={{ tab: 'overview' }}
                  className="min-w-0 font-medium hover:underline"
                >
                  <bdi className="block text-xs text-muted-foreground">{doc.documentNo ?? '—'}</bdi>
                  <span dir="auto" className="block">
                    {documentName(language, doc, t('document.untitled'))}
                  </span>
                </Link>
              }
              chips={
                <>
                  <DocStatusChip status={doc.status} />
                  <SensitivityChip code={doc.sensitivity} />
                  <ValidityChips
                    status={doc.status}
                    expiryDate={doc.expiryDate}
                    underRevision={doc.underRevision}
                  />
                  <span className="text-xs text-muted-foreground">{typeLabel(doc.docType)}</span>
                </>
              }
            />
          ))}
        </ul>
      }
      preview={
        selected && (
          <SplitPreview
            title={documentName(language, selected, t('document.untitled'))}
            rows={[
              [t('fields.documentNo'), <bdi key="n">{selected.documentNo ?? '—'}</bdi>],
              [t('fields.type'), `${typeLabel(selected.docType)} · ${levelLabel(selected.level)}`],
              [t('fields.version'), <bdi key="v">{selected.currentVersion ?? '—'}</bdi>],
              [
                t('fields.ownerUnit'),
                selected.ownerUnit
                  ? pickText(language, selected.ownerUnit.nameAr, selected.ownerUnit.nameEn)
                  : '—',
              ],
              [t('fields.effectiveDate'), date(selected.effectiveDate)],
              [t('fields.expiryDate'), expiry(selected)],
              [t('fields.status'), <DocStatusChip key="s" status={selected.status} />],
            ]}
            open={
              <Link
                to="/documents/$documentId"
                params={{ documentId: selected.id }}
                search={{ tab: 'overview' }}
              >
                {ta('split.open')}
              </Link>
            }
          />
        )
      }
    />
  );
}

function LibraryTable({
  items,
  language,
}: {
  items: NonNullable<ReturnType<typeof useLibrary>['data']>['page']['items'];
  language: string;
}) {
  const { t } = useTranslation('docreg');
  const levelLabel = useLookupLabel('docreg.level');
  const typeLabel = useLookupLabel('docreg.type');
  const date = useFormatDate();
  return (
    <Table data-testid="library-table">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('fields.documentNo')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.type')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.version')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.ownerUnit')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.effectiveDate')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.expiryDate')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((doc) => (
          <TableRow key={doc.id} data-testid={`library-row-${doc.id}`}>
            <TableCell className="whitespace-nowrap">
              <Link
                to="/documents/$documentId"
                params={{ documentId: doc.id }}
                search={{ tab: 'overview' }}
                className="font-medium text-primary hover:underline"
              >
                <bdi>{doc.documentNo ?? '—'}</bdi>
              </Link>
            </TableCell>
            <TableCell>
              <span className="flex flex-col gap-1">
                <Link
                  to="/documents/$documentId"
                  params={{ documentId: doc.id }}
                  search={{ tab: 'overview' }}
                  className="hover:underline"
                >
                  {documentName(language, doc, t('document.untitled'))}
                </Link>
                <span className="flex flex-wrap gap-1">
                  <SensitivityChip code={doc.sensitivity} />
                  <ValidityChips
                    status={doc.status}
                    expiryDate={doc.expiryDate}
                    underRevision={doc.underRevision}
                  />
                </span>
              </span>
            </TableCell>
            <TableCell>
              <span className="flex flex-col">
                <span>{typeLabel(doc.docType)}</span>
                <span className="text-xs text-muted-foreground">{levelLabel(doc.level)}</span>
              </span>
            </TableCell>
            <TableCell className="tabular-nums">
              <bdi>{doc.currentVersion ?? '—'}</bdi>
            </TableCell>
            <TableCell>
              {doc.ownerUnit ? pickText(language, doc.ownerUnit.nameAr, doc.ownerUnit.nameEn) : '—'}
            </TableCell>
            <TableCell className="whitespace-nowrap">{date(doc.effectiveDate)}</TableCell>
            <TableCell className="whitespace-nowrap">
              {doc.expiryDate ? date(doc.expiryDate) : t('common.untilSuperseded')}
            </TableCell>
            <TableCell>
              <DocStatusChip status={doc.status} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function FacetList({
  title,
  facets,
  label,
  selected,
  onSelect,
}: {
  title: string;
  facets: Facet[];
  label: (code: string) => string;
  selected: string | undefined;
  onSelect: (code: string | undefined) => void;
}) {
  return (
    <FacetSection
      title={title}
      rows={facets.filter((f) => f.code).map((f) => ({ key: f.code, label: label(f.code), count: f.count }))}
      selected={selected}
      onSelect={onSelect}
    />
  );
}

function UnitFacets({
  units,
  selected,
  onSelect,
}: {
  units: UnitFacet[];
  selected: string | undefined;
  onSelect: (id: string | undefined) => void;
}) {
  const { t, i18n } = useTranslation('docreg');
  return (
    <FacetSection
      title={t('library.units')}
      rows={units.map((u) => ({
        key: u.id,
        label: pickText(i18n.language, u.nameAr, u.nameEn),
        count: u.count,
      }))}
      selected={selected}
      onSelect={onSelect}
    />
  );
}

function FacetSection({
  title,
  rows,
  selected,
  onSelect,
}: {
  title: string;
  rows: { key: string; label: string; count: number }[];
  selected: string | undefined;
  onSelect: (key: string | undefined) => void;
}) {
  const { t } = useTranslation('docreg');
  const num = useNumber();
  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-card p-4" aria-label={title}>
      <h2 className="text-sm font-semibold">{title}</h2>
      {rows.length === 0 ? (
        <p className="text-xs text-muted-foreground">{t('common.noData')}</p>
      ) : (
        <ul className="flex flex-col gap-0.5">
          <li>
            <FacetButton active={!selected} onClick={() => onSelect(undefined)} label={t('common.all')} />
          </li>
          {rows
            .sort((a, b) => b.count - a.count)
            .map((row) => (
              <li key={row.key}>
                <FacetButton
                  active={selected === row.key}
                  onClick={() => onSelect(selected === row.key ? undefined : row.key)}
                  label={row.label}
                  count={num(row.count)}
                />
              </li>
            ))}
        </ul>
      )}
    </section>
  );
}

function FacetButton({
  active,
  onClick,
  label,
  count,
}: {
  active: boolean;
  onClick: () => void;
  label: string;
  count?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        'flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-start text-sm hover:bg-muted',
        active && 'bg-muted font-semibold text-primary',
      )}
    >
      <span className="truncate">{label}</span>
      {count && <span className="tabular-nums text-xs text-muted-foreground">{count}</span>}
    </button>
  );
}

/** "Ask the documents" (AI Q&A over published documents the user may see); hidden when AI is off or not permitted. */
function AskBox() {
  const { t } = useTranslation('docreg');
  const { t: tc, i18n } = useTranslation();
  const enabled = useDocRegAi('docreg-qa');
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState<Answer>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [hidden, setHidden] = useState(false);
  if (!enabled || hidden) return null;

  const submit = (event: SyntheticEvent) => {
    event.preventDefault();
    if (!question.trim()) return;
    setBusy(true);
    setError(undefined);
    docregApi
      .ask(question.trim())
      .then(setAnswer)
      .catch((e: unknown) => {
        if (isAiUnavailable(e)) setHidden(true);
        else setError(errorMessage(tc, e));
      })
      .finally(() => setBusy(false));
  };

  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="docreg-ask">
      <header className="flex flex-col gap-1">
        <h2 className="flex items-center gap-2 font-semibold">
          <Sparkles className="size-4 text-info" aria-hidden />
          {t('ask.title')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('ask.hint')}</p>
      </header>
      <form className="flex flex-col gap-2 sm:flex-row" onSubmit={submit}>
        <Input
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder={t('ask.placeholder')}
          aria-label={t('ask.title')}
          maxLength={500}
          data-testid="docreg-ask-input"
        />
        <Button type="submit" loading={busy} disabled={!question.trim()}>
          {t('ask.action')}
        </Button>
      </form>
      {error && <StatusBanner tone="danger" title={error} />}
      {answer && (
        <div
          className="flex flex-col gap-2 rounded-md border border-info p-3"
          data-testid="docreg-ask-answer"
        >
          <AiBadge label={tc('status.aiGenerated')} className="w-fit" />
          <p dir="auto" className="whitespace-pre-line text-sm">
            {answer.answer}
          </p>
          {answer.sources.length > 0 && (
            <div className="flex flex-col gap-1">
              <h3 className="text-xs font-medium text-muted-foreground">{t('ask.sources')}</h3>
              <ul className="flex flex-wrap gap-2 text-sm">
                {answer.sources.map((source) => (
                  <li key={source.documentId}>
                    <Link
                      to="/documents/$documentId"
                      params={{ documentId: source.documentId }}
                      search={{ tab: 'overview' }}
                      className="text-primary hover:underline"
                    >
                      {source.documentNo && <bdi>{source.documentNo}</bdi>}
                      {source.documentNo && ' · '}
                      {documentName(i18n.language, source, t('document.untitled'))}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
