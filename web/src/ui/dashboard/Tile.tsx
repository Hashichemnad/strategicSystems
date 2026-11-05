import { Link } from '@tanstack/react-router';
import { ArrowDown, ArrowUp, Minus } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';

export type Rag = 'green' | 'amber' | 'red' | 'grey';
export type TileTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

const toneText: Record<TileTone, string> = {
  neutral: '',
  info: 'text-info',
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
};

export const ragTone: Record<Rag, TileTone> = {
  green: 'success',
  amber: 'warning',
  red: 'danger',
  grey: 'neutral',
};

const ragDot: Record<Rag, string> = {
  green: 'bg-success',
  amber: 'bg-warning',
  red: 'bg-danger',
  grey: 'bg-muted-foreground/50',
};

export interface TileDelta {
  direction: 'up' | 'down' | 'same';
  /** Absolute change. */
  amount: number;
  /** Whether the change is good (true), bad (false) or neither (no change). */
  good: boolean | null;
}

/** Change against the previous value, judged by `higherIsBetter` (pure; exported for tests). */
export function tileDelta(value: number, previous: number, higherIsBetter = true): TileDelta {
  const diff = value - previous;
  if (Math.abs(diff) < 1e-9) return { direction: 'same', amount: 0, good: null };
  const up = diff > 0;
  return { direction: up ? 'up' : 'down', amount: Math.abs(diff), good: up === higherIsBetter };
}

/** Colour class of a delta: green when good, red when bad, muted when unchanged. */
export function deltaClass(delta: TileDelta): string {
  return delta.good === null ? 'text-muted-foreground' : delta.good ? 'text-success' : 'text-danger';
}

export interface TileProps {
  label: ReactNode;
  /** A number (formatted with Intl in the user's language, Arabic-Indic digits per Settings) or ready content. */
  value: number | ReactNode;
  format?: (value: number) => string;
  /** The previous period's value: shows the change with an arrow, coloured by `higherIsBetter`. */
  previous?: number | null;
  higherIsBetter?: boolean;
  /** Format of the change (defaults to `format`). */
  deltaFormat?: (amount: number) => string;
  /** Colours the value (e.g. danger for overdue counts). */
  tone?: TileTone;
  /** RAG status: a coloured dot with its name for screen readers; colours the value unless `tone` is set. */
  rag?: Rag;
  /** Twelve-month trend (oldest first) drawn as a small line. */
  sparkline?: readonly (number | null)[];
  /** Makes the label a link (drill-down). */
  to?: string;
  /** Extra content under the value (hint, link). */
  footer?: ReactNode;
  testId?: string;
  className?: string;
}

/** A metric tile (docs/design/reporting.md §3.1): label, value, delta vs previous period, RAG, sparkline, link. */
export function Tile({
  label,
  value,
  format,
  previous,
  higherIsBetter = true,
  deltaFormat,
  tone,
  rag,
  sparkline,
  to,
  footer,
  testId,
  className,
}: TileProps) {
  const { t, i18n } = useTranslation('charts');
  const fmt = format ?? ((v: number) => formatNumber(v, i18n.language));
  const delta =
    typeof value === 'number' && typeof previous === 'number'
      ? tileDelta(value, previous, higherIsBetter)
      : null;
  const effectiveTone = tone ?? (rag ? ragTone[rag] : 'neutral');
  const DeltaIcon = delta?.direction === 'up' ? ArrowUp : delta?.direction === 'down' ? ArrowDown : Minus;
  const deltaText = delta
    ? delta.direction === 'same'
      ? t('delta.same')
      : t(`delta.${delta.direction}`, { value: (deltaFormat ?? fmt)(delta.amount) })
    : '';
  return (
    <div
      className={cn('flex min-w-0 flex-col gap-1 rounded-lg border bg-card p-4', className)}
      data-testid={testId}
    >
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        {rag && (
          <span className={cn('inline-block size-2.5 shrink-0 rounded-full', ragDot[rag])} aria-hidden />
        )}
        {to ? (
          <Link to={to as never} className="truncate text-primary hover:underline">
            {label}
          </Link>
        ) : (
          <span className="truncate">{label}</span>
        )}
        {rag && <span className="sr-only">{t(`rag.${rag}`)}</span>}
      </div>
      <div className="flex items-end justify-between gap-2">
        <span
          className={cn('text-2xl font-semibold tabular-nums', toneText[effectiveTone])}
          data-testid="tile-value"
        >
          {typeof value === 'number' ? fmt(value) : value}
        </span>
        {sparkline && sparkline.length > 1 && <Sparkline values={sparkline} />}
      </div>
      {delta && (
        <span className={cn('flex items-center gap-1 text-xs', deltaClass(delta))} data-testid="tile-delta">
          <DeltaIcon className="size-3.5" aria-hidden />
          {deltaText}
        </span>
      )}
      {footer}
    </div>
  );
}

/** A small trend line (decorative: the tile states the value; the trend is in the dashboard's charts and tables). */
export function Sparkline({ values, className }: { values: readonly (number | null)[]; className?: string }) {
  const width = 80;
  const height = 24;
  const numbers = values.filter((v): v is number => v !== null);
  if (numbers.length < 2) return null;
  const min = Math.min(...numbers);
  const span = Math.max(...numbers) - min || 1;
  const step = width / (values.length - 1);
  const points = values
    .map((v, i) =>
      v === null
        ? null
        : `${(i * step).toFixed(1)},${(height - 2 - ((v - min) / span) * (height - 4)).toFixed(1)}`,
    )
    .filter(Boolean)
    .join(' ');
  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      className={cn('shrink-0 text-primary rtl:-scale-x-100', className)}
      aria-hidden
      focusable="false"
      data-testid="tile-sparkline"
    >
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinejoin="round" />
    </svg>
  );
}
