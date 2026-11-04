import { Link } from '@tanstack/react-router';
import { ListChecks } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, pickText } from '@/lib/format';
import {
  EmptyState,
  PageHeader,
  Skeleton,
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
} from '@/ui';
import { resultTone, targetText, useKpiPending, type KpiResultListItem } from './api';
import { KpiPage, PerformanceValue, useKpiT, useNumber } from './components';

/** My Pending Updates (SPEC §2.5): the owner's open, returned and pending results with due dates. */
export function KpiPendingPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const [view, setView] = useState<'mine' | 'scope'>('mine');
  const canScope = useCan('kpi.result.submit');
  const pending = useKpiPending(view === 'scope' ? 'scope' : undefined);
  return (
    <KpiPage testId="kpi-pending">
      <PageHeader title={t('pending.title')} description={t('pending.description')} />
      {canScope && (
        <Tabs value={view} onValueChange={(v) => setView(v as 'mine')}>
          <TabsList>
            <TabsTrigger value="mine">{t('pending.mine')}</TabsTrigger>
            <TabsTrigger value="scope">{t('pending.scope')}</TabsTrigger>
          </TabsList>
        </Tabs>
      )}
      {pending.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, pending.error)} />
      ) : !pending.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : pending.data.length === 0 ? (
        <EmptyState icon={ListChecks} title={t('pending.empty')} />
      ) : (
        <ResultsTable items={pending.data} testId="kpi-pending-table" />
      )}
    </KpiPage>
  );
}

/** Results with their period, target, value, performance, due date and flags (pending updates, approval queue). */
export function ResultsTable({
  items,
  testId,
  select,
}: {
  items: KpiResultListItem[];
  testId?: string;
  select?: { selected: Set<string>; onToggle: (id: string, on: boolean) => void };
}) {
  const { t } = useTranslation('kpi');
  const { k, language } = useKpiT();
  const num = useNumber();
  return (
    <div className="overflow-x-auto">
      <Table data-testid={testId}>
        <TableHead>
          <TableRow>
            {select && (
              <TableHeaderCell>
                <span className="sr-only">{t('bulk.select')}</span>
              </TableHeaderCell>
            )}
            <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.period')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.responsibleUnitId')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.target')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.achieve')}</TableHeaderCell>
            <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
            <TableHeaderCell>{t('result.dueOn')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((r) => (
            <TableRow key={r.id} data-testid="kpi-result-row">
              {select && (
                <TableCell>
                  <input
                    type="checkbox"
                    className="size-4 accent-primary"
                    aria-label={`${r.cardCode} ${r.periodCode}`}
                    checked={select.selected.has(r.id)}
                    onChange={(e) => select.onToggle(r.id, e.target.checked)}
                    data-testid="kpi-bulk-check"
                  />
                </TableCell>
              )}
              <TableCell>
                <Link
                  to="/kpi/results/$resultId"
                  params={{ resultId: r.id }}
                  className="font-medium text-primary hover:underline"
                >
                  <bdi>{r.cardCode}</bdi>
                </Link>
                <span className="block text-xs text-muted-foreground">
                  {pickText(language, r.cardNameAr, r.cardNameEn)}
                </span>
              </TableCell>
              <TableCell>
                {k(`period.${r.periodCode}`)} {r.year}
              </TableCell>
              <TableCell>
                {r.ownerUnit ? pickText(language, r.ownerUnit.nameAr, r.ownerUnit.nameEn) : '—'}
              </TableCell>
              <TableCell className="tabular-nums">
                {targetText(num, r.targetType, r.target, r.rangeMin, r.rangeMax)}
              </TableCell>
              <TableCell className="tabular-nums">
                {r.notApplicable ? t('result.na') : num(r.achieve)}
              </TableCell>
              <TableCell>
                <PerformanceValue capped={r.capped} />
              </TableCell>
              <TableCell>{r.dueOn ? formatDate(r.dueOn, language) : '—'}</TableCell>
              <TableCell>
                <span className="flex flex-wrap gap-1">
                  <StatusChip tone={resultTone[r.status] ?? 'neutral'}>
                    {k(`resultStatus.${r.status}`)}
                  </StatusChip>
                  {r.overdue && <StatusChip tone="danger">{t('result.overdue')}</StatusChip>}
                  {r.late && (
                    <StatusChip tone="warning">{t('result.lateDays', { count: r.daysLate })}</StatusChip>
                  )}
                  {r.anomalies.length > 0 && (
                    <StatusChip tone="warning">
                      {t('result.anomalies', { count: r.anomalies.length })}
                    </StatusChip>
                  )}
                  {r.status === 'pending_approval' && r.evidenceCount === 0 && !r.notApplicable && (
                    <StatusChip tone="warning">{t('result.noEvidence')}</StatusChip>
                  )}
                  {r.myDecision && <StatusChip tone="info">{t('approvals.yourDecision')}</StatusChip>}
                </span>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
