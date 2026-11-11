import { Link } from '@tanstack/react-router';
import { FileText, Plus, Search } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  Input,
  ListViewToggle,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SplitCard,
  SplitPreview,
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
import { requestStatuses, useCatalogue, useMyRequests, type RequestListItem } from './api';
import { RequestStatusChip, useDynamicT } from './components';

const PAGE_SIZE = 25;
const ANY = '*';

export function useDebounced(value: string, delay = 300) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value.trim()), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** My Requests (docs/design/requests.md §2.7): the user's own requests, returned ones first (the server orders them). */
export function MyRequestsPage() {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const dt = useDynamicT();
  const [status, setStatus] = useState(ANY);
  const [type, setType] = useState(ANY);
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const search = useDebounced(text);
  const catalogue = useCatalogue();
  const requests = useMyRequests({
    page,
    pageSize: PAGE_SIZE,
    ...(status !== ANY ? { status } : {}),
    ...(type !== ANY ? { type } : {}),
    ...(search ? { search } : {}),
  });
  const { i18n } = useTranslation();
  const filtered = status !== ANY || type !== ANY || Boolean(search);
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('requests.mine');
  const [selectedId, setSelectedId] = useState<string>();
  // The first request is previewed until the user picks another.
  const selected = requests.data?.items.find((r) => r.id === selectedId) ?? requests.data?.items[0];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('mine.title')}
        description={t('mine.description')}
        actions={
          <>
            {listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
            <Button asChild>
              <Link to="/requests">
                <Plus aria-hidden />
                {t('mine.newRequest')}
              </Link>
            </Button>
          </>
        }
      />
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            value={text}
            onChange={(e) => {
              setText(e.target.value);
              setPage(1);
            }}
            placeholder={t('list.search')}
            aria-label={t('list.search')}
          />
        </div>
        <Select
          aria-label={t('list.filterStatus')}
          value={status}
          onValueChange={(value) => {
            setStatus(value);
            setPage(1);
          }}
          options={[
            { value: ANY, label: t('list.anyStatus') },
            ...requestStatuses.map((s) => ({ value: s, label: dt(`status.${s}`, s) })),
          ]}
        />
        <Select
          aria-label={t('list.filterType')}
          value={type}
          onValueChange={(value) => {
            setType(value);
            setPage(1);
          }}
          options={[
            { value: ANY, label: t('list.anyType') },
            ...(catalogue.data?.types ?? []).map((ty) => ({
              value: ty.code,
              label: pickText(i18n.language, ty.nameAr, ty.nameEn) || ty.code,
            })),
          ]}
        />
      </div>
      {requests.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : requests.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, requests.error)} />
      ) : requests.data.items.length === 0 ? (
        <EmptyState
          icon={FileText}
          title={filtered ? t('list.emptyFiltered') : t('mine.empty')}
          action={
            !filtered && (
              <Button asChild>
                <Link to="/requests">{t('mine.newRequest')}</Link>
              </Button>
            )
          }
        />
      ) : (
        <>
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="my-requests-cards">
                  {requests.data.items.map((r) => (
                    <SplitCard
                      key={r.id}
                      selected={r.id === selected?.id}
                      onPreview={() => setSelectedId(r.id)}
                      title={
                        <RequestLink request={r} className="min-w-0 font-medium hover:underline">
                          <bdi className="block text-xs text-muted-foreground">{r.number}</bdi>
                          <span dir="auto" className="block">
                            {pickText(i18n.language, r.titleAr, r.titleEn) ||
                              pickText(i18n.language, r.typeNameAr, r.typeNameEn) ||
                              r.typeCode}
                          </span>
                        </RequestLink>
                      }
                      chips={
                        <>
                          <RequestStatusChip status={r.status} />
                          <span className="text-xs text-muted-foreground">
                            {pickText(i18n.language, r.stageNameAr, r.stageNameEn)}
                          </span>
                          <DueCell item={r} />
                        </>
                      }
                    />
                  ))}
                </ul>
              }
              preview={selected && <RequestPreview request={selected} />}
            />
          ) : (
            <RequestTable items={requests.data.items} busy={requests.isFetching} />
          )}
          <Pagination
            page={page}
            totalPages={Math.max(1, Math.ceil(requests.data.totalCount / PAGE_SIZE))}
            totalCount={requests.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </div>
  );
}

