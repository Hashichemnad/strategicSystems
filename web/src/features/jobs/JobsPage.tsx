import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { History, Pause, Play, RotateCw } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { useCan } from '@/features/access/session';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogContent,
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
  useToast,
  type Tone,
} from '@/ui';

type Job = components['schemas']['JobDto'];
type JobRun = components['schemas']['JobRunDto'];

const runTone: Record<string, Tone> = { succeeded: 'success', failed: 'danger', running: 'info' };

/** Settings → Jobs: every scheduled job with its schedule, next and last run, recent runs, "Run now", pause / resume; outbox status. */
export function JobsPage() {
  const { t, i18n } = useTranslation('jobs');
  const { t: tc } = useTranslation();
  const canManage = useCan('settings.job.manage');
  const queryClient = useQueryClient();
  const toast = useToast();
  const [runsOf, setRunsOf] = useState<Job | null>(null);
  const jobs = useQuery({
    queryKey: ['jobs'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/scheduling/jobs', { signal })),
    refetchInterval: 15_000,
  });
  const outbox = useQuery({
    queryKey: ['jobs', 'outbox'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/scheduling/outbox', { signal })),
    refetchInterval: 15_000,
  });
  const act = useMutation({
    mutationFn: async ({ key, action }: { key: string; action: 'run' | 'pause' | 'resume' }) => {
      const params = { params: { path: { key } } };
      if (action === 'run') unwrap(await api.POST('/api/v1/scheduling/jobs/{key}/run', params));
      else if (action === 'pause') unwrap(await api.POST('/api/v1/scheduling/jobs/{key}/pause', params));
      else unwrap(await api.POST('/api/v1/scheduling/jobs/{key}/resume', params));
    },
    onSuccess: (_, { action }) => {
      toast({ title: t(`toast.${action}`) });
      window.setTimeout(() => void queryClient.invalidateQueries({ queryKey: ['jobs'] }), 1500);
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const requeue = useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/v1/scheduling/outbox/{id}/requeue', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['jobs', 'outbox'] }),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />
      {jobs.isPending ? (
        <Skeleton className="h-64" />
      ) : jobs.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, jobs.error)} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('job')}</TableHeaderCell>
                <TableHeaderCell>{t('schedule')}</TableHeaderCell>
                <TableHeaderCell>{t('nextRun')}</TableHeaderCell>
                <TableHeaderCell>{t('lastRun')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {jobs.data.map((job) => (
                <TableRow key={job.key} data-testid="job-row">
                  <TableCell>
                    <div className="font-medium">{pickText(i18n.language, job.nameAr, job.nameEn)}</div>
                    <div className="font-mono text-xs text-muted-foreground" dir="ltr">
                      {job.key}
                    </div>
                  </TableCell>
                  <TableCell>
                    <span className="font-mono text-xs whitespace-nowrap" dir="ltr" title={job.timeZone}>
                      {job.cron}
                    </span>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {job.paused ? (
                      <StatusChip tone="warning">{t('paused')}</StatusChip>
                    ) : job.nextRunAt ? (
                      formatDateTime(job.nextRunAt, i18n.language)
                    ) : (
                      '—'
                    )}
                  </TableCell>
                  <TableCell>
                    {job.lastRun ? (
                      <div className="flex flex-col gap-1">
                        <StatusChip tone={runTone[job.lastRun.status] ?? 'neutral'}>
                          {t(`statuses.${job.lastRun.status as 'succeeded'}`)}
                        </StatusChip>
                        <span className="text-xs text-muted-foreground">
                          {formatDateTime(job.lastRun.firedAt, i18n.language)}
                        </span>
                        {job.failuresLast7Days > 0 && (
                          <span className="text-xs text-danger">
                            {t('failuresCount', { count: job.failuresLast7Days })}
                          </span>
                        )}
                        {job.lastRun.message && (
                          <span
                            className="max-w-xs truncate text-xs text-muted-foreground"
                            title={job.lastRun.message}
                            dir="ltr"
                          >
                            {job.lastRun.message}
                          </span>
                        )}
                      </div>
                    ) : (
                      <span className="text-muted-foreground">{t('never')}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <div className="flex justify-end gap-1">
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={t('runsOf', { name: pickText(i18n.language, job.nameAr, job.nameEn) })}
                        onClick={() => setRunsOf(job)}
                      >
                        <History aria-hidden />
                      </Button>
                      {canManage && (
                        <>
                          <Button
                            variant="outline"
                            size="icon"
                            aria-label={t('runNow')}
                            title={t('runNow')}
                            onClick={() => act.mutate({ key: job.key, action: 'run' })}
                          >
                            <Play aria-hidden />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={job.paused ? t('resume') : t('pause')}
                            onClick={() =>
                              act.mutate({ key: job.key, action: job.paused ? 'resume' : 'pause' })
                            }
                          >
                            {job.paused ? <RotateCw aria-hidden /> : <Pause aria-hidden />}
                          </Button>
                        </>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <section className="flex flex-col gap-3" aria-labelledby="outbox-title">
        <h2 id="outbox-title" className="text-lg font-semibold">
          {t('outbox.title')}
        </h2>
        {outbox.data && (
          <>
            <dl className="grid gap-4 sm:grid-cols-3">
              <Stat label={t('outbox.pending')} value={formatNumber(outbox.data.pending, i18n.language)} />
              <Stat
                label={t('outbox.processed')}
                value={formatNumber(outbox.data.processedLast24Hours, i18n.language)}
              />
              <Stat label={t('outbox.dead')} value={formatNumber(outbox.data.dead, i18n.language)} />
            </dl>
            {outbox.data.deadMessages.length > 0 && (
              <div className="overflow-x-auto rounded-lg border bg-card">
                <Table>
                  <TableHead>
                    <TableRow>
                      <TableHeaderCell>{t('outbox.type')}</TableHeaderCell>
                      <TableHeaderCell>{t('outbox.created')}</TableHeaderCell>
                      <TableHeaderCell>{t('outbox.attempts')}</TableHeaderCell>
                      <TableHeaderCell>{t('outbox.error')}</TableHeaderCell>
                      <TableHeaderCell>
                        <span className="sr-only">{t('outbox.requeue')}</span>
                      </TableHeaderCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {outbox.data.deadMessages.map((message) => (
                      <TableRow key={message.id}>
                        <TableCell className="font-mono text-xs">{message.type}</TableCell>
                        <TableCell>{formatDateTime(message.createdAt, i18n.language)}</TableCell>
                        <TableCell>{formatNumber(message.attempts, i18n.language)}</TableCell>
                        <TableCell className="text-xs" dir="ltr">
                          {message.lastError}
                        </TableCell>
                        <TableCell>
                          {canManage && (
                            <Button variant="outline" size="sm" onClick={() => requeue.mutate(message.id)}>
                              {t('outbox.requeue')}
                            </Button>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
          </>
        )}
      </section>
      <Dialog open={runsOf !== null} onOpenChange={(open) => !open && setRunsOf(null)}>
        {runsOf && <RunsDialog job={runsOf} />}
      </Dialog>
    </div>
  );
}

function RunsDialog({ job }: { job: Job }) {
  const { t, i18n } = useTranslation('jobs');
  const runs = useQuery({
    queryKey: ['jobs', 'runs', job.key],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/scheduling/jobs/{key}/runs', { params: { path: { key: job.key } }, signal }),
      ),
  });
  const duration = (run: JobRun) =>
    run.finishedAt
      ? `${formatNumber((new Date(run.finishedAt).getTime() - new Date(run.firedAt).getTime()) / 1000, i18n.language)} s`
      : '—';
  return (
    <DialogContent
      title={t('runsTitle', { name: pickText(i18n.language, job.nameAr, job.nameEn) })}
      className="max-w-3xl"
    >
      {runs.isPending ? (
        <Skeleton className="h-32" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('started')}</TableHeaderCell>
              <TableHeaderCell>{t('duration')}</TableHeaderCell>
              <TableHeaderCell>{t('status')}</TableHeaderCell>
              <TableHeaderCell>{t('message')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(runs.data ?? []).map((run) => (
              <TableRow key={run.id}>
                <TableCell className="whitespace-nowrap">
                  {formatDateTime(run.firedAt, i18n.language)}
                </TableCell>
                <TableCell>{duration(run)}</TableCell>
                <TableCell>
                  <StatusChip tone={runTone[run.status] ?? 'neutral'}>
                    {t(`statuses.${run.status as 'succeeded'}`)}
                  </StatusChip>
                  {run.triggeredBy && <div className="text-xs text-muted-foreground">{t('manual')}</div>}
                </TableCell>
                <TableCell className="text-xs" dir="ltr">
                  {run.message ?? '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </DialogContent>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="mt-1 text-2xl font-semibold">{value}</dd>
    </div>
  );
}
