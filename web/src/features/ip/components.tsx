import { Link, useRouterState } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useLookupValues } from '@/features/settings/lookups';
import { cn } from '@/lib/cn';
import { formatDate, pickText } from '@/lib/format';
import { StatusChip } from '@/ui';
import { statusTone } from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, sources…); the message-key test proves they all exist. */
export function useIpT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('ip');
  return { k: t as unknown as Dyn, language: i18n.language };
}

/** Label of a lookup code (property type, protection type, contributor role) in the user's language. */
export function useLookupLabel(type: string): (code: string | null | undefined) => string {
  const { i18n } = useTranslation();
  const values = useLookupValues(type).data ?? [];
  return (code) => {
    if (!code) return '—';
    const value = values.find((v) => v.code === code);
    return (value && pickText(i18n.language, value.labelAr, value.labelEn)) || code;
  };
}

export function useDate() {
  const { i18n } = useTranslation();
  return (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : '—');
}

export function IpStatusChip({ status }: { status: string }) {
  const { k } = useIpT();
  return (
    <StatusChip tone={statusTone[status] ?? 'neutral'}>
      <span data-testid="ip-status" data-status={status}>
        {k(`status.${status}`)}
      </span>
    </StatusChip>
  );
}

/**
 * A user-facing sentence for an action error: the translated field errors of a validation problem when there are
 * any (shares, Emirates ID, dates…), otherwise the problem's own message.
 */
export function actionError(tc: TFunction, error: unknown): string {
  if (error instanceof ApiError && error.problem.errors?.length) {
    const messages = error.problem.errors.map((e) =>
      translateMessageKey(tc, e.messageKey, e.params as Record<string, unknown> | null | undefined),
    );
    return [...new Set(messages)].join(' ');
  }
  return errorMessage(tc, error);
}

const navItems = [
  { to: '/ip', key: 'register', exact: true },
  { to: '/ip/dashboard', key: 'dashboard', permission: 'ip.dashboard.read' },
] as const;

/** Secondary navigation of the IP Registry. */
export function IpNav() {
  const { t } = useTranslation('ip');
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const canDashboard = useCan('ip.dashboard.read');
  return (
    <nav
      aria-label={t('nav.label')}
      data-module-nav
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="ip-nav"
    >
      {navItems
        .filter((item) => !('permission' in item) || canDashboard)
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

/** Layout of every IP page: secondary navigation above the page. */
export function IpPage({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <IpNav />
      {children}
    </div>
  );
}

/** A labelled value in a details grid. */
export function Detail({ label, children, wide }: { label: ReactNode; children: ReactNode; wide?: boolean }) {
  return (
    <div className={cn('flex flex-col gap-0.5', wide && 'sm:col-span-2')}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm break-words">{children}</dd>
    </div>
  );
}
