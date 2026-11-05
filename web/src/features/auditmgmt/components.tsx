import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, Dialog, DialogClose, DialogContent, StatusChip, useToast } from '@/ui';
import {
  findingTone,
  priorityTone,
  recommendationTone,
  reportTone,
  useAuditMutation,
  useAuditOptions,
  type AuditPerson,
  type AuditUnit,
} from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, views…); the message-key test proves they all exist. */
export function useAuditT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('auditmgmt');
  return { k: t as unknown as Dyn, language: i18n.language };
}

export function usePct() {
  const { i18n } = useTranslation();
  return (ratio: number | null | undefined) =>
    ratio == null ? '—' : `${formatNumber(Math.round(ratio * 1000) / 10, i18n.language)} %`;
}

export function useDate() {
  const { i18n } = useTranslation();
  return (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : '—');
}

export function useNames() {
  const { i18n } = useTranslation();
  return {
    unit: (u: AuditUnit | null | undefined) =>
      u ? pickText(i18n.language, u.nameAr, u.nameEn) || u.code || '—' : '—',
    person: (p: AuditPerson | null | undefined) =>
      p ? pickText(i18n.language, p.nameAr, p.nameEn) || '—' : '—',
    text: (ar: string | null | undefined, en: string | null | undefined) => pickText(i18n.language, ar, en),
  };
}

