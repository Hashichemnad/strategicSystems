import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '@/lib/format';
import { barOption } from '../charts/builders';
import { Chart } from '../charts/Chart';
import type { ChartClickEvent } from '../charts/types';
import { Section } from './parts';

export interface CountChartRow {
  key: string;
  label: string;
  value: number;
  /** A CSS colour or chart `token(...)` for this bar. */
  color?: string;
}

export interface CountChartProps {
  title: string;
  rows: readonly CountChartRow[];
  /** Horizontal bars (default; long labels) or vertical columns. */
  orientation?: 'horizontal' | 'vertical';
  /** Keep the N largest and add the rest up as "others". */
  topN?: number;
  /** Counts (integer axis, default) or amounts. */
  integer?: boolean;
  format?: (value: number) => string;
  /** Shown instead of the chart when there are no rows. */
  empty?: ReactNode;
  actions?: ReactNode;
  onClick?: (event: ChartClickEvent) => void;
  height?: number;
  testId?: string;
  className?: string;
}

/** A dashboard section with one bar per key (counts by status / type / unit…) — the common case of `barOption`. */
export function CountChart({
  title,
  rows,
  orientation = 'horizontal',
  topN,
  integer = true,
  format,
  empty,
  actions,
  onClick,
  height,
  testId,
  className,
}: CountChartProps) {
  const { t, i18n } = useTranslation('charts');
  const fmt = format ?? ((v: number) => formatNumber(v, i18n.language));
  const shown = topN !== undefined ? Math.min(rows.length, topN + 1) : rows.length;
  const spec = barOption({
    orientation,
    categories: rows,
    series: [{ name: title, values: rows.map((r) => r.value) }],
    categoryColors: rows.map((r) => r.color),
    ...(topN !== undefined ? { topN, othersLabel: t('others') } : {}),
    integer,
    format: fmt,
  });
  return (
    <Section title={title} actions={actions} testId={testId} className={className}>
      {rows.length === 0 ? (
        (empty ?? <p className="text-sm text-muted-foreground">{t('noData')}</p>)
      ) : (
        <Chart
          {...spec}
          label={title}
          onClick={onClick}
          height={height ?? (orientation === 'horizontal' ? Math.max(140, shown * 32 + 40) : 240)}
        />
      )}
    </Section>
  );
}
