import { CalendarClock } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { Button, Dialog, DialogContent, DialogTrigger, FormField, Input, Select, StatusBanner, Switch, useToast } from '@/ui';
import { useSaveSchedule, type Schedule } from './api';
import { PrincipalListEditor, type PrincipalRef } from './shared';

export const periodRules = ['previous-month', 'current-month', 'previous-quarter', 'current-quarter', 'previous-year', 'current-year'] as const;

/** Schedule a dashboard or report by e-mail (ADR 0145): built as each recipient, in their language, at send time. */
export function ScheduleDialog({
  targetKind,
  targetId,
  defaultName,
  existing,
  trigger,
}: {
  targetKind: 'dashboard' | 'report';
  targetId: string;
  defaultName: string;
  existing?: Schedule;
  trigger?: React.ReactNode;
}) {
  const { t } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSaveSchedule(existing?.id);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(existing?.name ?? defaultName);
  const [frequency, setFrequency] = useState(existing?.frequency ?? 'weekly');
  const [time, setTime] = useState(existing?.time ?? '07:00');
  const [day, setDay] = useState(String(existing?.day ?? 0));
  const [periodRule, setPeriodRule] = useState(existing?.periodRule ?? 'previous-month');
  const [format, setFormat] = useState(existing?.format ?? 'pdf');
  const [active, setActive] = useState(existing?.active ?? true);
  const [recipients, setRecipients] = useState<PrincipalRef[]>(
    existing?.recipients.map((r) => ({ kind: r.kind as PrincipalRef['kind'], ref: r.ref, label: r.ref })) ?? [],
  );
  const days =
    frequency === 'weekly'
      ? [0, 1, 2, 3, 4, 5, 6].map((d) => ({ value: String(d), label: t(`schedule.weekdays.${d}` as 'schedule.weekdays.0') }))
      : Array.from({ length: 28 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }));

  const submit = () =>
    save.mutate(
      {
        input: {
          name,
          targetKind,
          targetId,
          frequency,
          time,
          day: frequency === 'daily' ? 1 : Number(day),
          periodRule,
          format,
          active,
          recipients: recipients.map((r) => ({ kind: r.kind, ref: r.ref })),
        },
        version: existing?.version,
      },
      {
        onSuccess: () => {
          toast({ title: t('schedule.saved') });
          setOpen(false);
        },
      },
    );

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        {trigger ?? (
          <Button variant="outline" data-testid="schedule-open">
            <CalendarClock className="size-4" aria-hidden />
            {t('schedule.title')}
          </Button>
        )}
      </DialogTrigger>
      <DialogContent
        title={t('schedule.title')}
        description={t('schedule.description')}
        footer={
          <Button onClick={submit} disabled={save.isPending || recipients.length === 0} data-testid="schedule-save">
            {t('schedule.save')}
          </Button>
        }
      >
        <div className="flex flex-col gap-3" data-testid="schedule-dialog">
          {save.isError && <StatusBanner tone="danger" title={errorMessage(tc, save.error)} />}
          <FormField label={t('schedule.name')}>
            <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
          </FormField>
          <div className="grid gap-3 sm:grid-cols-3">
            <FormField label={t('schedule.frequency')}>
              <Select
                value={frequency}
                onValueChange={(v) => {
                  setFrequency(v);
                  setDay(v === 'weekly' ? '0' : '1');
                }}
                options={['daily', 'weekly', 'monthly'].map((f) => ({ value: f, label: t(`schedule.frequencies.${f}` as 'schedule.frequencies.daily') }))}
              />
            </FormField>
            {frequency !== 'daily' && (
              <FormField label={t('schedule.day')}>
                <Select value={day} onValueChange={setDay} options={days} />
              </FormField>
            )}
            <FormField label={t('schedule.time')}>
              <Input type="time" value={time} onChange={(e) => setTime(e.target.value)} />
            </FormField>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <FormField label={t('schedule.period')}>
              <Select
                value={periodRule}
                onValueChange={setPeriodRule}
                options={periodRules.map((r) => ({ value: r, label: t(`schedule.periodRules.${r}`) }))}
              />
            </FormField>
            <FormField label={t('schedule.format')}>
              <Select
                value={format}
                onValueChange={setFormat}
                options={['pdf', 'xlsx', 'link'].map((f) => ({ value: f, label: t(`schedule.formats.${f}` as 'schedule.formats.pdf') }))}
              />
            </FormField>
          </div>
          <FormField label={t('schedule.recipients')} hint={t('schedule.recipientsHint')}>
            <PrincipalListEditor value={recipients} onChange={setRecipients} testId="schedule-recipients" />
          </FormField>
          <Switch checked={active} onCheckedChange={setActive} label={t('schedule.active')} />
        </div>
      </DialogContent>
    </Dialog>
  );
}
