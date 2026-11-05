import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import {
  Button,
  EmptyState,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import {
  findingStatuses,
  findingViews,
  lookupTypes,
  useAuditFindings,
  type AuditFindingListItem,
} from './api';
import {
  AuditPageLayout,
  FindingStatusChip,
  PctBar,
  PriorityChip,
  useAuditLabels,
  useAuditT,
  useDate,
  useNames,
} from './components';

const route = getRouteApi('/app/audit-management/findings');

/** Findings register: saved views, filters (status, priority, department, year, report type). */
export function AuditFindingsPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const search = route.useSearch();
  const navigate = useNavigate();
  const labels = useAuditLabels();
  const [text, setText] = useState(search.search ?? '');
  const page = search.page ?? 1;
  const findings = useAuditFindings({
    page,
    pageSize: 25,
    view: search.view ?? 'open',
    ...(search.search ? { search: search.search } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.priority ? { priority: search.priority } : {}),
    ...(search.unitId ? { unitId: search.unitId } : {}),
    ...(search.year ? { year: search.year } : {}),
    ...(search.reportType ? { reportType: search.reportType } : {}),
  });
  const setSearch = (patch: Record<string, string | number | undefined>) =>
    void navigate({ to: '/audit-management/findings', search: { ...search, ...patch } });
  const year0 = new Date().getFullYear();

  return (
    <AuditPageLayout testId="audit-findings">
      <PageHeader title={t('findings.title')} description={t('findings.description')} />
      <div className="flex flex-wrap gap-1" role="tablist" aria-label={t('findings.views')}>
        {findingViews.map((v) => (
          <Button
            key={v}
            size="sm"
            variant={(search.view ?? 'open') === v ? 'primary' : 'ghost'}
            onClick={() => setSearch({ view: v, page: undefined })}
            data-testid={`audit-view-${v}`}
          >
            {k(`view.${v}`)}
          </Button>
        ))}
      </div>
      <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setSearch({ search: text || undefined, page: undefined });
          }}
        >
          <Input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={tc('actions.search')}
            aria-label={tc('actions.search')}
          />
        </form>
        <Select
          aria-label={t('fields.status')}
          value={search.status ?? 'all'}
          onValueChange={(v) => setSearch({ status: v === 'all' ? undefined : v, page: undefined })}
          options={[
            { value: 'all', label: t('common.allStatuses') },
            ...findingStatuses.map((s) => ({ value: s, label: k(`findingStatus.${s}`) })),
          ]}
        />
        <Select
          aria-label={t('fields.priority')}
          value={search.priority ?? 'all'}
          onValueChange={(v) => setSearch({ priority: v === 'all' ? undefined : v, page: undefined })}
          options={[
            { value: 'all', label: t('common.allPriorities') },
            ...labels.lookupOptions(lookupTypes.priority),
          ]}
        />
        <Select
          aria-label={t('fields.reportType')}
          value={search.reportType ?? 'all'}
          onValueChange={(v) => setSearch({ reportType: v === 'all' ? undefined : v, page: undefined })}
          options={[
            { value: 'all', label: t('common.allTypes') },
            ...labels.lookupOptions(lookupTypes.reportType),
          ]}
        />
        <Select
          aria-label={t('fields.year')}
          value={search.year ? String(search.year) : 'all'}
          onValueChange={(v) => setSearch({ year: v === 'all' ? undefined : Number(v), page: undefined })}
          options={[
            { value: 'all', label: t('common.allYears') },
            ...[year0, year0 - 1, year0 - 2, year0 - 3].map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
        <UnitSelect
          allowEmpty
          value={search.unitId}
          onChange={(v) => setSearch({ unitId: v && v !== '-' ? v : undefined, page: undefined })}
        />
      </div>
      {findings.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, findings.error)} />
      ) : !findings.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : findings.data.items.length === 0 ? (
        <EmptyState title={t('findings.empty')} />
      ) : (
        <>
          <FindingsTable items={findings.data.items} />
          <div className="flex items-center justify-end gap-2 text-sm">
            <Button
              size="sm"
              variant="outline"
              disabled={page <= 1}
              onClick={() => setSearch({ page: page - 1 })}
            >
              {t('common.previous')}
            </Button>
            <span>
              {t('common.page', { page, total: Math.max(1, Math.ceil(findings.data.totalCount / 25)) })}
            </span>
            <Button
              size="sm"
              variant="outline"
              disabled={page * 25 >= findings.data.totalCount}
              onClick={() => setSearch({ page: page + 1 })}
            >
              {t('common.next')}
            </Button>
          </div>
        </>
      )}
    </AuditPageLayout>
  );
}

export function FindingsTable({
  items,
  showComment = true,
}: {
  items: readonly AuditFindingListItem[];
  showComment?: boolean;
}) {
  const { t } = useTranslation('auditmgmt');
  const names = useNames();
  const date = useDate();
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.report')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.responsibleUnit')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.priority')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.completion')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.nextDue')}</TableHeaderCell>
          {showComment && <TableHeaderCell>{t('fields.officeComment')}</TableHeaderCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((f) => (
          <TableRow key={f.id} data-testid="audit-finding-row">
            <TableCell>
              <Link
                to="/audit-management/findings/$findingId"
                params={{ findingId: f.id }}
                className="text-primary hover:underline"
              >
                <bdi>{f.number}</bdi>
              </Link>
            </TableCell>
            <TableCell className="max-w-64">{names.text(f.titleAr, f.titleEn)}</TableCell>
            <TableCell>
              <bdi>{f.reportNumber}</bdi>
            </TableCell>
            <TableCell>{names.unit(f.responsibleUnit)}</TableCell>
            <TableCell>
              <PriorityChip priority={f.priority} />
            </TableCell>
            <TableCell>
              <FindingStatusChip status={f.status} />
            </TableCell>
            <TableCell>
              <PctBar value={f.completion} late={f.status === 'late'} />
            </TableCell>
            <TableCell>{date(f.nextDueOn)}</TableCell>
            {showComment && (
              <TableCell className="max-w-64 text-sm text-muted-foreground">{f.lastComment ?? '—'}</TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
