import { Link } from '@tanstack/react-router';
import { ShieldAlert } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDate, pickText } from '@/lib/format';
import { StatusChip } from '@/ui';

type MyWorkItem = components['schemas']['MyWorkItem'];

/** My Work → Risks (SPEC §0.4): my risks due for review / reassessment / returned, and my overdue treatment actions. */
export function MyRiskCard({ items }: { items: MyWorkItem[] }) {
  const { t, i18n } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const count = (key: string) => items.filter((i) => i.messageKey === `risk.myWork.${key}`).length;
  const reviews = count('reviewDue') + count('reviewOverdue') + count('reassessment');
  const overdue = count('reviewOverdue');
  const returned = count('returned');
  const actions = count('actionOverdue');
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('myWork.title')}
      data-testid="my-risk-card"
    >
      <header className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold">
          <ShieldAlert className="size-5 text-primary" aria-hidden />
          {t('myWork.title')}
        </h2>
        <Link to="/risk/my-actions" className="text-sm text-primary hover:underline">
          {t('myWork.all')}
        </Link>
      </header>
      <div className="flex flex-wrap gap-2 text-sm">
        <StatusChip tone={reviews > 0 ? 'info' : 'neutral'}>
          {t('myWork.counts.reviews')}: {reviews}
        </StatusChip>
        <StatusChip tone={overdue > 0 ? 'danger' : 'neutral'}>
          {t('myWork.counts.overdueReviews')}: {overdue}
        </StatusChip>
        <StatusChip tone={returned > 0 ? 'warning' : 'neutral'}>
          {t('myWork.counts.returned')}: {returned}
        </StatusChip>
        <StatusChip tone={actions > 0 ? 'danger' : 'neutral'}>
          {t('myWork.counts.overdueActions')}: {actions}
        </StatusChip>
      </div>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('myWork.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y">
          {items.slice(0, 8).map((item) => (
            <li key={`${item.resourceType}:${item.resourceId}`} className="flex flex-col gap-0.5 py-3">
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
