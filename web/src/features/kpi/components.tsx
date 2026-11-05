import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { StatusChip } from '@/ui';
import { ragFill, ragTone, useKpiOptions } from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, periods, RAG…); the message-key test proves they all exist. */
export function useKpiT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('kpi');
  return { k: t as unknown as Dyn, language: i18n.language };
}

export function usePercent() {
  const { i18n } = useTranslation();
  return (ratio: number | null | undefined) =>
    ratio == null ? '—' : `${formatNumber(Math.round(ratio * 1000) / 10, i18n.language)} %`;
}

export function useNumber() {
  const { i18n } = useTranslation();
  return (value: number | null | undefined) => (value == null ? '—' : formatNumber(value, i18n.language));
}

export function RagChip({ rag }: { rag: string }) {
  const { k } = useKpiT();
  return (
    <StatusChip tone={ragTone[rag] ?? 'neutral'}>
      <span className={cn('me-1 inline-block size-2 rounded-full', ragFill[rag])} aria-hidden />
      {k(`rag.${rag}`)}
    </StatusChip>
  );
}

/** Capped performance as the main figure with the actual beside it (SPEC K5): "100 % (actual 125 %)"; "—" when it cannot be calculated. */
export function PerformanceValue({
  capped,
  actual,
  className,
}: {
  capped: number | null | undefined;
  actual?: number | null | undefined;
  className?: string;
}) {
  const { t } = useTranslation('kpi');
  const pct = usePercent();
  if (capped == null)
    return (
      <span className={cn('text-muted-foreground', className)} title={t('performance.cannotCalculate')}>
        —<span className="sr-only">{t('performance.cannotCalculate')}</span>
      </span>
    );
  return (
    <span className={cn('tabular-nums', className)} data-testid="kpi-performance">
      <bdi className="font-semibold">{pct(capped)}</bdi>
      {actual != null && actual !== capped && (
        <span className="ms-1 text-xs text-muted-foreground">
          ({t('performance.actual', { value: pct(actual) })})
        </span>
      )}
    </span>
  );
}

/** A horizontal score bar (capped performance 0–100 %) coloured by its band. */
export function ScoreBar({
  score,
  rag,
  className,
}: {
  score: number | null | undefined;
  rag: string;
  className?: string;
}) {
  return (
    <span className={cn('flex h-2.5 w-full overflow-hidden rounded bg-muted', className)} aria-hidden>
      <span
        className={cn('h-full', ragFill[rag])}
        style={{ inlineSize: `${Math.max(0, Math.min(1, score ?? 0)) * 100}%` }}
      />
    </span>
  );
}

const navItems = [
  { to: '/kpi', key: 'dashboard', exact: true },
  { to: '/kpi/cards', key: 'cards' },
  { to: '/kpi/pending', key: 'pending' },
  { to: '/kpi/approvals', key: 'approvals' },
  { to: '/kpi/scorecard', key: 'scorecard' },
  { to: '/kpi/strategy-map', key: 'strategyMap' },
  { to: '/kpi/reports', key: 'reports' },
  { to: '/kpi/new-year', key: 'newYear', plans: true },
] as const;

/** Secondary navigation of Performance Management. */
export function KpiNav() {
  const { t } = useTranslation('kpi');
  const options = useKpiOptions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  return (
    <nav
      aria-label={t('nav.label')}
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="kpi-nav"
    >
      {navItems
        .filter((item) => !('plans' in item) || options.data?.canManagePlans)
        .map((item) => {
          const active = 'exact' in item ? pathname === item.to : pathname.startsWith(item.to);
          return (
            <Link
              key={item.key}
              to={item.to}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm hover:bg-muted',
                active ? 'bg-muted font-semibold text-primary' : 'text-muted-foreground',
              )}
              aria-current={active ? 'page' : undefined}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
    </nav>
  );
}

/** Layout of every KPI page: secondary navigation above the page. */
export function KpiPage({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <KpiNav />
      {children}
    </div>
  );
}

/** Year chooser (current year ± a few). */
export function yearOptions(around: number, before = 3, after = 2): { value: string; label: string }[] {
  const years: number[] = [];
  for (let y = around + after; y >= around - before; y--) years.push(y);
  return years.map((y) => ({ value: String(y), label: String(y) }));
}
