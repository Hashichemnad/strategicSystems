import { Link } from '@tanstack/react-router';
import { ArrowLeft, ShieldAlert, ShieldCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, formatNumber } from '@/lib/format';
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
  useToast,
} from '@/ui';
import { useVerification, useVerifyChain } from './api';

/** Audit chain verification (CLAUDE.md §8): recomputes the hash chain and shows every run; a broken chain is flagged here. */
export function VerificationPage() {
  const { t, i18n } = useTranslation('audit');
  const { t: tc } = useTranslation();
  const status = useVerification();
  const verify = useVerifyChain();
  const toast = useToast();

  if (status.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (status.isError) return <StatusBanner tone="danger" title={errorMessage(tc, status.error)} />;
  const data = status.data;
  const latest = data.runs[0];

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/audit"
        search={{ page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('log.title')}
      </Link>
      <PageHeader
        title={t('verification.title')}
        description={t('verification.description')}
        actions={
          <Button
            loading={verify.isPending}
            onClick={() =>
              verify.mutate('full', {
                onSuccess: (run) =>
                  toast(
                    run.ok
                      ? { title: t('verification.okToast') }
                      : { tone: 'error', title: t('verification.brokenToast') },
                  ),
                onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
              })
            }
          >
            <ShieldCheck aria-hidden />
            {t('verification.verifyNow')}
          </Button>
        }
      />

      {latest &&
        (latest.ok ? (
          <StatusBanner
            tone="success"
            title={t('verification.intact')}
            description={t('verification.intactDetail', {
              count: latest.entriesChecked,
              entries: formatNumber(latest.entriesChecked, i18n.language),
              date: formatDateTime(latest.finishedAt, i18n.language),
            })}
          />
        ) : (
          <StatusBanner
            tone="danger"
            title={t('verification.broken')}
            description={t('verification.brokenDetail', {
              seq: latest.brokenSeq ?? 0,
              reason: t(`messages.${(latest.messageKey ?? '').replace('audit.verify.', '')}`, {
                defaultValue: latest.messageKey ?? '',
              }),
            })}
          />
        ))}

      <dl className="grid gap-4 sm:grid-cols-3">
        <Stat label={t('verification.totalEntries')} value={formatNumber(data.totalEntries, i18n.language)} />
        <Stat
          label={t('verification.checkpoint')}
          value={data.checkpointSeq !== null ? formatNumber(data.checkpointSeq, i18n.language) : '—'}
        />
        <Stat
          label={t('verification.checkpointAt')}
          value={data.checkpointAt ? formatDateTime(data.checkpointAt, i18n.language) : '—'}
        />
      </dl>

      <div>
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('verification.when')}</TableHeaderCell>
              <TableHeaderCell>{t('verification.mode')}</TableHeaderCell>
              <TableHeaderCell>{t('verification.result')}</TableHeaderCell>
              <TableHeaderCell>{t('verification.checked')}</TableHeaderCell>
              <TableHeaderCell>{t('verification.detail')}</TableHeaderCell>
              <TableHeaderCell>{t('log.channel')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.runs.map((run) => (
              <TableRow key={run.id}>
                <TableCell className="whitespace-nowrap">
                  {formatDateTime(run.startedAt, i18n.language)}
                </TableCell>
                <TableCell>{t(`verification.modes.${run.mode as 'full'}`)}</TableCell>
                <TableCell>
                  {run.ok ? (
                    <StatusChip tone="success">
                      <ShieldCheck className="me-1 size-3" aria-hidden />
                      {t('verification.ok')}
                    </StatusChip>
                  ) : (
                    <StatusChip tone="danger">
                      <ShieldAlert className="me-1 size-3" aria-hidden />
                      {t('verification.failed')}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell>{formatNumber(run.entriesChecked, i18n.language)}</TableCell>
                <TableCell>
                  {run.ok
                    ? t('verification.upTo', { seq: run.lastSeq })
                    : t('verification.brokenAt', {
                        seq: run.brokenSeq ?? 0,
                        reason: t(`messages.${(run.messageKey ?? '').replace('audit.verify.', '')}`, {
                          defaultValue: run.messageKey ?? '',
                        }),
                      })}
                </TableCell>
                <TableCell>{t(`channels.${run.channel as 'ui'}`, { defaultValue: run.channel })}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
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