/** Labels of the audit lookups (report type, priority, finding type, response) — configuration, never hard-coded. */
export function useAuditLabels() {
  const options = useAuditOptions();
  const { language } = useAuditT();
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

export function FindingStatusChip({ status }: { status: string }) {
  const { k } = useAuditT();
  return (
    <StatusChip tone={findingTone[status] ?? 'neutral'}>
      <span data-testid="audit-finding-status">{k(`findingStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function RecommendationStatusChip({ status }: { status: string }) {
  const { k } = useAuditT();
  return (
    <StatusChip tone={recommendationTone[status] ?? 'neutral'}>
      <span data-testid="audit-recommendation-status">{k(`recommendationStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function ReportStatusChip({ status }: { status: string }) {
  const { k } = useAuditT();
  return (
    <StatusChip tone={reportTone[status] ?? 'neutral'}>
      <span data-testid="audit-report-status">{k(`reportStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function PriorityChip({ priority }: { priority: string }) {
  const labels = useAuditLabels();
  return (
    <StatusChip tone={priorityTone[priority] ?? 'neutral'}>
      {labels.lookup('auditmgmt.priority', priority)}
    </StatusChip>
  );
}

/** Completion bar (ratio 0..1). */
export function PctBar({ value, late }: { value: number; late?: boolean }) {
  const pct = usePct();
  const width = Math.max(0, Math.min(100, Math.round(value * 100)));
  return (
    <div className="flex min-w-28 items-center gap-2" data-testid="audit-pct">
      <div className="h-2 flex-1 overflow-hidden rounded-full bg-muted" aria-hidden>
        <div
          className={cn('h-full rounded-full', late ? 'bg-danger' : 'bg-primary')}
          style={{ width: `${width}%` }}
        />
      </div>
      <bdi className="text-xs tabular-nums">{pct(value)}</bdi>
    </div>
  );
}

export function Rows({ rows, className }: { rows: [string, ReactNode][]; className?: string }) {
  return (
    <dl className={cn('grid gap-4 rounded-lg border bg-card p-6 md:grid-cols-2', className)}>
      {rows.map(([label, value]) => (
        <div key={label}>
          <dt className="text-sm text-muted-foreground">{label}</dt>
          <dd className="mt-1 whitespace-pre-line">{value || '—'}</dd>
        </div>
      ))}
    </dl>
  );
}

export function Section({
  title,
  actions,
  children,
  testId,
  className,
}: {
  title: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  testId?: string;
  className?: string;
}) {
  return (
    <section
      className={cn('flex flex-col gap-3 rounded-lg border bg-card p-5', className)}
      data-testid={testId}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{title}</h2>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </header>
      {children}
    </section>
  );
}

export function Tile({
  label,
  value,
  tone,
  testId,
}: {
  label: string;
  value: ReactNode;
  tone?: string;
  testId?: string;
}) {
  return (
    <div className="rounded-lg border bg-card p-4" data-testid={testId}>
      <div className="text-sm text-muted-foreground">{label}</div>
      <div className={cn('mt-1 text-2xl font-semibold tabular-nums', tone)}>{value}</div>
    </div>
  );
}

const navItems = [
  {
    to: '/audit-management',
    key: 'dashboard',
    exact: true,
    office: false,
    dashboard: true,
    lifecycle: false,
  },
  {
    to: '/audit-management/my-department',
    key: 'department',
    office: false,
    dashboard: false,
    lifecycle: false,
  },
  { to: '/audit-management/reports', key: 'reports', office: false, dashboard: false, lifecycle: false },
  { to: '/audit-management/findings', key: 'findings', office: false, dashboard: false, lifecycle: false },
  { to: '/audit-management/calendar', key: 'calendar', office: true, dashboard: false, lifecycle: false },
  { to: '/audit-management/universe', key: 'universe', office: true, dashboard: false, lifecycle: true },
  { to: '/audit-management/plans', key: 'plans', office: false, dashboard: false, lifecycle: true },
] as const;

/** Secondary navigation of Audit Management; Layer 2 entries only while the switch is on. */
export function AuditNav() {
  const { t } = useTranslation('auditmgmt');
  const options = useAuditOptions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const o = options.data;
  return (
    <nav
      aria-label={t('nav.label')}
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="audit-nav"
    >
      {navItems
        .filter(
          (item) =>
            (!item.dashboard || o?.canDashboard) &&
            (!item.office || o?.isOffice) &&
            (!item.lifecycle || (o?.lifecycleEnabled && (o.canLifecycle || item.key === 'plans'))) &&
            (item.key !== 'department' || !o?.isOffice),
        )
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
              data-testid={`audit-nav-${item.key}`}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
    </nav>
  );
}

export function AuditPageLayout({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <AuditNav />
      {children}
    </div>
  );
}

export function SimpleDialog({
  title,
  description,
  onClose,
  onSubmit,
  busy,
  submitLabel,
  disabled,
  testId,
  wide,
  children,
}: {
  title: string;
  description?: string;
  onClose: () => void;
  onSubmit: () => void;
  busy: boolean;
  submitLabel: string;
  disabled?: boolean;
  testId: string;
  wide?: boolean;
  children: ReactNode;
}) {
  const { t: tc } = useTranslation();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={title}
        description={description}
        className={wide ? 'max-w-3xl' : undefined}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button onClick={onSubmit} loading={busy} disabled={disabled} data-testid={testId}>
              {submitLabel}
            </Button>
          </>
        }
      >
        <div className="flex max-h-[70vh] flex-col gap-3 overflow-y-auto">{children}</div>
      </DialogContent>
    </Dialog>
  );
}

/** Mutation that closes the dialog on success and toasts the translated problem on failure. */
export function useDialogMutation<TInput, TOutput>(
  run: (input: TInput) => Promise<TOutput>,
  onClose: () => void,
) {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useAuditMutation(run);
  return {
    busy: mutation.isPending,
    run: (input: TInput, after?: (output: TOutput) => void) =>
      mutation.mutate(input, {
        onSuccess: (output) => {
          onClose();
          after?.(output);
        },
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }),
  };
}

export function useToastMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useAuditMutation(run);
  return {
    busy: mutation.isPending,
    run: (input: TInput, after?: (output: TOutput) => void) =>
      mutation.mutate(input, {
        onSuccess: (output) => after?.(output),
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }),
  };
}

export const isoToday = () => new Date().toISOString().slice(0, 10);
