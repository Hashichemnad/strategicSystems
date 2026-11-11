import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import {
  Button,
  EmptyState,
  Input,
  ListViewToggle,
  PageHeader,
  Select,
  Skeleton,
  SplitCard,
  SplitPreview,
  SplitView,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  ProgressBar,
  useListView,
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
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('auditmgmt.findings');
  const [selectedId, setSelectedId] = useState<string>();
  // The first finding is previewed until the user picks another.
  const selected = findings.data?.items.find((f) => f.id === selectedId) ?? findings.data?.items[0];

  return (
    <AuditPageLayout testId="audit-findings">
      <PageHeader
        title={t('findings.title')}
        description={t('findings.description')}
        actions={listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
      />
      <div className="flex flex-wrap gap-1" role="group" aria-label={t('findings.views')}>
        {findingViews.map((v) => (
          <Button
            key={v}
            size="sm"
            variant={(search.view ?? 'open') === v ? 'primary' : 'ghost'}
            aria-pressed={(search.view ?? 'open') === v}
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
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="audit-finding-cards">
                  {findings.data.items.map((f) => (
                    <FindingCard
                      key={f.id}
                      finding={f}
                      selected={f.id === selected?.id}
                      onPreview={() => setSelectedId(f.id)}
                    />
                  ))}
                </ul>
              }
              preview={selected && <FindingPreview finding={selected} />}
            />
          ) : (
            <FindingsTable items={findings.data.items} />
          )}
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
              <ProgressBar
                value={f.completion}
                tone={f.status === 'late' ? 'danger' : 'primary'}
                testId="audit-pct"
              />
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

function FindingLink({
  finding,
  className,
  children,
}: {
  finding: AuditFindingListItem;
  className?: string;
  children: ReactNode;
}) {
  return (
    <Link to="/audit-management/findings/$findingId" params={{ findingId: finding.id }} className={className}>
      {children}
    </Link>
  );
}

function FindingCard({
  finding: f,
  selected,
  onPreview,
}: {
  finding: AuditFindingListItem;
  selected: boolean;
  onPreview: () => void;
}) {
  const names = useNames();
  const date = useDate();
  return (
    <SplitCard
      selected={selected}
      onPreview={onPreview}
      title={
        <FindingLink finding={f} className="min-w-0 font-medium hover:underline">
          <bdi className="block text-xs text-muted-foreground">{f.number}</bdi>
          <span dir="auto" className="block">
            {names.text(f.titleAr, f.titleEn)}
          </span>
        </FindingLink>
      }
      chips={
        <>
          <FindingStatusChip status={f.status} />
          <PriorityChip priority={f.priority} />
          <span className="text-xs text-muted-foreground">{date(f.nextDueOn)}</span>
          <span className="w-full">
            <ProgressBar value={f.completion} tone={f.status === 'late' ? 'danger' : 'primary'} />
          </span>
        </>
      }
    />
  );
}

function FindingPreview({ finding: f }: { finding: AuditFindingListItem }) {
  const { t } = useTranslation('auditmgmt');
  const { t: ta } = useTranslation('appearance');
  const names = useNames();
  const date = useDate();
  return (
    <SplitPreview
      title={names.text(f.titleAr, f.titleEn)}
      rows={[
        [t('fields.number'), <bdi key="n">{f.number}</bdi>],
        [t('fields.report'), <bdi key="r">{f.reportNumber}</bdi>],
        [t('fields.responsibleUnit'), names.unit(f.responsibleUnit)],
        [t('fields.priority'), <PriorityChip key="p" priority={f.priority} />],
        [t('fields.status'), <FindingStatusChip key="s" status={f.status} />],
        [
          t('fields.completion'),
          <ProgressBar key="c" value={f.completion} tone={f.status === 'late' ? 'danger' : 'primary'} />,
        ],
        [t('fields.nextDue'), date(f.nextDueOn)],
        [t('fields.officeComment'), f.lastComment ?? '—'],
      ]}
      open={<FindingLink finding={f}>{ta('split.open')}</FindingLink>}
    />
  );
}
