import { AlarmClock, Lock } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { StatusChip, type Tone } from '@/ui';

const statusTones: Record<string, Tone> = {
  draft: 'neutral',
  assigned: 'info',
  to_do: 'info',
  in_progress: 'info',
  on_hold: 'warning',
  submitted: 'info',
  approved: 'success',
  done: 'success',
  rejected: 'danger',
  cancelled: 'neutral',
};

const priorityTones: Record<string, Tone> = { high: 'danger', medium: 'warning', low: 'neutral' };

const indicatorTones: Record<string, Tone> = {
  on_time: 'success',
  due_soon: 'warning',
  overdue: 'danger',
  on_hold: 'neutral',
};

/** The status name of the task's template (names are per template, ADR 0036). */
export function useStatusLabel() {
  const { t } = useTranslation('tasks');
  return (template: string, status: string) =>
    t(`status.${template === 'simple' ? 'simple' : 'full'}.${status}` as 'status.full.assigned', {
      defaultValue: status,
    });
}

export function TaskStatusChip({ template, status }: { template: string; status: string }) {
  const label = useStatusLabel();
  return <StatusChip tone={statusTones[status] ?? 'neutral'}>{label(template, status)}</StatusChip>;
}

export function PriorityChip({ priority }: { priority: string }) {
  const { t } = useTranslation('tasks');
  return (
    <StatusChip tone={priorityTones[priority] ?? 'neutral'}>
      {t(`priority.${priority}` as 'priority.high', { defaultValue: priority })}
    </StatusChip>
  );
}

export function IndicatorChip({ indicator }: { indicator: string | null | undefined }) {
  const { t } = useTranslation('tasks');
  if (!indicator) return null;
  return (
    <StatusChip tone={indicatorTones[indicator] ?? 'neutral'}>
      {indicator === 'overdue' && <AlarmClock className="size-3" aria-hidden />}
      {t(`indicator.${indicator}` as 'indicator.overdue', { defaultValue: indicator })}
    </StatusChip>
  );
}

export function ConfidentialChip() {
  const { t } = useTranslation('tasks');
  return (
    <StatusChip tone="warning">
      <Lock className="size-3" aria-hidden />
      {t('fields.confidentialShort')}
    </StatusChip>
  );
}

export { statusTones };
