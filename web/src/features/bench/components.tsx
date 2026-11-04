import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import { StatusChip, useToast } from '@/ui';
import { useBenchMutation, useBenchOptions } from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes; the message-key test proves they all exist. */
export function useBenchT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('bench');
  return { k: t as unknown as Dyn, language: i18n.language };
}

/** Labels of the benchmarking lookups (comparison type, purpose, sector) — configuration. */
export function useBenchLabels() {
  const options = useBenchOptions();
  const { language } = useBenchT();
  return {
    lookup: (type: string, code: string | null | undefined) => {
      if (!code) return '—';
      const o = options.data?.lookups[type]?.find((x) => x.code === code);
      return o ? pickText(language, o.labelAr, o.labelEn) || code : code;
    },
    lookupOptions: (type: string) =>
      (options.data?.lookups[type] ?? []).map((o) => ({
        value: o.code,
        label: pickText(language, o.labelAr, o.labelEn) || o.code,
      })),
  };
}

const stageTone: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  plan: 'neutral',
  search: 'info',
  acquire: 'info',
  analysis: 'info',
  completed: 'success',
};

export function StageChip({ stage, status }: { stage: string; status: string }) {
  const { k } = useBenchT();
  if (status === 'cancelled') return <StatusChip tone="neutral">{k('status.cancelled')}</StatusChip>;
  return (
    <StatusChip tone={stageTone[stage] ?? 'neutral'}>
      <span data-testid="bench-stage">{k(`stage.${stage}`)}</span>
    </StatusChip>
  );
}

const navItems = [
  { to: '/bench', key: 'dashboard', exact: true },
  { to: '/bench/plan', key: 'plan' },
  { to: '/bench/studies', key: 'studies' },
  { to: '/bench/comparators', key: 'comparators' },
  { to: '/bench/practices', key: 'practices' },
] as const;

export function BenchNav() {
  const { t } = useTranslation('bench');
  const options = useBenchOptions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  return (
    <nav
      aria-label={t('nav.label')}
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="bench-nav"
    >
      {navItems
        .filter((item) => item.key !== 'dashboard' || options.data?.canDashboard !== false)
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
              data-testid={`bench-nav-${item.key}`}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
    </nav>
  );
}

export function BenchPageLayout({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <BenchNav />
      {children}
    </div>
  );
}

/** Mutation with an error toast; `onDone` runs after success (e.g. close a dialog). */
export function useBenchAction<TInput, TOutput>(
  run: (input: TInput) => Promise<TOutput>,
  onDone?: () => void,
) {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useBenchMutation(run);
  return {
    busy: mutation.isPending,
    run: (input: TInput, after?: (output: TOutput) => void) =>
      mutation.mutate(input, {
        onSuccess: (output) => {
          onDone?.();
          after?.(output);
        },
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }),
  };
}
