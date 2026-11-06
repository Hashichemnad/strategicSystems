import { useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';

export interface GridAxisItem {
  key: string;
  label: ReactNode;
  /** Tooltip of the header (e.g. the scale's name for a number). */
  title?: string;
}

export interface HeatGridCell {
  /** Visible content (count). */
  content?: ReactNode;
  /** Accessible name of the cell (always say what the colour means). */
  label: string;
  background?: string;
  color?: string;
  /** Dimmed (e.g. empty cell). */
  muted?: boolean;
  testId?: string;
}

export interface HeatGridProps {
  /** Rows top to bottom. */
  rows: readonly GridAxisItem[];
  /** Columns from the start side (mirrors in RTL by itself). */
  columns: readonly GridAxisItem[];
  cell: (row: GridAxisItem, column: GridAxisItem) => HeatGridCell;
  onCellClick?: (row: string, column: string) => void;
  selected?: { row: string; column: string } | undefined;
  /** Accessible name of the grid. */
  label: string;
  /** Axis captions (vertical beside the rows, under the columns). */
  rowAxisLabel?: ReactNode;
  columnAxisLabel?: ReactNode;
  testId?: string;
  className?: string;
}

/** Moves the focus position for an arrow / Home / End key; ArrowLeft / Right follow the visual direction. Pure; exported for tests. */
export function moveFocus(
  key: string,
  position: { row: number; column: number },
  size: { rows: number; columns: number },
  rtl: boolean,
): { row: number; column: number } | null {
  const forward = rtl ? -1 : 1;
  const clamp = (v: number, max: number) => Math.max(0, Math.min(max - 1, v));
  switch (key) {
    case 'ArrowRight':
      return { ...position, column: clamp(position.column + forward, size.columns) };
    case 'ArrowLeft':
      return { ...position, column: clamp(position.column - forward, size.columns) };
    case 'ArrowDown':
      return { ...position, row: clamp(position.row + 1, size.rows) };
    case 'ArrowUp':
      return { ...position, row: clamp(position.row - 1, size.rows) };
    case 'Home':
      return { ...position, column: 0 };
    case 'End':
      return { ...position, column: size.columns - 1 };
    default:
      return null;
  }
}

/**
 * A heat-map as an accessible grid of buttons (ADR 0105, 0148: better than a canvas for keyboard and screen readers): rows × columns,
 * each cell coloured, with its count and a full accessible name; arrow keys move between cells (one tab stop), Enter / Space clicks.
 */
export function HeatGrid({
  rows,
  columns,
  cell,
  onCellClick,
  selected,
  label,
  rowAxisLabel,
  columnAxisLabel,
  testId,
  className,
}: HeatGridProps) {
  const { t, i18n } = useTranslation();
  const rtl = i18n.dir() === 'rtl';
  const initial = selected
    ? {
        row: Math.max(
          0,
          rows.findIndex((r) => r.key === selected.row),
        ),
        column: Math.max(
          0,
          columns.findIndex((c) => c.key === selected.column),
        ),
      }
    : { row: 0, column: 0 };
  const [focus, setFocus] = useState(initial);
  const refs = useRef(new Map<string, HTMLButtonElement>());
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const next = moveFocus(event.key, focus, { rows: rows.length, columns: columns.length }, rtl);
    if (!next) return;
    event.preventDefault();
    setFocus(next);
    refs.current.get(`${next.row}:${next.column}`)?.focus();
  };
  if (rows.length === 0 || columns.length === 0) {
    // A grid needs rows and cells (WCAG 4.1.2): with nothing to show, say so instead of an empty grid.
    return (
      <p className={cn('text-sm text-muted-foreground', className)} data-testid={testId}>
        <span className="sr-only">{label}: </span>
        {t('heatGrid.empty')}
      </p>
    );
  }
  return (
    <div className={cn('flex flex-col gap-2', className)} data-testid={testId}>
      <div className="flex items-stretch gap-2">
        {rowAxisLabel && (
          <div className="flex items-center">
            <span className="rotate-180 text-xs text-muted-foreground [writing-mode:vertical-rl]">
              {rowAxisLabel}
            </span>
          </div>
        )}
        <div
          className="grid flex-1 gap-1"
          style={{ gridTemplateColumns: `auto repeat(${columns.length}, minmax(2.5rem, 1fr))` }}
          role="grid"
          tabIndex={-1}
          aria-label={label}
          onKeyDown={onKeyDown}
        >
          {rows.map((row, r) => (
            <div key={row.key} role="row" className="contents">
              <span
                role="rowheader"
                className="flex items-center justify-end pe-1 text-xs text-muted-foreground tabular-nums"
                title={row.title}
              >
                {row.label}
              </span>
              {columns.map((column, c) => {
                const value = cell(row, column);
                const active = selected?.row === row.key && selected.column === column.key;
                return (
                  <button
                    key={column.key}
                    ref={(el) => {
                      if (el) refs.current.set(`${r}:${c}`, el);
                      else refs.current.delete(`${r}:${c}`);
                    }}
                    type="button"
                    role="gridcell"
                    tabIndex={focus.row === r && focus.column === c ? 0 : -1}
                    data-testid={value.testId}
                    aria-selected={active}
                    title={value.label}
                    onFocus={() => setFocus({ row: r, column: c })}
                    onClick={() => onCellClick?.(row.key, column.key)}
                    className={cn(
                      'flex h-11 items-center justify-center rounded text-sm font-semibold tabular-nums outline-offset-2 transition hover:opacity-90',
                      !value.background && 'bg-muted',
                      active && 'ring-2 ring-foreground ring-offset-2',
                      value.muted && 'opacity-60',
                    )}
                    style={
                      value.background ? { backgroundColor: value.background, color: value.color } : undefined
                    }
                  >
                    {value.content}
                    {/* Named by its content: the visible count first, then the full description (WCAG 2.5.3). */}
                    <span className="sr-only">
                      {', '}
                      {value.label}
                    </span>
                  </button>
                );
              })}
            </div>
          ))}
          <div role="row" className="contents">
            <span aria-hidden />
            {columns.map((column) => (
              <span
                key={column.key}
                role="columnheader"
                className="text-center text-xs text-muted-foreground tabular-nums"
                title={column.title}
              >
                {column.label}
              </span>
            ))}
          </div>
        </div>
      </div>
      {columnAxisLabel && (
        <span className="text-center text-xs text-muted-foreground">{columnAxisLabel}</span>
      )}
    </div>
  );
}
