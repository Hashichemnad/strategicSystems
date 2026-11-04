import { getRouteApi, Link } from '@tanstack/react-router';
import { ListTodo, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import {
  EmptyState,
  Input,
  PageHeader,
  Pagination,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { actionViews, useCapActions } from './api';
import {
  ActionStatusChip,
  CapPageLayout,
  PctBar,
  ValidationChip,
  useCapT,
  useDate,
  useNames,
} from './components';

const route = getRouteApi('/app/cap/my-actions');

/** Actions: my actions (to update monthly), to validate (PM Office), late, budget-dependent, pending completion… */
export function CapActionsPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const names = useNames();
  const date = useDate();
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
  const view = search.view ?? 'mine';
  const actions = useCapActions({
    page: search.page ?? 1,
    pageSize: 25,
    view,
    ...(search.search ? { search: search.search } : {}),
  });

  return (
    <CapPageLayout testId="cap-actions">
      <PageHeader title={t('actions.listTitle')} description={t('actions.listDescription')} />
      <div role="tablist" aria-label={t('actions.views')} className="flex flex-wrap gap-1">
        {actionViews.map((v) => (
          <button
            key={v}
            type="button"
            role="tab"
            aria-selected={view === v}
            data-testid={`cap-action-view-${v}`}
            onClick={() =>
              void navigate({
                search: (p) => ({ ...p, view: v === 'mine' ? undefined : v, page: undefined }),
              })
            }
            className={cn(
              'rounded-full border px-3 py-1 text-sm',
              view === v ? 'border-primary bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
          >
            {k(`actionView.${v}`)}
          </button>
        ))}
      </div>
      <div className="relative max-w-md">
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
      {actions.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, actions.error)} />
      ) : !actions.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : actions.data.items.length === 0 ? (
        <EmptyState icon={ListTodo} title={t('actions.listEmpty')} />
      ) : (
        <>
          <Table data-testid="cap-action-list">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.details')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.remark')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.assignee')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.plannedEnd')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.validation')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.achieved')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {actions.data.items.map((a) => (
                <TableRow key={a.id} data-testid="cap-action-list-row">
                  <TableCell>
                    <Link
                      to="/cap/actions/$actionId"
                      params={{ actionId: a.id }}
                      className="whitespace-nowrap font-medium text-primary hover:underline"
                    >
                      <bdi>{a.number}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell className="max-w-sm">
                    <span className="line-clamp-2">{names.text(a.detailsAr, a.detailsEn)}</span>
                    {a.budgetRequired && (
                      <StatusChip tone={a.budgetAvailable ? 'neutral' : 'warning'}>
                        {a.programRequestNumber
                          ? `${t('budget.programRequest')}: ${a.programRequestNumber}`
                          : a.budgetAvailable
                            ? t('budget.availableYes')
                            : t('budget.availableNo')}
                      </StatusChip>
                    )}
                  </TableCell>
                  <TableCell>
                    <Link
                      to="/cap/remarks/$remarkId"
                      params={{ remarkId: a.homeRemarkId }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{a.homeRemarkNumber}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell>{names.person(a.assignee)}</TableCell>
                  <TableCell className={a.status === 'late' ? 'font-medium text-danger' : undefined}>
                    {date(a.effectiveEnd)}
                  </TableCell>
                  <TableCell>
                    <ValidationChip status={a.validationStatus} />
                    {a.pendingProcess && (
                      <span className="block text-xs text-muted-foreground">
                        {k(`process.${a.pendingProcess}`)}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    {a.validationStatus === 'approved' ? <ActionStatusChip status={a.status} /> : '—'}
                  </TableCell>
                  <TableCell>
                    <PctBar value={a.achievedPct} late={a.status === 'late'} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination
            page={actions.data.page}
            totalPages={actions.data.totalPages ?? 1}
            totalCount={actions.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </CapPageLayout>
  );
}
