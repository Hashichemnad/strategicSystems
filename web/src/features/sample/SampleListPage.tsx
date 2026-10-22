import { getRouteApi } from '@tanstack/react-router';
import { Boxes, Pencil, Plus, Power, PowerOff, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  EmptyState,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SortableHeader,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
  type SortDirection,
} from '@/ui';
import { useChangeSampleItemStatus, useSampleItems, type SampleItem } from './api';
import { SampleItemDialog } from './SampleItemDialog';

const route = getRouteApi('/app/sample');
const PAGE_SIZE = 10;

export function SampleListPage() {
  const { t, i18n } = useTranslation('sample');
  const { t: tc } = useTranslation();
  const { t: te } = useTranslation('errors');
  const toast = useToast();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const [searchText, setSearchText] = useState(search.search ?? '');
  const [editing, setEditing] = useState<SampleItem | undefined>();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [confirming, setConfirming] = useState<SampleItem | undefined>();
  const changeStatus = useChangeSampleItemStatus();

  const query = useSampleItems({
    page: search.page,
    pageSize: PAGE_SIZE,
    sort: search.sort,
    ...(search.search ? { search: search.search } : {}),
    ...(search.status ? { status: search.status } : {}),
  });

  // Debounce typing into the URL (so the view is shareable) and reset to page 1.
  useEffect(() => {
    if (searchText === (search.search ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (prev) => ({ ...prev, search: searchText || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [searchText, search.search, navigate]);

  const sortDirection = (field: string): SortDirection =>
    search.sort === field ? 'asc' : search.sort === `-${field}` ? 'desc' : null;
  const toggleSort = (field: string) =>
    void navigate({
      search: (prev) => ({ ...prev, sort: prev.sort === field ? `-${field}` : field, page: 1 }),
    });
  const nameField = i18n.language === 'ar' ? 'nameAr' : 'nameEn';

  async function runStatusChange(item: SampleItem, action: 'activate' | 'deactivate') {
    try {
      await changeStatus.mutateAsync({ item, action });
      toast({ title: t(action === 'activate' ? 'toast.activated' : 'toast.deactivated') });
      setConfirming(undefined);
    } catch (failure) {
      toast({ tone: 'error', title: te('title'), description: errorMessage(tc, failure) });
    }
  }

  const data = query.data;
  const filtered = Boolean(search.search || search.status);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        actions={
          <Button
            onClick={() => {
              setEditing(undefined);
              setDialogOpen(true);
            }}
          >
            <Plus aria-hidden />
            {t('new')}
          </Button>
        }
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            type="search"
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder={t('searchPlaceholder')}
            aria-label={t('searchPlaceholder')}
            className="ps-9"
          />
        </div>
        <Select
          className="sm:w-48"
          aria-label={t('fields.status')}
          value={search.status ?? 'all'}
          onValueChange={(value) =>
            void navigate({
              search: (prev) => ({
                ...prev,
                status: value === 'all' ? undefined : (value as 'active' | 'inactive'),
                page: 1,
              }),
            })
          }
          options={[
            { value: 'all', label: t('status.all') },
            { value: 'active', label: t('status.active') },
            { value: 'inactive', label: t('status.inactive') },
          ]}
        />
      </div>

      {query.isError ? (
        <StatusBanner
          tone="danger"
          title={te('title')}
          description={errorMessage(tc, query.error)}
          actions={
            <Button variant="outline" onClick={() => void query.refetch()}>
              {tc('actions.retry')}
            </Button>
          }
        />
      ) : !data ? (
        <div className="flex flex-col gap-2" aria-busy="true" aria-label={tc('status.loading')}>
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : data.items.length === 0 ? (
        <EmptyState
          icon={Boxes}
          title={t(filtered ? 'empty.filteredTitle' : 'empty.title')}
          description={t(filtered ? 'empty.filteredDescription' : 'empty.description')}
        />
      ) : (
        <div className="flex flex-col gap-3">
          <Table aria-busy={query.isFetching}>
            <TableHead>
              <tr>
                <SortableHeader direction={sortDirection(nameField)} onSort={() => toggleSort(nameField)}>
                  {t('fields.name')}
                </SortableHeader>
                <SortableHeader direction={sortDirection('status')} onSort={() => toggleSort('status')}>
                  {t('fields.status')}
                </SortableHeader>
                <SortableHeader direction={sortDirection('updatedAt')} onSort={() => toggleSort('updatedAt')}>
                  {t('fields.updatedAt')}
                </SortableHeader>
                <TableHeaderCell className="text-end">{t('fields.actions')}</TableHeaderCell>
              </tr>
            </TableHead>
            <TableBody>
              {data.items.map((item) => {
                const name = pickText(i18n.language, item.nameAr, item.nameEn);
                const other = i18n.language === 'ar' ? item.nameEn : item.nameAr;
                return (
                  <TableRow key={item.id} data-testid="sample-row">
                    <TableCell>
                      <div className="font-medium">{name}</div>
                      {other && other !== name && (
                        <div
                          className="text-xs text-muted-foreground"
                          lang={i18n.language === 'ar' ? 'en' : 'ar'}
                        >
                          {other}
                        </div>
                      )}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={item.status === 'active' ? 'success' : 'neutral'}>
                        {t(item.status === 'active' ? 'status.active' : 'status.inactive')}
                      </StatusChip>
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-muted-foreground">
                      {formatDateTime(item.updatedAt, i18n.language)}
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        {item.status === 'active' ? (
                          <>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => {
                                setEditing(item);
                                setDialogOpen(true);
                              }}
                            >
                              <Pencil aria-hidden />
                              {tc('actions.edit')}
                            </Button>
                            <Button variant="ghost" size="sm" onClick={() => setConfirming(item)}>
                              <PowerOff aria-hidden />
                              {t('actions.deactivate')}
                            </Button>
                          </>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => void runStatusChange(item, 'activate')}
                          >
                            <Power aria-hidden />
                            {t('actions.activate')}
                          </Button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <Pagination
            page={data.page}
            totalPages={data.totalPages ?? Math.ceil(data.totalCount / data.pageSize)}
            totalCount={data.totalCount}
            onPageChange={(page) => void navigate({ search: (prev) => ({ ...prev, page }) })}
          />
        </div>
      )}

      <SampleItemDialog open={dialogOpen} onOpenChange={setDialogOpen} item={editing} />
      <ConfirmDialog
        open={confirming !== undefined}
        onOpenChange={(open) => !open && setConfirming(undefined)}
        title={t('confirmDeactivate.title')}
        description={t('confirmDeactivate.description', {
          name: confirming ? pickText(i18n.language, confirming.nameAr, confirming.nameEn) : '',
        })}
        confirmLabel={t('actions.deactivate')}
        destructive
        loading={changeStatus.isPending}
        onConfirm={() => confirming && void runStatusChange(confirming, 'deactivate')}
      />
    </div>
  );
}
