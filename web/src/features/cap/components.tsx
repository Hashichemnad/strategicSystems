import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, Dialog, DialogClose, DialogContent, StatusChip, useToast } from '@/ui';
import {
  actionTone,
  priorityTone,
  remarkTone,
  useCapMutation,
  useCapOptions,
  validationTone,
  type CapPerson,
  type CapUnit,
} from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, views, flags…); the message-key test proves they all exist. */
export function useCapT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('cap');
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

/** Names in the user's language with fallback to the other language. */
export function useNames() {
  const { i18n } = useTranslation();
  return {
    unit: (u: CapUnit | null | undefined) =>
      u ? pickText(i18n.language, u.nameAr, u.nameEn) || u.code || '—' : '—',
    person: (p: CapPerson | null | undefined) =>
      p ? pickText(i18n.language, p.nameAr, p.nameEn) || '—' : '—',
    text: (ar: string | null | undefined, en: string | null | undefined) => pickText(i18n.language, ar, en),
  };
}

/** Labels of the CAP lookups (source type, remark type, criterion, action type) — configuration, never hard-coded. */
export function useCapLabels() {
  const options = useCapOptions();
  const { language } = useCapT();
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

export const lookupTypes = {
  sourceType: 'cap.source-type',
  remarkType: 'cap.remark-type',
  criterion: 'cap.criterion',
  actionType: 'cap.action-type',
} as const;

export function RemarkStatusChip({ status }: { status: string }) {
  const { k } = useCapT();
  return (
    <StatusChip tone={remarkTone[status] ?? 'neutral'}>
      <span data-testid="cap-remark-status">{k(`remarkStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function ActionStatusChip({ status }: { status: string }) {
  const { k } = useCapT();
  return (
    <StatusChip tone={actionTone[status] ?? 'neutral'}>
      <span data-testid="cap-action-status">{k(`actionStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function ValidationChip({ status }: { status: string }) {
  const { k } = useCapT();
  return (
    <StatusChip tone={validationTone[status] ?? 'neutral'}>
      <span data-testid="cap-validation-status">{k(`validationStatus.${status}`)}</span>
    </StatusChip>
  );
}

export function PriorityChip({ priority }: { priority: string }) {
  const { k } = useCapT();
  return <StatusChip tone={priorityTone[priority] ?? 'neutral'}>{k(`priority.${priority}`)}</StatusChip>;
}

/** Achievement bar (ratio 0..1). */
export function PctBar({ value, late }: { value: number; late?: boolean }) {
  const pct = usePct();
  const width = Math.max(0, Math.min(100, Math.round(value * 100)));
  return (
    <div className="flex min-w-28 items-center gap-2" data-testid="cap-pct">
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
  { to: '/cap', key: 'dashboard', exact: true },
  { to: '/cap/sources', key: 'sources' },
  { to: '/cap/remarks', key: 'remarks' },
  { to: '/cap/my-actions', key: 'actions' },
  { to: '/cap/suggestions', key: 'suggestions' },
] as const;

/** Secondary navigation of the Corrective Action Plan. */
export function CapNav() {
  const { t } = useTranslation('cap');
  const options = useCapOptions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  return (
    <nav
      aria-label={t('nav.label')}
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="cap-nav"
    >
      {navItems
        .filter(
          (item) =>
            (item.key !== 'dashboard' || options.data?.canDashboard !== false) &&
            (item.key !== 'suggestions' || options.data?.canOffice !== false),
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
              data-testid={`cap-nav-${item.key}`}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
    </nav>
  );
}

export function CapPageLayout({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <CapNav />
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
  const mutation = useCapMutation(run);
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

/** Plain mutation with an error toast (no dialog). */
export function useToastMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useCapMutation(run);
  return {
    busy: mutation.isPending,
    run: (input: TInput, after?: (output: TOutput) => void) =>
      mutation.mutate(input, {
        onSuccess: (output) => after?.(output),
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }),
  };
}
