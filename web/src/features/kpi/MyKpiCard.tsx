import { Link } from '@tanstack/react-router';
import { Gauge } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDate, pickText } from '@/lib/format';
import { StatusChip } from '@/ui';
import { useKpiPending, useKpiQueue } from './api';

type MyWorkItem = components['schemas']['MyWorkItem'];

/** My Work → Performance (SPEC §0.4 level 1): my KPI results to submit, overdue, returned, and results waiting for my approval. */
export function MyKpiCard({ items }: { items: MyWorkItem[] }) {
  const { t, i18n } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const pending = useKpiPending();
  const queue = useKpiQueue({ mine: true });
  const results = pending.data ?? [];
  const open = results.filter((r) => r.status === 'open').length;
  const overdue = results.filter((r) => r.overdue).length;
  const returned = results.filter((r) => r.status === 'returned').length;
  const awaiting = queue.data?.length;
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('myWork.title')}
      data-testid="my-kpi-card"
    >
      <header className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold">
          <Gauge className="size-5 text-primary" aria-hidden />
          {t('myWork.title')}
        </h2>
        <Link to="/kpi/pending" className="text-sm text-primary hover:underline">
          {t('myWork.all')}
        </Link>
      </header>
      <div className="flex flex-wrap gap-2 text-sm">
        <StatusChip tone="info">
          {t('myWork.counts.open')}: {pending.data ? open : '—'}
        </StatusChip>
        <StatusChip tone={overdue > 0 ? 'danger' : 'neutral'}>
          {t('myWork.counts.overdue')}: {pending.data ? overdue : '—'}
        </StatusChip>
        <StatusChip tone={returned > 0 ? 'warning' : 'neutral'}>
          {t('myWork.counts.returned')}: {pending.data ? returned : '—'}
        </StatusChip>
        <StatusChip tone={awaiting ? 'warning' : 'neutral'}>
          <Link to="/kpi/approvals">
            {t('myWork.counts.awaiting')}: {awaiting ?? '—'}
          </Link>
        </StatusChip>
      </div>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('myWork.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y">
          {items.slice(0, 8).map((item) => (
            <li key={item.resourceId} className="flex flex-col gap-0.5 py-3">
              <Link to={item.link as '/'} className="font-medium text-primary hover:underline">
                {[item.number, pickText(i18n.language, item.titleAr, item.titleEn)]
                  .filter(Boolean)
                  .join(' · ')}
              </Link>
              <span className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                {translateMessageKey(tc, item.messageKey)}
                {item.dueDate && <span>· {formatDate(item.dueDate, i18n.language)}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