/** Due date of the current stage with an overdue chip (SLA in working time, computed by the server). */
export function DueCell({ item }: { item: Pick<RequestListItem, 'stageDueAt' | 'overdue'> }) {
  const { t, i18n } = useTranslation('requests');
  if (!item.stageDueAt) return <span className="text-muted-foreground">—</span>;
  return (
    <span className="flex flex-wrap items-center gap-1">
      <time dateTime={item.stageDueAt}>{formatDateTime(item.stageDueAt, i18n.language)}</time>
      {item.overdue && <StatusChip tone="danger">{t('list.overdue')}</StatusChip>}
    </span>
  );
}

function RequestTable({ items, busy }: { items: RequestListItem[]; busy: boolean }) {
  const { t, i18n } = useTranslation('requests');
  const lang = i18n.language;
  return (
    <div className="overflow-x-auto rounded-lg border bg-card">
      <Table aria-busy={busy} data-testid="my-requests-table">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('list.number')}</TableHeaderCell>
            <TableHeaderCell>{t('list.type')}</TableHeaderCell>
            <TableHeaderCell>{t('list.title')}</TableHeaderCell>
            <TableHeaderCell>{t('list.status')}</TableHeaderCell>
            <TableHeaderCell>{t('list.stage')}</TableHeaderCell>
            <TableHeaderCell>{t('list.submitted')}</TableHeaderCell>
            <TableHeaderCell>{t('list.due')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((r) => (
            <TableRow key={r.id}>
              <TableCell>
                <Link
                  to="/requests/$requestId"
                  params={{ requestId: r.id }}
                  search={{ tab: 'overview' }}
                  className="font-medium text-primary hover:underline"
                >
                  <bdi>{r.number}</bdi>
                </Link>
              </TableCell>
              <TableCell>{pickText(lang, r.typeNameAr, r.typeNameEn) || r.typeCode}</TableCell>
              <TableCell>
                <span dir="auto">{pickText(lang, r.titleAr, r.titleEn) || '—'}</span>
              </TableCell>
              <TableCell>
                <RequestStatusChip status={r.status} />
              </TableCell>
              <TableCell>{pickText(lang, r.stageNameAr, r.stageNameEn) || '—'}</TableCell>
              <TableCell>{r.submittedAt ? formatDate(r.submittedAt, lang) : '—'}</TableCell>
              <TableCell>
                <DueCell item={r} />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function RequestLink({
  request,
  className,
  children,
}: {
  request: RequestListItem;
  className?: string;
  children: ReactNode;
}) {
  return (
    <Link
      to="/requests/$requestId"
      params={{ requestId: request.id }}
      search={{ tab: 'overview' }}
      className={className}
    >
      {children}
    </Link>
  );
}

/** The split view's preview of the selected request (list fields only; the record page has everything). */
function RequestPreview({ request: r }: { request: RequestListItem }) {
  const { t, i18n } = useTranslation('requests');
  const { t: ta } = useTranslation('appearance');
  const lang = i18n.language;
  return (
    <SplitPreview
      title={pickText(lang, r.titleAr, r.titleEn) || pickText(lang, r.typeNameAr, r.typeNameEn) || r.typeCode}
      rows={[
        [t('list.number'), <bdi key="n">{r.number}</bdi>],
        [t('list.type'), pickText(lang, r.typeNameAr, r.typeNameEn) || r.typeCode],
        [t('list.status'), <RequestStatusChip key="s" status={r.status} />],
        [t('list.stage'), pickText(lang, r.stageNameAr, r.stageNameEn) || '—'],
        [t('list.submitted'), r.submittedAt ? formatDate(r.submittedAt, lang) : '—'],
        [t('list.due'), <DueCell key="d" item={r} />],
      ]}
      open={<RequestLink request={r}>{ta('split.open')}</RequestLink>}
    />
  );
}
