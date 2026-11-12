import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  PageHeader,
  Select,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
  type Tone,
} from '@/ui';
import { useReplayDeliveries, useWebhookDeliveries } from './clients';

const tone: Record<string, Tone> = {
  pending: 'info',
  retrying: 'warning',
  delivered: 'success',
  dead: 'danger',
};

/** Settings → Integrations → Webhook deliveries (ADR 0194): the delivery log of every client, dead letters first, replay. */
export function WebhookDeliveriesPage() {
  const { t } = useTranslation('integration');
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('deliveries.title')} description={t('deliveries.description')} />
      <DeliveryLog initialStatus="dead" />
    </div>
  );
}

/** The delivery log of one client (or all), filterable by status, with replay of selected deliveries and attempt details. */
export function DeliveryLog({ clientId, initialStatus }: { clientId?: string; initialStatus?: string }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [status, setStatus] = useState(initialStatus ?? '-');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const deliveries = useWebhookDeliveries(clientId, status === '-' ? undefined : status, page);
  const replay = useReplayDeliveries();
  const items = deliveries.data?.items ?? [];
  return (
    <div className="flex flex-col gap-4 pt-4" data-testid="delivery-log">
      <div className="flex flex-wrap items-end gap-3">
        <div className="w-48">
          <Select
            value={status}
            onValueChange={(value) => {
              setStatus(value);
              setPage(1);
              setSelected([]);
            }}
            aria-label={t('deliveries.status')}
            options={[
              { value: '-', label: t('deliveries.all') },
              ...['dead', 'retrying', 'pending', 'delivered'].map((s) => ({
                value: s,
                label: t(`deliveries.states.${s}` as 'deliveries.states.dead'),
              })),
            ]}
          />
        </div>
        <Button
          variant="outline"
          disabled={selected.length === 0}
          loading={replay.isPending}
          onClick={() =>
            replay.mutate(selected, {
              onSuccess: (count) => {
                setSelected([]);
                toast({ tone: 'success', title: t('deliveries.replayed', { count }) });
              },
              onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
            })
          }
        >
          {t('deliveries.replay', { count: selected.length })}
        </Button>
      </div>
      {deliveries.isPending ? (
        <Skeleton className="h-40 rounded-lg" />
      ) : items.length === 0 ? (
        <EmptyState title={t('deliveries.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>
                <span className="sr-only">{t('deliveries.select')}</span>
              </TableHeaderCell>
              <TableHeaderCell>{t('deliveries.event')}</TableHeaderCell>
              {!clientId && <TableHeaderCell>{t('deliveries.client')}</TableHeaderCell>}
              <TableHeaderCell>{t('deliveries.created')}</TableHeaderCell>
              <TableHeaderCell>{t('deliveries.attempts')}</TableHeaderCell>
              <TableHeaderCell>{t('deliveries.last')}</TableHeaderCell>
              <TableHeaderCell>{t('deliveries.status')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map((d) => (
              <TableRow key={d.id} data-testid={`delivery-${d.status}`}>
                <TableCell>
                  <Checkbox
                    label={<span className="sr-only">{d.eventCode}</span>}
                    checked={selected.includes(d.id)}
                    onChange={(e) =>
                      setSelected(e.target.checked ? [...selected, d.id] : selected.filter((x) => x !== d.id))
                    }
                  />
                </TableCell>
                <TableCell>
                  <button
                    type="button"
                    className="font-mono text-xs text-primary hover:underline"
                    dir="ltr"
                    onClick={() => setOpen(d.id)}
                  >
                    {d.eventCode}
                  </button>
                  <div className="text-xs text-muted-foreground">{d.subscriptionName}</div>
                </TableCell>
                {!clientId && <TableCell>{d.clientName}</TableCell>}
                <TableCell>{formatDateTime(d.createdAt, i18n.language)}</TableCell>
                <TableCell>{d.attempts}</TableCell>
                <TableCell>
                  {d.lastStatusCode ?? ''}{' '}
                  {d.lastError && <span className="text-xs text-muted-foreground">{d.lastError}</span>}
                  {d.nextAttemptAt && (
                    <div className="text-xs text-muted-foreground">
                      {t('deliveries.nextAttempt', { at: formatDateTime(d.nextAttemptAt, i18n.language) })}
                    </div>
                  )}
                </TableCell>
                <TableCell>
                  <StatusChip tone={tone[d.status] ?? 'neutral'}>
                    {t(`deliveries.states.${d.status}` as 'deliveries.states.dead')}
                  </StatusChip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {deliveries.data && deliveries.data.totalCount > deliveries.data.pageSize && (
        <div className="flex gap-2">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {t('pager.previous')}
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={page * deliveries.data.pageSize >= deliveries.data.totalCount}
            onClick={() => setPage(page + 1)}
          >
            {t('pager.next')}
          </Button>
        </div>
      )}
      {open && <DeliveryDialog id={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function DeliveryDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const detail = useQuery({
    queryKey: ['integration', 'deliveries', 'detail', id],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/webhooks/deliveries/{id}', { params: { path: { id } }, signal }),
      ),
  });
  let payload = detail.data?.payload ?? '';
  try {
    payload = JSON.stringify(JSON.parse(payload), null, 2);
  } catch {
    // shown as stored
  }
  return (
    <Dialog open onOpenChange={(value) => !value && onClose()}>
      <DialogContent
        className="max-w-3xl"
        title={t('deliveries.detail')}
        footer={
          <DialogClose asChild>
            <Button>{tc('actions.close')}</Button>
          </DialogClose>
        }
      >
        {detail.isPending ? (
          <Skeleton className="h-40" />
        ) : (
          <div className="flex flex-col gap-3">
            <pre dir="ltr" className="max-h-72 overflow-auto rounded-md bg-muted p-3 text-start text-xs">
              {payload}
            </pre>
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>#</TableHeaderCell>
                  <TableHeaderCell>{t('deliveries.at')}</TableHeaderCell>
                  <TableHeaderCell>{t('deliveries.result')}</TableHeaderCell>
                  <TableHeaderCell>{t('usage.duration')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {detail.data?.attempts.map((a) => (
                  <TableRow key={a.number}>
                    <TableCell>{a.number}</TableCell>
                    <TableCell>{formatDateTime(a.at, i18n.language)}</TableCell>
                    <TableCell>{a.error ?? a.statusCode}</TableCell>
                    <TableCell>{t('usage.ms', { count: a.durationMs })}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
