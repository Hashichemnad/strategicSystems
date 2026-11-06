import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import type { Rag } from './Tile';

const ragCell: Record<Rag, string> = {
  green: 'bg-success-soft text-success',
  amber: 'bg-warning-soft text-warning',
  red: 'bg-danger-soft text-danger',
  grey: 'bg-muted text-muted-foreground',
};

const ragDot: Record<Rag, string> = {
  green: 'bg-success',
  amber: 'bg-warning',
  red: 'bg-danger',
  grey: 'bg-muted-foreground/50',
};

export interface RagGridCell {
  rag: Rag;
  /** Visible value (score, count); the RAG name is added for screen readers. */
  value?: ReactNode;
  /** Plain-text value for the accessible name (defaults to `value` when it is a string or number). */
  text?: string;
  title?: string;
}

export interface RagGridRow {
  key: string;
  label: ReactNode;
  /** Plain-text row name for accessible names. */
  text: string;
  cells: Readonly<Record<string, RagGridCell | undefined>>;
}

export interface RagGridProps {
  columns: readonly { key: string; label: ReactNode; text?: string }[];
  rows: readonly RagGridRow[];
  /** Accessible name / caption of the grid. */
  label: string;
  /** Header of the row-name column. */
  rowHeader?: ReactNode;
  onRowClick?: (row: string) => void;
  onCellClick?: (row: string, column: string) => void;
  testId?: string;
  className?: string;
}

/**
 * Rows (departments, projects) × columns (metrics) with RAG cells (department scorecard). A semantic table: the first column sticks to
 * the start side while scrolling; row names and cells are buttons when clickable (drill-down); colour is never the only signal
 * (dot + value + RAG name for screen readers).
 */
export function RagGrid({
  columns,
  rows,
  label,
  rowHeader,
  onRowClick,
  onCellClick,
  testId,
  className,
}: RagGridProps) {
  const { t } = useTranslation('charts');
  return (
    <div className={cn('w-full overflow-x-auto rounded-lg border bg-card', className)} data-testid={testId}>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">{label}</caption>
        <thead className="bg-muted/60 text-muted-foreground">
          <tr>
            <th scope="col" className="sticky start-0 z-10 h-11 bg-card px-4 text-start font-medium">
              {rowHeader}
            </th>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className="h-11 px-3 text-center font-medium whitespace-nowrap"
              >
                {column.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y">
          {rows.map((row) => (
            <tr key={row.key} data-testid={testId ? `${testId}-row` : undefined}>
              <th scope="row" className="sticky start-0 z-10 bg-card px-4 py-2 text-start font-medium">
                {onRowClick ? (
                  <button
                    type="button"
                    className="text-start text-primary hover:underline"
                    onClick={() => onRowClick(row.key)}
                  >
                    {row.label}
                  </button>
                ) : (
                  row.label
                )}
              </th>
              {columns.map((column) => {
                const value = row.cells[column.key];
                if (!value)
                  return (
                    <td key={column.key} className="px-3 py-2 text-center text-muted-foreground">
                      —<span className="sr-only">{t('grid.empty')}</span>
                    </td>
                  );
                const text =
                  value.text ??
                  (typeof value.value === 'string' || typeof value.value === 'number'
                    ? String(value.value)
                    : '');
                const name = t('grid.cellLabel', {
                  row: row.text,
                  column: column.text ?? (typeof column.label === 'string' ? column.label : column.key),
                  value: [text, t(`rag.${value.rag}`)].filter(Boolean).join(' · '),
                });
                const content = (
                  <>
                    <span className={cn('size-2 shrink-0 rounded-full', ragDot[value.rag])} aria-hidden />
                    <span className="tabular-nums">{value.value}</span>
                  </>
                );
                const classes = cn(
                  'flex w-full min-w-16 items-center justify-center gap-1.5 rounded px-2 py-1.5 text-xs font-medium',
                  ragCell[value.rag],
                );
                return (
                  <td key={column.key} className="p-1" data-rag={value.rag}>
                    {onCellClick ? (
                      <button
                        type="button"
                        className={cn(classes, 'hover:opacity-90')}
                        aria-label={name}
                        title={value.title ?? name}
                        onClick={() => onCellClick(row.key, column.key)}
                      >
                        {content}
                      </button>
                    ) : (
                      <span className={classes} title={value.title ?? name}>
                        {content}
                        <span className="sr-only">{t(`rag.${value.rag}`)}</span>
                      </span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
