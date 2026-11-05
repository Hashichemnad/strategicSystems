import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
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
import { lookupTypes, reportStatuses, useAuditOptions, useAuditReports } from './api';
import {
  AuditPageLayout,
  ReportStatusChip,
  useAuditLabels,
  useAuditT,
  useDate,
  useNames,
} from './components';
import { ReportDialog } from './forms';

const route = getRouteApi('/app/audit-management/reports');

/** Audit reports (internal audit, external audit, special report, revenue audit, consultancy…). */
export function AuditReportsPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const search = route.useSearch();
  const navigate = useNavigate();
  const options = useAuditOptions();
  const labels = useAuditLabels();
  const names = useNames();
  const date = useDate();
  const [text, setText] = useState(search.search ?? '');
  const [creating, setCreating] = useState(false);
  const reports = useAuditReports({
    page: search.page ?? 1,
    pageSize: 25,
    ...(search.search ? { search: search.search } : {}),
    ...(search.type ? { type: search.type } : {}),
    ...(search.status ? { status: search.status } : {}),
  });
  const setSearch = (patch: Record<string, string | number | undefined>) =>
    void navigate({ to: '/audit-management/reports', search: { ...search, ...patch } });

  return (
    <AuditPageLayout testId="audit-reports">
      <PageHeader
        title={t('reports.title')}
        description={t('reports.description')}
        actions={
          options.data?.isOffice && (
            <Button onClick={() => setCreating(true)} data-testid="audit-report-new">
              <Plus aria-hidden />
              {t('reports.new')}
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-3">
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
          aria-label={t('fields.reportType')}
          value={search.type ?? 'all'}
          onValueChange={(v) => setSearch({ type: v === 'all' ? undefined : v, page: undefined })}
          options={[
            { value: 'all', label: t('common.allTypes') },
            ...labels.lookupOptions(lookupTypes.reportType),
          ]}
        />
        <Select
          aria-label={t('fields.status')}
          value={search.status ?? 'all'}
          onValueChange={(v) => setSearch({ status: v === 'all' ? undefined : v, page: undefined })}
          options={[
            { value: 'all', label: t('common.allStatuses') },
            ...reportStatuses.map((s) => ({ value: s, label: k(`reportStatus.${s}`) })),
          ]}
        />
      </div>
      {reports.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, reports.error)} />
      ) : !reports.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : reports.data.items.length === 0 ? (
        <EmptyState title={t('reports.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.reportType')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.auditorBody')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.reportDate')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('reports.findings')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {reports.data.items.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  <Link
                    to="/audit-management/reports/$reportId"
                    params={{ reportId: r.id }}
                    className="text-primary hover:underline"
                  >
                    <bdi>{r.number}</bdi>
                  </Link>
                </TableCell>
                <TableCell>{names.text(r.titleAr, r.titleEn)}</TableCell>
                <TableCell>{labels.lookup(lookupTypes.reportType, r.typeCode)}</TableCell>
                <TableCell>{r.auditorBody ?? '—'}</TableCell>
                <TableCell>{date(r.reportDate)}</TableCell>
                <TableCell>
                  <ReportStatusChip status={r.status} />
                </TableCell>
                <TableCell>
                  {t('reports.counts', {
                    total: r.findings,
                    open: r.openFindings,
                    late: r.lateFindings,
                    closed: r.closedFindings,
                  })}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {creating && (
        <ReportDialog
          onClose={() => setCreating(false)}
          onSaved={(id) =>
            void navigate({ to: '/audit-management/reports/$reportId', params: { reportId: id } })
          }
        />
      )}
    </AuditPageLayout>
  );
}
