import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDateTime, formatNumber } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  useSyncMutation,
  useSyncStatus,
  type ObjectRunStats,
  type SyncConnection,
  type SyncObjectStatus,
  type SyncRun,
} from './api';
import { Code, Fact, formatDuration, modeTone, runTone, ServerMessage, useSyncT } from './shared';

interface StatusTabProps {
  connectionId: string;
  canManage: boolean;
  canFailover: boolean;
  onOpen: (tab: 'exceptions' | 'failover' | 'connection') => void;
}

const COUNTS = ['created', 'updated', 'unchanged', 'deleted', 'failed', 'conflicts'] as const;

/** Mode, last / next run, API usage, per-object table, outage banner, Sync now / Full load, Activate / Deactivate. */
export function StatusTab({ connectionId, canManage, canFailover, onOpen }: StatusTabProps) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const status = useSyncStatus(connectionId);
  const [confirmFull, setConfirmFull] = useState(false);
  const [confirmDeactivate, setConfirmDeactivate] = useState(false);
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const syncNow = useSyncMutation(async (full: boolean) =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/sync-now', {
        params: { path: { id: connectionId } },
        body: { full },
      }),
    ),
  );
  const setActive = useSyncMutation(async (active: boolean) =>
    active
      ? unwrap(
          await api.POST('/api/v1/integration/sync/connections/{id}/activate', {
            params: { path: { id: connectionId } },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/integration/sync/connections/{id}/deactivate', {
            params: { path: { id: connectionId } },
          }),
        ),
  );
  if (status.isPending) return <Skeleton className="mt-4 h-64 rounded-lg" />;
  if (!status.data) return null;
  const { connection, lastRun, lastSuccess, running, late, openExceptions, objects, outage } = status.data;
  const actions = connection.modeActions;
  const syncing = connection.mode === 'standby' || connection.mode === 'reconciling';
  const queueRun = (full: boolean) =>
    syncNow.mutate(full, {
      onSuccess: () => toast({ tone: 'success', title: t('sync.status.queued') }),
      onError,
    });
  return (
    <div className="flex flex-col gap-4 pt-4" data-testid="sync-status">
      {outage && (
        <StatusBanner
          tone={connection.mode === 'primary' ? 'danger' : 'warning'}
          title={tx(`sync.outage.banner.${connection.mode === 'primary' ? 'primary' : 'reconciling'}`)}
          description={t('sync.outage.since', {
            date: formatDateTime(outage.startedAt, language),
            reason: outage.reason,
          })}
          actions={
            canFailover && (
              <Button variant="outline" size="sm" onClick={() => onOpen('failover')}>
                {t('sync.outage.open')}
              </Button>
            )
          }
        />
      )}
      {late && syncing && (
        <StatusBanner
          tone="warning"
          title={t('sync.status.late')}
          description={
            lastSuccess
              ? t('sync.status.lastSuccess', {
                  date: formatDateTime(lastSuccess.finishedAt ?? lastSuccess.startedAt, language),
                })
              : t('sync.status.noSuccess')
          }
        />
      )}
      {connection.mode === 'disabled' && (
        <StatusBanner
          tone="info"
          title={t('sync.status.disabledTitle')}
          description={t('sync.status.disabledHint')}
        />
      )}
      <dl className="grid gap-4 rounded-lg border bg-card p-4 sm:grid-cols-2 lg:grid-cols-4">
        <Fact label={t('sync.status.mode')}>
          <StatusChip tone={modeTone[connection.mode] ?? 'neutral'}>
            {tx(`sync.modes.${connection.mode}`, { defaultValue: connection.mode })}
          </StatusChip>
        </Fact>
        <Fact label={t('sync.status.lastRun')}>
          {lastRun ? <LastRun run={lastRun} running={running} /> : t('sync.status.never')}
        </Fact>
        <Fact label={t('sync.status.nextRun')}>
          {connection.runRequested
            ? t('sync.status.requested')
            : connection.nextRunAt && syncing
              ? formatDateTime(connection.nextRunAt, language)
              : '—'}
        </Fact>
        <Fact label={t('sync.status.openExceptions')}>
          {openExceptions > 0 ? (
            <Button variant="link" className="h-auto p-0" onClick={() => onOpen('exceptions')}>
              {formatNumber(openExceptions, language)}
            </Button>
          ) : (
            formatNumber(0, language)
          )}
        </Fact>
        {lastRun?.messageKey && (
          <div className="sm:col-span-2 lg:col-span-4">
            <Fact label={t('sync.status.lastError')}>
              <ServerMessage messageKey={lastRun.messageKey} params={lastRun.params} />
            </Fact>
          </div>
        )}
      </dl>
      <ApiUsage connection={connection} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          {syncing && (
            <>
              <Button
                loading={syncNow.isPending && !syncNow.variables}
                disabled={running}
                onClick={() => queueRun(false)}
              >
                {t('sync.status.syncNow')}
              </Button>
              <Button variant="outline" disabled={running} onClick={() => setConfirmFull(true)}>
                {t('sync.status.fullLoad')}
              </Button>
            </>
          )}
          {actions.includes('activate') && (
            <Button
              variant="secondary"
              loading={setActive.isPending}
              onClick={() =>
                setActive.mutate(true, {
                  onSuccess: () => toast({ tone: 'success', title: t('sync.status.activated') }),
                  onError,
                })
              }
            >
              {t('sync.actions.activate')}
            </Button>
          )}
          {actions.includes('deactivate') && (
            <Button variant="outline" onClick={() => setConfirmDeactivate(true)}>
              {t('sync.actions.deactivate')}
            </Button>
          )}
          {actions.includes('activate') && !connection.lastTest?.ok && (
            <Button variant="link" onClick={() => onOpen('connection')}>
              {t('sync.status.testFirst')}
            </Button>
          )}
        </div>
      )}
      <ObjectTable objects={objects} />
      <ConfirmDialog
        open={confirmFull}
        onOpenChange={setConfirmFull}
        title={t('sync.status.fullLoad')}
        description={t('sync.status.fullLoadWarning')}
        confirmLabel={t('sync.status.fullLoad')}
        loading={syncNow.isPending}
        onConfirm={() => {
          setConfirmFull(false);
          queueRun(true);
        }}
      />
      <ConfirmDialog
        open={confirmDeactivate}
        onOpenChange={setConfirmDeactivate}
        title={t('sync.actions.deactivate')}
        description={t('sync.status.deactivateWarning')}
        confirmLabel={t('sync.actions.deactivate')}
        destructive
        loading={setActive.isPending}
        onConfirm={() => setActive.mutate(false, { onSuccess: () => setConfirmDeactivate(false), onError })}
      />
    </div>
  );
}

