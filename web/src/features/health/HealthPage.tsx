import { useQuery } from '@tanstack/react-query';
import { RefreshCw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
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
} from '@/ui';
import { formatParams, messagePath, statusTone } from './healthFormat';

type Check = components['schemas']['AdminHealthCheck'];
type Item = components['schemas']['AdminHealthItem'];

const groups = ['platform', 'jobs', 'integrations', 'storage', 'security'] as const;

/**
 * Settings → Health (ADR 0151): every check of the installation — database and migrations, scheduled jobs and the message queue, e-mail,
 * PDF, AI and OCR, storage and disk, antivirus, audit chain, keys and certificates, version. Read-only; refreshed every minute.
 */
export function HealthPage() {
  const { t, i18n } = useTranslation('health');
  const { t: tc } = useTranslation();
  const health = useQuery({
    queryKey: ['admin-health'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/health', { signal })),
    refetchInterval: 60_000,
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        meta={
          health.data && (
            <StatusChip tone={statusTone[health.data.status] ?? 'neutral'}>
              {t(`overall.${health.data.status}`, { defaultValue: health.data.status })}
            </StatusChip>
          )
        }
        actions={
          <Button variant="outline" onClick={() => void health.refetch()} disabled={health.isFetching}>
            <RefreshCw aria-hidden className={health.isFetching ? 'animate-spin' : undefined} />
            {t('refresh')}
          </Button>
        }
      />
      {health.isPending ? (
        <div aria-busy className="flex flex-col gap-3">
          <Skeleton className="h-24" />
          <Skeleton className="h-64" />
        </div>
      ) : health.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, health.error)} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground" data-testid="health-checked-at">
            {t('checkedAt', { at: formatDateTime(health.data.checkedAt, i18n.language) })}
          </p>
          {groups.map((group) => {
            const checks = health.data.checks.filter((c) => c.group === group);
            if (checks.length === 0) return null;
            return (
              <section key={group} className="flex flex-col gap-3" aria-labelledby={`health-${group}`}>
                <h2 id={`health-${group}`} className="text-lg font-semibold">
                  {t(`groups.${group}`)}
                </h2>
                <div className="grid gap-3 lg:grid-cols-2">
                  {checks.map((check) => (
                    <CheckCard key={check.code} check={check} />
                  ))}
                </div>
              </section>
            );
          })}
        </>
      )}
    </div>
  );
}

function CheckCard({ check }: { check: Check }) {
  const { t, i18n } = useTranslation('health');
  const wide = check.code === 'scheduler' || check.code === 'migrations';
  return (
    <article
      className={`flex flex-col gap-3 rounded-lg border bg-card p-4 ${wide ? 'lg:col-span-2' : ''}`}
      data-testid={`health-check-${check.code}`}
      aria-labelledby={`health-check-${check.code}-title`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id={`health-check-${check.code}-title`} className="font-medium">
          {t(`checks.${check.code}`, { defaultValue: check.code })}
        </h3>
        <StatusChip tone={statusTone[check.status] ?? 'neutral'}>
          {t(`statuses.${check.status}`, { defaultValue: check.status })}
        </StatusChip>
      </div>
      <p className="text-sm">
        {t(messagePath(check.messageKey), {
          ...formatParams(check.params, i18n.language),
          defaultValue: check.messageKey,
        })}
      </p>
      {check.items && check.items.length > 0 && (
        <div className="max-h-96 overflow-auto rounded-md border">
          {check.code === 'scheduler' ? <JobTable items={check.items} /> : <ItemTable items={check.items} />}
        </div>
      )}
    </article>
  );
}

function ItemTable({ items }: { items: Item[] }) {
  const { t, i18n } = useTranslation('health');
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('item')}</TableHeaderCell>
          <TableHeaderCell>{t('status')}</TableHeaderCell>
          <TableHeaderCell>{t('details')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((item) => (
          <TableRow key={item.code}>
            <TableCell>
              {item.labelAr || item.labelEn
                ? pickText(i18n.language, item.labelAr, item.labelEn)
                : t(`items.${item.code}`, { defaultValue: item.code })}
            </TableCell>
            <TableCell>
              <StatusChip tone={statusTone[item.status] ?? 'neutral'}>
                {t(`statuses.${item.status}`, { defaultValue: item.status })}
              </StatusChip>
            </TableCell>
            <TableCell className="text-sm">
              {t(messagePath(item.messageKey), {
                ...formatParams(item.params, i18n.language),
                defaultValue: item.messageKey,
              })}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function JobTable({ items }: { items: Item[] }) {
  const { t, i18n } = useTranslation('health');
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('job.name')}</TableHeaderCell>
          <TableHeaderCell>{t('status')}</TableHeaderCell>
          <TableHeaderCell>{t('job.lastRun')}</TableHeaderCell>
          <TableHeaderCell>{t('job.nextRun')}</TableHeaderCell>
          <TableHeaderCell>{t('job.failures')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((item) => {
          const params = item.params ?? {};
          return (
            <TableRow key={item.code} data-testid="health-job-row">
              <TableCell>
                <div className="font-medium">{pickText(i18n.language, item.labelAr, item.labelEn)}</div>
                <div className="font-mono text-xs text-muted-foreground" dir="ltr">
                  {item.code}
                </div>
              </TableCell>
              <TableCell>
                <div className="flex flex-col gap-1">
                  <StatusChip tone={statusTone[item.status] ?? 'neutral'}>
                    {t(`statuses.${item.status}`, { defaultValue: item.status })}
                  </StatusChip>
                  <span className="text-xs text-muted-foreground">
                    {t(messagePath(item.messageKey), {
                      ...formatParams(params, i18n.language),
                      defaultValue: item.messageKey,
                    })}
                  </span>
                </div>
              </TableCell>
              <TableCell className="text-sm whitespace-nowrap">
                {params.lastRunAt ? formatDateTime(params.lastRunAt, i18n.language) : t('job.never')}
              </TableCell>
              <TableCell className="text-sm whitespace-nowrap">
                {params.nextRunAt ? formatDateTime(params.nextRunAt, i18n.language) : '—'}
              </TableCell>
              <TableCell className="text-sm">
                {formatParams(params, i18n.language).failures24h ?? '0'}
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
