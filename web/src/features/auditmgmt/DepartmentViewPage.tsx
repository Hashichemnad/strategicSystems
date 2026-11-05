import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import {
  EmptyState,
  PageHeader,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  ProgressBar,
  Section,
  Tile,
} from '@/ui';
import { useDepartmentView } from './api';
import { AuditPageLayout, RecommendationStatusChip, useAuditT, useDate, useNames } from './components';
import { FindingsTable } from './FindingsPage';

/**
 * Department view (read-only, replaces the Salesforce portal dashboard): the audits and findings of the user's own section / department —
 * status, completion, Audit Office comments and what is due soon. No action is offered: the Audit Office records every update.
 */
export function AuditDepartmentViewPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const view = useDepartmentView();
  const names = useNames();
  const date = useDate();

  if (view.isError) return <StatusBanner tone="danger" title={errorMessage(tc, view.error)} />;
  if (!view.data) return <Skeleton className="h-96 rounded-lg" />;
  const d = view.data;
  return (
    <AuditPageLayout testId="audit-department-view">
      <PageHeader
        title={t('department.title')}
        description={t('department.description', {
          units: d.units.map((u) => names.unit(u)).join('، ') || '—',
        })}
      />
      <StatusBanner
        tone="info"
        title={t('department.readOnly')}
        description={k(`department.scope.${d.scope}`)}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" data-testid="audit-department-tiles">
        <Tile label={t('department.audits')} value={d.audits} />
        <Tile label={t('department.auditsClosed')} value={d.auditsClosed} tone="success" />
        <Tile label={t('department.auditsActive')} value={d.auditsActive} />
        <Tile label={t('department.activeFindings')} value={d.activeFindings} />
        <Tile label={t('department.lateFindings')} value={d.lateFindings} tone="danger" />
        <Tile label={t('department.closedFindings')} value={d.closedFindings} tone="success" />
      </div>
      <Section title={t('department.active')} testId="audit-department-active">
        {d.active.length === 0 ? (
          <EmptyState title={t('department.noActive')} />
        ) : (
          <FindingsTable items={d.active} />
        )}
      </Section>
      <Section title={t('department.dueSoon')} testId="audit-department-due">
        {d.dueSoon.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('department.noDue')}</p>
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.recommendation')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.finding')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.dueOn')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.pct')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {d.dueSoon.map((r) => (
                <TableRow key={r.recommendationId}>
                  <TableCell>
                    <bdi>{r.number}</bdi> {names.text(r.actionAr, r.actionEn)}
                  </TableCell>
                  <TableCell>
                    <Link
                      to="/audit-management/findings/$findingId"
                      params={{ findingId: r.findingId }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{r.findingNumber}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell>{date(r.dueOn)}</TableCell>
                  <TableCell>
                    <ProgressBar
                      value={r.latestPct}
                      tone={r.status === 'late' ? 'danger' : 'primary'}
                      testId="audit-pct"
                    />
                  </TableCell>
                  <TableCell>
                    <RecommendationStatusChip status={r.status} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
    </AuditPageLayout>
  );
}
