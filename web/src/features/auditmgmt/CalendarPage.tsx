import { Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import {
  Button,
  EmptyState,
  FormField,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  ProgressBar,
  Section,
} from '@/ui';
import { useAuditCalendar, type AuditCalendarItem } from './api';
import { AuditPageLayout, PriorityChip, useAuditT, useDate, useNames } from './components';

type Range = 'week' | 'month' | 'next-month';

const iso = (d: Date) => d.toISOString().slice(0, 10);

function rangeOf(range: Range): { from: string; to: string } {
  const now = new Date();
  const today = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
  if (range === 'week') {
    const end = new Date(today);
    end.setUTCDate(end.getUTCDate() + 6);
    return { from: iso(today), to: iso(end) };
  }

  const offset = range === 'month' ? 0 : 1;
  const start = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + offset, 1));
  const end = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + offset + 1, 0));
  return { from: iso(start), to: iso(end) };
}

/** Follow-up calendar (Audit Office): next follow-up dates and due dates this week / month, by auditor; overdue follow-ups first. */
export function AuditCalendarPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const [range, setRange] = useState<Range>('month');
  const [auditor, setAuditor] = useState<string>();
  const { from, to } = rangeOf(range);
  const calendar = useAuditCalendar({ from, to, ...(auditor ? { auditorEmployeeId: auditor } : {}) });
  const date = useDate();
  const groups = useMemo(() => {
    const map = new Map<string, AuditCalendarItem[]>();
    for (const item of calendar.data ?? []) {
      const key = item.overdue && item.kind === 'follow-up' ? 'overdue' : item.date;
      map.set(key, [...(map.get(key) ?? []), item]);
    }

    return [...map.entries()].sort(([a], [b]) =>
      a === 'overdue' ? -1 : b === 'overdue' ? 1 : a.localeCompare(b),
    );
  }, [calendar.data]);

  return (
    <AuditPageLayout testId="audit-calendar">
      <PageHeader title={t('calendar.title')} description={t('calendar.description')} />
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex gap-1" role="group" aria-label={t('calendar.range')}>
          {(['week', 'month', 'next-month'] as const).map((r) => (
            <Button
              key={r}
              size="sm"
              variant={range === r ? 'primary' : 'ghost'}
              aria-pressed={range === r}
              onClick={() => setRange(r)}
              data-testid={`audit-calendar-${r}`}
            >
              {t(`calendar.${r}`)}
            </Button>
          ))}
        </div>
        <FormField label={t('fields.auditor')} className="min-w-64">
          <EmployeePicker value={auditor} onChange={setAuditor} />
        </FormField>
        <span className="text-sm text-muted-foreground">
          {date(from)} – {date(to)}
        </span>
      </div>
      {calendar.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, calendar.error)} />
      ) : !calendar.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : groups.length === 0 ? (
        <EmptyState title={t('calendar.empty')} />
      ) : (
        groups.map(([key, items]) => (
          <Section
            key={key}
            title={key === 'overdue' ? t('calendar.overdue') : date(key)}
            testId={`audit-calendar-day-${key}`}
          >
            <ul className="flex flex-col divide-y">
              {items.map((i) => (
                <CalendarItem key={`${i.recommendationId}-${i.kind}`} item={i} />
              ))}
            </ul>
          </Section>
        ))
      )}
    </AuditPageLayout>
  );
}

function CalendarItem({ item: i }: { item: AuditCalendarItem }) {
  const { k } = useAuditT();
  const names = useNames();
  const date = useDate();
  return (
    <li className="flex flex-wrap items-center gap-3 py-2 text-sm" data-testid="audit-calendar-item">
      <StatusChip tone={i.kind === 'due' ? 'warning' : i.overdue ? 'danger' : 'info'}>
        {k(`calendar.kind.${i.kind}`)}
      </StatusChip>
      {i.overdue && <span className="text-danger">{date(i.date)}</span>}
      <Link
        to="/audit-management/findings/$findingId"
        params={{ findingId: i.findingId }}
        search={{ tab: 'recommendations' }}
        className="text-primary hover:underline"
      >
        <bdi>{i.findingNumber}</bdi> · <bdi>{i.recommendationNumber}</bdi>
      </Link>
      <span className="max-w-96 truncate">{names.text(i.actionAr, i.actionEn)}</span>
      <PriorityChip priority={i.priority} />
      <span className="text-muted-foreground">{names.unit(i.unit)}</span>
      <span className="text-muted-foreground">{names.person(i.auditor)}</span>
      <div className="w-32">
        <ProgressBar
          value={i.latestPct}
          tone={i.status === 'late' ? 'danger' : 'primary'}
          testId="audit-pct"
        />
      </div>
    </li>
  );
}
