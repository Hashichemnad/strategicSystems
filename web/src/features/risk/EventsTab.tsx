import { Link } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  Section,
} from '@/ui';
import { riskApi, todayIso, useRiskEvents, useRiskMutation, type EventValues, type Risk } from './api';
import { useNum, useRiskT } from './components';

export function EventsTab({ risk }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { language } = useRiskT();
  const events = useRiskEvents(risk.id);
  const num = useNum();
  const [open, setOpen] = useState(false);
  return (
    <Section
      title={t('events.title')}
      testId="risk-events"
      actions={
        risk.can.logEvent && (
          <Button size="sm" onClick={() => setOpen(true)} data-testid="risk-event-add">
            <Plus aria-hidden />
            {t('events.log')}
          </Button>
        )
      }
    >
      {!events.data ? (
        <Skeleton className="h-24" />
      ) : events.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('events.empty')}</p>
      ) : (
        <Table data-testid="risk-events-table">
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
              <TableHeaderCell>{t('events.occurredOn')}</TableHeaderCell>
              <TableHeaderCell>{t('events.description')}</TableHeaderCell>
              <TableHeaderCell>{t('events.financialImpact')}</TableHeaderCell>
              <TableHeaderCell>{t('events.rootCause')}</TableHeaderCell>
              <TableHeaderCell>{t('events.lessons')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {events.data.map((e) => (
              <TableRow key={e.id} data-testid="risk-event-row">
                <TableCell>
                  <span className="flex flex-col gap-1">
                    <bdi className="font-medium">{e.number}</bdi>
                    {e.values.nearMiss && <StatusChip tone="info">{t('events.nearMiss')}</StatusChip>}
                    {e.capRemarkId && (
                      <Link
                        to="/cap/remarks/$remarkId"
                        params={{ remarkId: e.capRemarkId }}
                        className="text-sm text-primary hover:underline"
                        data-testid="risk-event-cap-remark"
                      >
                        <StatusChip tone="warning">{t('events.capRemark')}</StatusChip>
                      </Link>
                    )}
                  </span>
                </TableCell>
                <TableCell>{formatDate(e.values.occurredOn, language)}</TableCell>
                <TableCell className="max-w-72 whitespace-pre-line">
                  {e.values.description ?? '—'}
                  {e.values.otherImpact && (
                    <span className="block text-xs text-muted-foreground">{e.values.otherImpact}</span>
                  )}
                </TableCell>
                <TableCell className="tabular-nums">
                  {e.values.financialImpact == null
                    ? '—'
                    : `${num(e.values.financialImpact)} ${e.values.currency ?? ''}`}
                </TableCell>
                <TableCell className="max-w-56 whitespace-pre-line">{e.values.rootCause ?? '—'}</TableCell>
                <TableCell className="max-w-56 whitespace-pre-line">{e.values.lessons ?? '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {open && <EventDialog risk={risk} onClose={() => setOpen(false)} />}
    </Section>
  );
}

function EventDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const log = useRiskMutation(riskApi.logEvent);
  const [v, setV] = useState<EventValues>({
    occurredOn: todayIso(),
    description: null,
    financialImpact: null,
    currency: 'AED',
    otherImpact: null,
    nearMiss: false,
    rootCause: null,
    lessons: null,
    unitId: risk.values.unitId,
    riskIds: [risk.id],
  });
  const set = <K extends keyof EventValues>(key: K, value: EventValues[K]) =>
    setV((p) => ({ ...p, [key]: value }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('events.log')}
        description={t('events.hint')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={log.isPending}
              disabled={!v.occurredOn || !v.description?.trim()}
              data-testid="risk-event-save"
              onClick={() =>
                log.mutate(v, {
                  onSuccess: onClose,
                  onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                })
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('events.occurredOn')} required>
            <Input type="date" value={v.occurredOn} onChange={(e) => set('occurredOn', e.target.value)} />
          </FormField>
          <Checkbox
            label={t('events.nearMiss')}
            checked={v.nearMiss}
            onChange={(e) => set('nearMiss', e.target.checked)}
          />
          <FormField label={t('events.description')} required className="md:col-span-2">
            <Textarea
              value={v.description ?? ''}
              onChange={(e) => set('description', e.target.value || null)}
              data-testid="risk-event-description"
            />
          </FormField>
          <FormField label={t('events.financialImpact')}>
            <Input
              type="number"
              min={0}
              step="any"
              value={v.financialImpact ?? ''}
              onChange={(e) => set('financialImpact', e.target.value === '' ? null : Number(e.target.value))}
            />
          </FormField>
          <FormField label={t('events.currency')}>
            <Input
              value={v.currency ?? ''}
              maxLength={3}
              onChange={(e) => set('currency', e.target.value.toUpperCase() || null)}
            />
          </FormField>
          <FormField label={t('events.otherImpact')} className="md:col-span-2">
            <Textarea
              value={v.otherImpact ?? ''}
              onChange={(e) => set('otherImpact', e.target.value || null)}
            />
          </FormField>
          <FormField label={t('events.rootCause')}>
            <Textarea value={v.rootCause ?? ''} onChange={(e) => set('rootCause', e.target.value || null)} />
          </FormField>
          <FormField label={t('events.lessons')}>
            <Textarea value={v.lessons ?? ''} onChange={(e) => set('lessons', e.target.value || null)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}