function LastRun({ run, running }: { run: SyncRun; running: boolean }) {
  const { tx, language } = useSyncT();
  const state = running ? 'running' : run.status;
  return (
    <span className="flex flex-wrap items-center gap-2">
      <StatusChip tone={runTone[state] ?? 'neutral'}>
        {tx(`sync.runStatus.${state}`, { defaultValue: state })}
      </StatusChip>
      <span>{formatDateTime(run.startedAt, language)}</span>
      <span className="text-muted-foreground">{formatDuration(run.durationSeconds, language, tx)}</span>
    </span>
  );
}

/** Daily API requests used, from /limits or — when the integration user may not read it — the usage header of normal calls (ADR 0210). */
function ApiUsage({ connection }: { connection: SyncConnection }) {
  const { t, language } = useSyncT();
  const limits = connection.limits;
  if (!limits?.dailyApiMax || limits.dailyApiRemaining === null) return null;
  const used = limits.dailyApiMax - limits.dailyApiRemaining;
  const ratio = Math.min(1, Math.max(0, used / limits.dailyApiMax));
  const tone = ratio > 0.9 ? 'bg-danger' : ratio > 0.7 ? 'bg-warning' : 'bg-primary';
  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-card p-4" data-testid="api-usage">
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <span className="font-medium">{t('sync.status.apiUsage')}</span>
        <span className="text-muted-foreground">
          {t('sync.status.apiUsed', {
            used: formatNumber(used, language),
            max: formatNumber(limits.dailyApiMax, language),
            percent: formatNumber(Math.round(ratio * 100), language),
          })}
        </span>
      </div>
      <div
        className="h-2 w-full overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-label={t('sync.status.apiUsage')}
        aria-valuemin={0}
        aria-valuemax={limits.dailyApiMax}
        aria-valuenow={used}
      >
        <div className={`h-full ${tone}`} style={{ inlineSize: `${ratio * 100}%` }} />
      </div>
      <span className="text-xs text-muted-foreground">
        {t('sync.status.limitsAt', { date: formatDateTime(limits.at, language) })}
        {limits.bulkJobsMax
          ? ` · ${t('sync.status.bulkJobs', {
              remaining: formatNumber(limits.bulkJobsRemaining ?? 0, language),
              max: formatNumber(limits.bulkJobsMax, language),
            })}`
          : ''}
        {limits.source === 'header' && ` · ${t('sync.status.limitsFromHeader')}`}
      </span>
    </div>
  );
}

