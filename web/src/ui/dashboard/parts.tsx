import { Link } from '@tanstack/react-router';
import { ChevronRight, Download, FileSpreadsheet, FileText, Image as ImageIcon } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatDateTime, formatNumber } from '@/lib/format';
import { Button } from '../button';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '../dropdown-menu';
import type { ChartHandle } from '../charts/types';

export interface SectionProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  testId?: string;
  className?: string;
}

/** A dashboard / record card with a heading (named region) and optional actions. */
export function Section({ title, description, actions, children, testId, className }: SectionProps) {
  const id = useId();
  return (
    <section
      className={cn('flex min-w-0 flex-col gap-3 rounded-lg border bg-card p-5', className)}
      aria-labelledby={`${id}-title`}
      data-testid={testId}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <h2 id={`${id}-title`} className="font-semibold">
            {title}
          </h2>
          {description && <p className="text-xs text-muted-foreground">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </header>
      {children}
    </section>
  );
}

export type ProgressTone = 'primary' | 'info' | 'success' | 'warning' | 'danger';

const progressFill: Record<ProgressTone, string> = {
  primary: 'bg-primary',
  info: 'bg-info',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
};

export interface ProgressBarProps {
  /** Ratio 0..1 (clamped for the bar; the text shows the real value). Null shows "—". */
  value: number | null | undefined;
  /** Accessible name: makes the bar a `progressbar`; without it the bar is decorative and the visible percentage carries the value. */
  label?: string;
  tone?: ProgressTone;
  /** Shows the percentage beside the bar (default true). */
  showValue?: boolean;
  format?: (ratio: number) => string;
  testId?: string;
  className?: string;
}

/** A progress bar (ratio), filling from the start side; `role="progressbar"` with its value. */
export function ProgressBar({
  value,
  label,
  tone = 'primary',
  showValue = true,
  format,
  testId,
  className,
}: ProgressBarProps) {
  const { i18n } = useTranslation();
  const text =
    value == null
      ? '—'
      : (format ?? ((r: number) => `${formatNumber(Math.round(r * 1000) / 10, i18n.language)} %`))(value);
  const clamped = Math.max(0, Math.min(1, value ?? 0));
  return (
    <span className={cn('flex min-w-24 items-center gap-2', className)} title={label} data-testid={testId}>
      <span
        className="flex h-2 flex-1 overflow-hidden rounded-full bg-muted"
        {...(label
          ? {
              role: 'progressbar',
              'aria-label': label,
              'aria-valuemin': 0,
              'aria-valuemax': 100,
              'aria-valuenow': value == null ? undefined : Math.round(clamped * 100),
              'aria-valuetext': text,
            }
          : { 'aria-hidden': true })}
      >
        <span
          className={cn('h-full rounded-full', progressFill[tone])}
          style={{ inlineSize: `${clamped * 100}%` }}
        />
      </span>
      {showValue && <bdi className="text-xs tabular-nums text-muted-foreground">{text}</bdi>}
    </span>
  );
}

/** "As of 5 Nov 2026, 10:00" for the time the figures were computed. */
export function AsOf({ at, className }: { at: string | Date | null | undefined; className?: string }) {
  const { t, i18n } = useTranslation('charts');
  if (!at) return null;
  const date = typeof at === 'string' ? new Date(at) : at;
  return (
    <p className={cn('text-xs text-muted-foreground', className)} data-testid="as-of">
      <time dateTime={date.toISOString()}>{t('asOf', { time: formatDateTime(date, i18n.language) })}</time>
    </p>
  );
}

export interface DrillLinkProps {
  to: string;
  params?: Record<string, string>;
  search?: Record<string, unknown>;
  children?: ReactNode;
  className?: string;
  testId?: string;
}

/** A "show details" link from a widget to the filtered list / record (drill-down); the chevron mirrors in RTL. */
export function DrillLink({ to, params, search, children, className, testId }: DrillLinkProps) {
  const { t } = useTranslation('charts');
  return (
    <Link
      to={to as never}
      params={params as never}
      search={search as never}
      className={cn('inline-flex items-center gap-1 text-sm text-primary hover:underline', className)}
      data-testid={testId}
    >
      {children ?? t('drill')}
      <ChevronRight className="size-4 rtl:rotate-180" aria-hidden />
    </Link>
  );
}

type ExportAction = () => void | Promise<void>;

export interface ExportMenuProps {
  onPdf?: ExportAction;
  onExcel?: ExportAction;
  onPng?: ExportAction;
  disabled?: boolean;
  /** Called when an export throws (e.g. show a toast). */
  onError?: (error: unknown) => void;
  testId?: string;
}

/** Export menu (PDF / Excel / PNG); only the formats with a callback are offered. */
export function ExportMenu({ onPdf, onExcel, onPng, disabled, onError, testId }: ExportMenuProps) {
  const { t } = useTranslation('charts');
  const [busy, setBusy] = useState(false);
  const items = [
    ['pdf', onPdf, FileText],
    ['excel', onExcel, FileSpreadsheet],
    ['png', onPng, ImageIcon],
  ] as const;
  const run = async (action: ExportAction) => {
    setBusy(true);
    try {
      await action();
    } catch (error) {
      onError?.(error);
    } finally {
      setBusy(false);
    }
  };
  if (!items.some(([, action]) => action)) return null;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" loading={busy} disabled={disabled} data-testid={testId}>
          <Download aria-hidden />
          {t('export.menu')}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {items.map(([key, action, Icon]) =>
          action ? (
            <DropdownMenuItem
              key={key}
              onSelect={() => void run(action)}
              data-testid={testId ? `${testId}-${key}` : undefined}
            >
              <Icon className="size-4" aria-hidden />
              {t(`export.${key}`)}
            </DropdownMenuItem>
          ) : null,
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/** Saves a data URL as a file. */
export function downloadDataUrl(url: string, fileName: string): void {
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.append(a);
  a.click();
  a.remove();
}

/** PNG export of a chart through its handle (`getDataURL`, rasterised). */
export async function exportChartPng(
  handle: ChartHandle | null | undefined,
  fileName: string,
): Promise<void> {
  const url = await handle?.toPng();
  if (!url) throw new Error('charts.export.notReady');
  downloadDataUrl(url, fileName.endsWith('.png') ? fileName : `${fileName}.png`);
}
