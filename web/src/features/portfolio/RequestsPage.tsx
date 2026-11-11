import { getRouteApi, Link } from '@tanstack/react-router';
import { BriefcaseBusiness, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatNumber, pickText } from '@/lib/format';
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
  Tabs,
  TabsList,
  TabsTrigger,
  useListView,
} from '@/ui';
import { bandTone, requestStatuses, statusTone, usePortfolioOptions, useRequests } from './api';

const route = getRouteApi('/app/portfolio/requests');
const PAGE_SIZE = 25;
export const requestViews = ['all', 'mine', 'in-approval', 'returned'] as const;

/** Program requests list (SPEC §1.8): views, filters by status, classification and priority, search. State in the URL. */
export function RequestsPage() {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('appearance');
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const canCreate = useCan('portfolio.request.create');
  const options = usePortfolioOptions();
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    if (text === (search.search ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (p) => ({ ...p, search: text || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [text, search.search, navigate]);
  const requests = useRequests({
    view: search.view,
    page: search.page,
    pageSize: PAGE_SIZE,
    ...(search.status ? { status: search.status } : {}),
    ...(search.classification ? { classification: search.classification } : {}),
    ...(search.band ? { priorityBand: search.band } : {}),
    ...(search.search ? { search: search.search } : {}),
  });
  const any = { value: '*', label: t('filters.any') };
  const bandLabel = (code: string | null | undefined) => {
    const band = options.data?.bands.find((b) => b.code === code);
    return band ? pickText(i18n.language, band.labelAr, band.labelEn) : code;
  };
  const filtered = Boolean(search.status || search.classification || search.band || search.search);
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('portfolio.requests');
  const [selectedId, setSelectedId] = useState<string>();
  // The first row is previewed until the user picks another.
  const selected = requests.data?.items.find((r) => r.id === selectedId) ?? requests.data?.items[0];
  const statusLabel = (r: RequestListItem) =>
    r.status === 'in-approval' && (r.approvalStepNameAr || r.approvalStepNameEn)
      ? pickText(i18n.language, r.approvalStepNameAr, r.approvalStepNameEn)
      : r.approvalStatus === 'returned' && r.status === 'draft'
        ? t('approvalStatus.returned')
        : t(`status.${r.status as 'draft'}`);
  const budget = (r: RequestListItem) =>
    r.requiresBudget && r.estimatedBudget !== null
      ? `${formatNumber(r.estimatedBudget, i18n.language)} ${options.data?.currencyCode ?? ''}`
      : '—';

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        actions={
          (canCreate || listView.available) && (
            <>
              {listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
              {canCreate && (
                <Button asChild>
                  <Link to="/portfolio/requests/new" search={{}}>
                    <Plus aria-hidden />
                    {t('newRequest')}
                  </Link>
                </Button>
              )}
            </>
          )
        }
      />
      <Tabs
        value={search.view}
        onValueChange={(view) => void navigate({ search: (p) => ({ ...p, view: view as 'all', page: 1 }) })}
      >
        <TabsList>
          {requestViews.map((view) => (
            <TabsTrigger filter key={view} value={view}>
              {t(`views.${view}`)}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      <div className="grid gap-3 md:grid-cols-4">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder={t('filters.search')}
            aria-label={t('filters.search')}
          />
        </div>
        <Select
          aria-label={t('filters.status')}
          value={search.status ?? '*'}
          onValueChange={(value) =>
            void navigate({ search: (p) => ({ ...p, status: value === '*' ? undefined : value, page: 1 }) })
          }
          options={[any, ...requestStatuses.map((s) => ({ value: s, label: t(`status.${s}`) }))]}
        />
        <Select
          aria-label={t('filters.classification')}
          value={search.classification ?? '*'}
          onValueChange={(value) =>
            void navigate({
              search: (p) => ({ ...p, classification: value === '*' ? undefined : value, page: 1 }),
            })
          }
          options={[
            any,
            { value: 'project', label: t('classification.project') },
            { value: 'initiative', label: t('classification.initiative') },
          ]}
        />
        <Select
          aria-label={t('filters.band')}
          value={search.band ?? '*'}
          onValueChange={(value) =>
            void navigate({ search: (p) => ({ ...p, band: value === '*' ? undefined : value, page: 1 }) })
          }
          options={[
            any,
            ...(options.data?.bands ?? []).map((b) => ({
              value: b.code,
              label: pickText(i18n.language, b.labelAr, b.labelEn),
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
          icon={BriefcaseBusiness}
          title={filtered ? t('list.emptyFiltered') : t('list.empty')}
          action={
            canCreate && !filtered ? (
              <Button asChild>
                <Link to="/portfolio/requests/new" search={{}}>
                  {t('newRequest')}
                </Link>
              </Button>
            ) : undefined
          }
        />
      ) : (
        <>
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="requests-cards">
                  {requests.data.items.map((r) => (
                    <SplitCard
                      key={r.id}
                      selected={r.id === selected?.id}
                      onPreview={() => setSelectedId(r.id)}
                      title={
                        <Link
                          to="/portfolio/requests/$requestId"
                          params={{ requestId: r.id }}
                          search={{ tab: 'overview' }}
                          className="min-w-0 font-medium hover:underline"
                        >
                          <bdi className="block text-xs text-muted-foreground">{r.number}</bdi>
                          <span dir="auto" className="block">
                            {pickText(i18n.language, r.nameAr, r.nameEn)}
                          </span>
                        </Link>
                      }
                      chips={
                        <>
                          <StatusChip tone={statusTone[r.status] ?? 'neutral'}>{statusLabel(r)}</StatusChip>
                          <StatusChip tone="info">
                            {t(`classification.${r.effectiveClassification as 'project'}`)}
                          </StatusChip>
                          {r.priorityBand && (
                            <StatusChip tone={bandTone[r.priorityBand] ?? 'neutral'}>
                              {bandLabel(r.priorityBand)}
                            </StatusChip>
                          )}
                        </>
                      }
                      progress={
                        r.evaluationScore !== null
                          ? {
                              value: r.evaluationScore,
                              label: `${t('fields.priorityBand')} · ${formatNumber(Math.round(r.evaluationScore * 100), i18n.language)} %`,
                            }
                          : undefined
                      }
                    />
                  ))}
                </ul>
              }
              preview={
                selected && (
                  <SplitPreview
                    title={pickText(i18n.language, selected.nameAr, selected.nameEn)}
                    rows={[
                      [t('fields.number'), <bdi key="n">{selected.number}</bdi>],
                      [t('fields.status'), statusLabel(selected)],
                      [
                        t('fields.classification'),
                        t(`classification.${selected.effectiveClassification as 'project'}`),
                      ],
                      [
                        t('fields.priorityBand'),
                        selected.priorityBand ? bandLabel(selected.priorityBand) : '—',
                      ],
                      [t('fields.estimatedBudget'), budget(selected)],
                      [
                        t('fields.unit'),
                        selected.unit
                          ? pickText(i18n.language, selected.unit.nameAr, selected.unit.nameEn)
                          : '—',
                      ],
                      [t('fields.updatedAt'), formatDate(selected.updatedAt, i18n.language)],
                    ]}
                    open={
                      <Link
                        to="/portfolio/requests/$requestId"
                        params={{ requestId: selected.id }}
                        search={{ tab: 'overview' }}
                      >
                        {ta('split.open')}
                      </Link>
                    }
                  />
                )
              }
            />
          ) : (
            <div className="overflow-x-auto rounded-lg border bg-card">
              <Table aria-busy={requests.isFetching} data-testid="requests-table">
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.classification')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.priorityBand')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.estimatedBudget')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.updatedAt')}</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {requests.data.items.map((r) => (
                    <TableRow key={r.id}>
                      <TableCell>
                        <Link
                          to="/portfolio/requests/$requestId"
                          params={{ requestId: r.id }}
                          search={{ tab: 'overview' }}
                          className="font-medium text-primary hover:underline"
                        >
                          <bdi>{r.number}</bdi>
                        </Link>
                      </TableCell>
                      <TableCell>
                        <span dir="auto">{pickText(i18n.language, r.nameAr, r.nameEn)}</span>
                        {r.projectNumber && (
                          <span className="block text-xs text-muted-foreground">
                            <bdi>{r.projectNumber}</bdi>
                          </span>
                        )}
                      </TableCell>
                      <TableCell>
                        <StatusChip tone={statusTone[r.status] ?? 'neutral'}>
                          {r.status === 'in-approval' && (r.approvalStepNameAr || r.approvalStepNameEn)
                            ? pickText(i18n.language, r.approvalStepNameAr, r.approvalStepNameEn)
                            : r.approvalStatus === 'returned' && r.status === 'draft'
                              ? t('approvalStatus.returned')
                              : t(`status.${r.status as 'draft'}`)}
                        </StatusChip>
                      </TableCell>
                      <TableCell>{t(`classification.${r.effectiveClassification as 'project'}`)}</TableCell>
                      <TableCell>
                        {r.priorityBand ? (
                          <StatusChip tone={bandTone[r.priorityBand] ?? 'neutral'}>
                            {bandLabel(r.priorityBand)}
                            {r.evaluationScore !== null &&
                              ` · ${formatNumber(Math.round(r.evaluationScore * 100), i18n.language)} %`}
                          </StatusChip>
                        ) : (
                          '—'
                        )}
                      </TableCell>
                      <TableCell className="tabular-nums">
                        {r.requiresBudget && r.estimatedBudget !== null
                          ? `${formatNumber(r.estimatedBudget, i18n.language)} ${options.data?.currencyCode ?? ''}`
                          : '—'}
                      </TableCell>
                      <TableCell>
                        {r.unit ? pickText(i18n.language, r.unit.nameAr, r.unit.nameEn) : '—'}
                      </TableCell>
                      <TableCell>{formatDate(r.updatedAt, i18n.language)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <Pagination
            page={search.page}
            totalPages={Math.max(1, Math.ceil(requests.data.totalCount / PAGE_SIZE))}
            totalCount={requests.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </div>
  );
}

type RequestListItem = NonNullable<ReturnType<typeof useRequests>['data']>['items'][number];
