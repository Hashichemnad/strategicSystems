import { CalendarClock } from 'lucide-react';
import { Fragment, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  EmptyState,
  PageHeader,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { useDeliveries, useSchedules } from './api';
import { ScheduleDialog } from './ScheduleDialog';

function Deliveries({ id }: { id: string }) {
  const { t, i18n } = useTranslation('reporting');
  const deliveries = useDeliveries(id);
  if (deliveries.isPending) return <Skeleton className="h-16" />;
  if (!deliveries.data?.length)
    return <p className="text-sm text-muted-foreground">{t('schedules.noDeliveries')}</p>;
  return (
    <Table data-testid="schedule-deliveries">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('schedules.sentAt')}</TableHeaderCell>
          <TableHeaderCell>{t('schedules.period')}</TableHeaderCell>
          <TableHeaderCell>{t('schedules.status')}</TableHeaderCell>
          <TableHeaderCell>{t('schedules.rows')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {deliveries.data.map((d) => (
          <TableRow key={d.id}>
            <TableCell>{formatDateTime(d.at, i18n.language)}</TableCell>
            <TableCell>{d.periodKey}</TableCell>
            <TableCell>
              <StatusChip
                tone={d.status === 'sent' ? 'success' : d.status === 'failed' ? 'danger' : 'neutral'}
              >
                {t(`schedules.statuses.${d.status}` as 'schedules.statuses.sent')}
              </StatusChip>
            </TableCell>
            <TableCell className="tabular-nums">{d.rows}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** Scheduled dashboards and reports of the user (all of them for administrators), with their delivery log. */
export function SchedulesPage() {
  const { t, i18n } = useTranslation('reporting');
  const schedules = useSchedules();
  const [open, setOpen] = useState<string>();
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('schedules.title')} description={t('schedules.description')} />
      {schedules.isPending ? (
        <Skeleton className="h-40" />
      ) : !schedules.data?.length ? (
        <EmptyState icon={CalendarClock} title={t('schedules.empty')} />
      ) : (
        <Table data-testid="schedules-table">
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('schedule.name')}</TableHeaderCell>
              <TableHeaderCell>{t('schedule.frequency')}</TableHeaderCell>
              <TableHeaderCell>{t('schedule.format')}</TableHeaderCell>
              <TableHeaderCell>{t('schedules.next')}</TableHeaderCell>
              <TableHeaderCell>{t('schedules.last')}</TableHeaderCell>
              <TableHeaderCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {schedules.data.map((s) => (
              <Fragment key={s.id}>
                <TableRow>
                  <TableCell>
                    {s.name} {!s.active && <StatusChip>{t('schedules.paused')}</StatusChip>}
                  </TableCell>
                  <TableCell>
                    {t(`schedule.frequencies.${s.frequency}` as 'schedule.frequencies.daily')} · {s.time}
                  </TableCell>
                  <TableCell>{t(`schedule.formats.${s.format}` as 'schedule.formats.pdf')}</TableCell>
                  <TableCell>{s.nextRunAt ? formatDateTime(s.nextRunAt, i18n.language) : '—'}</TableCell>
                  <TableCell>{s.lastRunAt ? formatDateTime(s.lastRunAt, i18n.language) : '—'}</TableCell>
                  <TableCell className="flex gap-2">
                    <ScheduleDialog
                      targetKind={s.targetKind as 'report'}
                      targetId={s.targetId}
                      defaultName={s.name}
                      existing={s}
                      trigger={
                        <Button size="sm" variant="outline">
                          {t('schedules.edit')}
                        </Button>
                      }
                    />
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-expanded={open === s.id}
                      onClick={() => setOpen(open === s.id ? undefined : s.id)}
                    >
                      {t('schedules.deliveries')}
                    </Button>
                  </TableCell>
                </TableRow>
                {open === s.id && (
                  <TableRow>
                    <TableCell colSpan={6}>
                      <Deliveries id={s.id} />
                    </TableCell>
                  </TableRow>
                )}
              </Fragment>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
