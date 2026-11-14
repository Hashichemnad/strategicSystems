import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, formatNumber } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  Pagination,
  ScrollRegion,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { useSyncRun, useSyncRuns, type SyncRun } from './api';
import { Code, formatDuration, runTone, ServerMessage, severityTone, useSyncT } from './shared';

function totals(run: SyncRun) {
  const sum = { read: 0, created: 0, updated: 0, failed: 0 };
  for (const stats of Object.values(run.objects)) {
    sum.read += stats.read ?? 0;
    sum.created += stats.created ?? 0;
    sum.updated += stats.updated ?? 0;
    sum.failed += stats.failed ?? 0;
  }
  return sum;
}

/** Run history with paging; a run opens its per-object counts and log (translated message keys). */
export function RunsTab({ connectionId }: { connectionId: string }) {
  const { t, tx, language } = useSyncT();
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const runs = useSyncRuns(connectionId, page);
  const items = runs.data?.items ?? [];
  if (runs.isPending) return <Skeleton className="mt-4 h-48 rounded-lg" />;
  if (items.length === 0) return <EmptyState className="mt-4" title={t('sync.runs.empty')} />;
  return (
    <div className="flex flex-col gap-4 pt-4">
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('sync.runs.started')}</TableHeaderCell>
            <TableHeaderCell>{t('sync.runs.trigger')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
            <TableHeaderCell>{t('sync.runs.duration')}</TableHeaderCell>
            <TableHeaderCell className="text-end">{tx('sync.counts.read')}</TableHeaderCell>
            <TableHeaderCell className="text-end">{tx('sync.counts.created')}</TableHeaderCell>
            <TableHeaderCell className="text-end">{tx('sync.counts.updated')}</TableHeaderCell>
            <TableHeaderCell className="text-end">{tx('sync.counts.failed')}</TableHeaderCell>
            <TableHeaderCell className="text-end">{t('sync.runs.apiCalls')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('sync.runs.details')}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((run) => {
            const sum = totals(run);
            return (
              <TableRow key={run.id}>
                <TableCell>{formatDateTime(run.startedAt, language)}</TableCell>
                <TableCell>
                  {tx(`sync.triggers.${run.trigger}`, { defaultValue: run.trigger })}
                  {run.full && ` · ${t('sync.runs.full')}`}
                </TableCell>
                <TableCell>
                  <div className="flex flex-col gap-1">
                    <StatusChip tone={runTone[run.status] ?? 'neutral'}>
                      {tx(`sync.runStatus.${run.status}`, { defaultValue: run.status })}
                    </StatusChip>
                    {run.messageKey && (
                      <span className="text-xs text-muted-foreground">
                        <ServerMessage messageKey={run.messageKey} params={run.params} />
                      </span>
                    )}
                  </div>
                </TableCell>
                <TableCell>{formatDuration(run.durationSeconds, language, tx)}</TableCell>
                <TableCell className="text-end tabular-nums">{formatNumber(sum.read, language)}</TableCell>
                <TableCell className="text-end tabular-nums">{formatNumber(sum.created, language)}</TableCell>
                <TableCell className="text-end tabular-nums">{formatNumber(sum.updated, language)}</TableCell>
                <TableCell className="text-end tabular-nums">{formatNumber(sum.failed, language)}</TableCell>
                <TableCell className="text-end tabular-nums">
                  {formatNumber(run.apiCalls, language)}
                </TableCell>
                <TableCell className="text-end">
                  <Button variant="ghost" size="sm" onClick={() => setOpen(run.id)}>
                    {t('sync.runs.details')}
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <Pagination
        page={page}
        totalPages={runs.data?.totalPages ?? 1}
        totalCount={runs.data?.totalCount ?? 0}
        onPageChange={setPage}
      />
      {open && <RunDialog connectionId={connectionId} runId={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function RunDialog({
  connectionId,
  runId,
  onClose,
}: {
  connectionId: string;
  runId: string;
  onClose: () => void;
}) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const detail = useSyncRun(connectionId, runId);
  const run = detail.data?.run;
  return (
    <Dialog open onOpenChange={(isOpen) => !isOpen && onClose()}>
      <DialogContent
        className="max-w-4xl"
        title={
          run
            ? t('sync.runs.title', { date: formatDateTime(run.startedAt, language) })
            : t('sync.runs.details')
        }
        footer={
          <DialogClose asChild>
            <Button variant="outline">{tc('actions.close')}</Button>
          </DialogClose>
        }
      >
        {!detail.data || !run ? (
          <Skeleton className="h-48 rounded-lg" />
        ) : (
          <div className="flex flex-col gap-4" data-testid="run-detail">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('sync.objects.object')}</TableHeaderCell>
                  {(
                    ['read', 'created', 'updated', 'unchanged', 'deleted', 'failed', 'conflicts'] as const
                  ).map((k) => (
                    <TableHeaderCell key={k} className="text-end">
                      {tx(`sync.counts.${k}`)}
                    </TableHeaderCell>
                  ))}
                  <TableHeaderCell>{t('sync.runs.api')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {Object.entries(run.objects).map(([key, stats]) => (
                  <TableRow key={key}>
                    <TableCell>
                      <Code>{key}</Code>
                    </TableCell>
                    {(
                      ['read', 'created', 'updated', 'unchanged', 'deleted', 'failed', 'conflicts'] as const
                    ).map((k) => (
                      <TableCell key={k} className="text-end tabular-nums">
                        {formatNumber(stats[k] ?? 0, language)}
                      </TableCell>
                    ))}
                    <TableCell>{stats.api ? <Code>{stats.api.toUpperCase()}</Code> : '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <section aria-labelledby="run-log-title" className="flex flex-col gap-2">
              <h3 id="run-log-title" className="font-medium">
                {t('sync.runs.log')}
              </h3>
              {detail.data.log.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t('sync.runs.noLog')}</p>
              ) : (
                <ScrollRegion className="max-h-80 overflow-auto rounded-md border" label={t('sync.runs.log')}>
                  <ol className="flex flex-col divide-y text-sm" data-testid="run-log">
                    {detail.data.log.map((line, i) => (
                      <li key={i} className="flex flex-wrap items-start gap-2 px-3 py-1.5">
                        <span className="text-xs text-muted-foreground tabular-nums">
                          {formatDateTime(line.at, language)}
                        </span>
                        <StatusChip
                          tone={severityTone[line.level === 'warn' ? 'warning' : line.level] ?? 'neutral'}
                        >
                          {tx(`sync.severity.${line.level === 'warn' ? 'warning' : line.level}`, {
                            defaultValue: line.level,
                          })}
                        </StatusChip>
                        {line.objectKey && <Code>{line.objectKey}</Code>}
                        <span className="min-w-0 flex-1">
                          <ServerMessage messageKey={line.messageKey} params={line.params} />
                        </span>
                      </li>
                    ))}
                  </ol>
                </ScrollRegion>
              )}
            </section>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
