import { getRouteApi } from '@tanstack/react-router';
import { ListChecks, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { cn } from '@/lib/cn';
import { EmptyState, Input, PageHeader, Pagination, Select, Skeleton, StatusBanner } from '@/ui';
import { priorities, remarkStatuses, remarkViews, useCapRemarks } from './api';
import { CapPageLayout, lookupTypes, useCapLabels, useCapT } from './components';
import { RemarksTable } from './RemarksTable';

const route = getRouteApi('/app/cap/remarks');
const ALL = '__all';

/** Remarks register with saved views (mine, to accept, my unit, overdue, to confirm…) and filters, scoped on the server. */
export function CapRemarksPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const labels = useCapLabels();
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
  const remarks = useCapRemarks({
    page: search.page ?? 1,
    pageSize: 25,
    ...(view !== 'all' ? { view } : {}),
    ...(search.search ? { search: search.search } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.priority ? { priority: search.priority } : {}),
    ...(search.unitId ? { unitId: search.unitId } : {}),
    ...(search.sourceType ? { sourceType: search.sourceType } : {}),
  });
  const set = (patch: Record<string, unknown>) =>
    void navigate({ search: (p) => ({ ...p, ...patch, page: undefined }) });

  return (
    <CapPageLayout testId="cap-remarks">
      <PageHeader title={t('remarks.title')} description={t('remarks.description')} />
      <div
        role="tablist"
        aria-label={t('remarks.views')}
        className="flex flex-wrap gap-1"
        data-testid="cap-remark-views"
      >
        {remarkViews.map((v) => (
          <button
            key={v}
            type="button"
            role="tab"
            aria-selected={view === v}
            data-testid={`cap-view-${v}`}
            onClick={() => set({ view: v === 'all' ? undefined : v })}
            className={cn(
              'rounded-full border px-3 py-1 text-sm',
              view === v ? 'border-primary bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
          >
            {k(`remarkView.${v}`)}
          </button>
        ))}
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
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
            data-testid="cap-remark-search"
          />
        </div>
        <UnitSelect
          allowEmpty
          value={search.unitId}
          onChange={(unitId) => set({ unitId: unitId === '-' ? undefined : unitId })}
        />
        <Select
          aria-label={t('fields.status')}
          value={search.status ?? ALL}
          onValueChange={(v) => set({ status: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('common.allStatuses') },
            ...remarkStatuses.map((s) => ({ value: s, label: k(`remarkStatus.${s}`) })),
          ]}
        />
        <Select
          aria-label={t('fields.priority')}
          value={search.priority ?? ALL}
          onValueChange={(v) => set({ priority: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('common.allPriorities') },
            ...priorities.map((p) => ({ value: p, label: k(`priority.${p}`) })),
          ]}
        />
        <Select
          aria-label={t('fields.sourceType')}
          value={search.sourceType ?? ALL}
          onValueChange={(v) => set({ sourceType: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('common.allTypes') },
            ...labels.lookupOptions(lookupTypes.sourceType),
          ]}
        />
      </div>
      {remarks.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, remarks.error)} />
      ) : !remarks.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : remarks.data.items.length === 0 ? (
        <EmptyState icon={ListChecks} title={t('remarks.empty')} />
      ) : (
        <>
          <RemarksTable rows={remarks.data.items} />
          <Pagination
            page={remarks.data.page}
            totalPages={remarks.data.totalPages ?? 1}
            totalCount={remarks.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </CapPageLayout>
  );
}