function ObjectTable({ objects }: { objects: readonly SyncObjectStatus[] }) {
  const { t, tx, language } = useSyncT();
  const count = (stats: ObjectRunStats | null, key: (typeof COUNTS)[number]) =>
    stats ? formatNumber(stats[key] ?? 0, language) : '—';
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('sync.objects.object')}</TableHeaderCell>
          <TableHeaderCell>{t('sync.objects.target')}</TableHeaderCell>
          <TableHeaderCell>{t('sync.objects.state')}</TableHeaderCell>
          <TableHeaderCell>{t('sync.objects.watermark')}</TableHeaderCell>
          <TableHeaderCell>{t('sync.objects.lastFullLoad')}</TableHeaderCell>
          {COUNTS.map((key) => (
            <TableHeaderCell key={key} className="text-end">
              {tx(`sync.counts.${key}`)}
            </TableHeaderCell>
          ))}
          <TableHeaderCell className="text-end">{t('sync.objects.openExceptions')}</TableHeaderCell>
          <TableHeaderCell>{t('sync.objects.reconciliation')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {objects.map((o) => (
          <TableRow key={o.id} data-testid={`sync-object-${o.key}`}>
            <TableCell className="min-w-48 whitespace-nowrap">
              <div className="flex flex-col gap-0.5">
                <Code>{o.key}</Code>
                <span className="text-xs text-muted-foreground">
                  <Code>{o.sourceObject}</Code>
                </span>
              </div>
            </TableCell>
            <TableCell className="whitespace-nowrap">
              {o.targetType ? (
                <Code>{o.targetType}</Code>
              ) : (
                tx(`sync.objectKinds.${o.kind}`, { defaultValue: o.kind })
              )}
            </TableCell>
            <TableCell>
              <div className="flex flex-wrap gap-1">
                <StatusChip tone={o.enabled ? 'success' : 'neutral'}>
                  {o.enabled ? t('sync.objects.enabled') : t('sync.objects.disabled')}
                </StatusChip>
                {o.confirmed ? (
                  <StatusChip tone="info">{t('sync.objects.confirmed')}</StatusChip>
                ) : (
                  <StatusChip tone="warning">{t('sync.objects.notConfirmed')}</StatusChip>
                )}
                {o.provisional && <StatusChip tone="warning">{t('sync.objects.provisional')}</StatusChip>}
              </div>
            </TableCell>
            <TableCell>{o.watermark ? formatDateTime(o.watermark, language) : '—'}</TableCell>
            <TableCell>{o.lastFullLoadAt ? formatDateTime(o.lastFullLoadAt, language) : '—'}</TableCell>
            {COUNTS.map((key) => (
              <TableCell key={key} className="text-end tabular-nums">
                {count(o.lastRun, key)}
              </TableCell>
            ))}
            <TableCell className="text-end tabular-nums">
              {formatNumber(o.openExceptions, language)}
            </TableCell>
            <TableCell className="whitespace-nowrap" data-testid={`sync-reconciliation-${o.key}`}>
              {o.reconciliation ? (
                <span title={o.reconciledAt ? formatDateTime(o.reconciledAt, language) : undefined}>
                  <StatusChip tone={o.reconciliation.difference === 0 ? 'success' : 'warning'}>
                    {t(
                      o.reconciliation.difference === 0
                        ? 'sync.objects.reconciled'
                        : 'sync.objects.reconcileDifference',
                      {
                        source: formatNumber(o.reconciliation.source, language),
                        synced: formatNumber(o.reconciliation.synced, language),
                        open: formatNumber(o.reconciliation.openExceptions, language),
                        difference: formatNumber(o.reconciliation.difference, language),
                      },
                    )}
                  </StatusChip>
                </span>
              ) : (
                '—'
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
