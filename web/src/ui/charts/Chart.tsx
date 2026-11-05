import { Table2 } from 'lucide-react';
import { useEffect, useId, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { Button } from '../button';
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '../table';
import { resolveTokenColors } from './builders';
import { prepareOption } from './direction';
import { chartTheme, readChartTokens, type ChartTokens } from './theme';
import type { ChartClickEvent, ChartHandle, ChartOption, ChartTable } from './types';

type Runtime = typeof import('./echarts-runtime');
type Instance = ReturnType<Runtime['init']>;

let runtime: Promise<Runtime> | undefined;

/** Loads ECharts once (own chunk). Exported so pages can preload it. */
export function loadECharts(): Promise<Runtime> {
  runtime ??= import('./echarts-runtime');
  return runtime;
}

/** True while the `dark` class is on <html> (set by the ThemeProvider); follows changes. */
export function useDarkMode(): boolean {
  const read = () => typeof document !== 'undefined' && document.documentElement.classList.contains('dark');
  const [dark, setDark] = useState(read);
  useEffect(() => {
    if (typeof MutationObserver === 'undefined') return;
    const observer = new MutationObserver(() => setDark(read()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);
  return dark;
}

/** True when the page is right-to-left. */
export function useRtl(): boolean {
  const { i18n } = useTranslation();
  return i18n.dir() === 'rtl';
}

/** Rasterises an SVG data URL to a PNG data URL (white background, `scale`×). */
export async function svgDataUrlToPng(
  url: string,
  width: number,
  height: number,
  scale = 2,
): Promise<string> {
  const image = new Image();
  await new Promise<void>((resolve, reject) => {
    image.onload = () => resolve();
    image.onerror = () => reject(new Error('charts.export.image'));
    image.src = url;
  });
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(width * scale));
  canvas.height = Math.max(1, Math.round(height * scale));
  const context = canvas.getContext('2d');
  if (!context) throw new Error('charts.export.canvas');
  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/png');
}

/** Maps an ECharts event to the wrapper's click event (the builders put the category / item code on each data item as `key`). */
export function toClickEvent(params: Record<string, unknown>): ChartClickEvent {
  const data = params.data as { key?: unknown } | null | undefined;
  return {
    key: data && typeof data === 'object' && typeof data.key === 'string' ? data.key : undefined,
    name: typeof params.name === 'string' ? params.name : '',
    seriesName: typeof params.seriesName === 'string' ? params.seriesName : undefined,
    seriesIndex: typeof params.seriesIndex === 'number' ? params.seriesIndex : undefined,
    dataIndex: typeof params.dataIndex === 'number' ? params.dataIndex : -1,
    value: params.value,
  };
}

export interface ChartProps {
  /** A left-to-right option, usually from a builder (`barOption`, `lineOption`…); RTL and theme are applied here. */
  option: ChartOption;
  /** Accessible name (also the table caption). */
  label: string;
  /** Short summary read after the name (defaults to the label). */
  description?: string;
  /** The same data as a table: enables "Show as table". */
  table?: ChartTable;
  height?: number;
  testId?: string;
  className?: string;
  /** Drill-down: a click on a data item (and, in the table, on a row's first cell). */
  onClick?: (event: ChartClickEvent) => void;
  /** Starts with the table open. */
  tableOpen?: boolean;
  ref?: Ref<ChartHandle>;
}

/**
 * The single chart component (ADR 0148): Apache ECharts loaded lazily (only the registered charts, SVG renderer), colours from the
 * design tokens (light / dark), direction mirrored centrally, `role="img"` with a name and description, the ECharts aria
 * description, and a "Show as table" toggle rendering the same data as an accessible table.
 */
export function Chart({
  option,
  label,
  description,
  table,
  height = 260,
  testId,
  className,
  onClick,
  tableOpen = false,
  ref,
}: ChartProps) {
  const { t, i18n } = useTranslation('charts');
  const rtl = i18n.dir() === 'rtl';
  const dark = useDarkMode();
  const container = useRef<HTMLDivElement>(null);
  const instance = useRef<Instance | null>(null);
  const click = useRef(onClick);
  const [ready, setReady] = useState(0);
  const [failed, setFailed] = useState(false);
  const [showTable, setShowTable] = useState(tableOpen);
  const id = useId();
  const tokens = useRef<ChartTokens | null>(null);
  // Callers often build the option inline: redraw only when its data (or the language) changed, not on every parent render.
  const latest = useRef(option);
  const signature = `${i18n.language}|${JSON.stringify(option)}`;

  useEffect(() => {
    click.current = onClick;
    latest.current = option;
  });

  useEffect(() => {
    let disposed = false;
    let observer: ResizeObserver | undefined;
    loadECharts()
      .then(({ init }) => {
        if (disposed || !container.current) return;
        tokens.current = readChartTokens(dark);
        const chart = init(container.current, chartTheme(tokens.current), { renderer: 'svg' });
        chart.on('click', (params) =>
          click.current?.(toClickEvent(params as unknown as Record<string, unknown>)),
        );
        instance.current = chart;
        if (typeof ResizeObserver !== 'undefined') {
          observer = new ResizeObserver(() => chart.resize());
          observer.observe(container.current);
        }
        setReady((n) => n + 1);
      })
      .catch(() => {
        if (!disposed) setFailed(true);
      });
    return () => {
      disposed = true;
      observer?.disconnect();
      instance.current?.dispose();
      instance.current = null;
    };
  }, [dark]);

  useEffect(() => {
    const chart = instance.current;
    if (!chart || !tokens.current) return;
    const prepared = prepareOption(resolveTokenColors(latest.current, tokens.current), {
      rtl,
      description: description ? `${label}. ${description}` : label,
    });
    chart.setOption(prepared, { notMerge: true });
  }, [signature, rtl, label, description, ready]);

  useImperativeHandle(
    ref,
    () => ({
      getDataURL: () => instance.current?.getDataURL({ backgroundColor: tokens.current?.card ?? '#ffffff' }),
      toPng: async () => {
        const chart = instance.current;
        if (!chart) return undefined;
        const url = chart.getDataURL({ backgroundColor: tokens.current?.card ?? '#ffffff' });
        return url.startsWith('data:image/svg')
          ? svgDataUrlToPng(url, chart.getWidth(), chart.getHeight())
          : url;
      },
    }),
    [],
  );

  const tableVisible = Boolean(table) && (showTable || failed);
  return (
    <figure className={cn('m-0 flex min-w-0 flex-col gap-2', className)} data-testid={testId}>
      <div
        role="img"
        aria-label={label}
        aria-describedby={description ? `${id}-desc` : undefined}
        className="relative w-full"
        style={{ height }}
        hidden={failed}
      >
        <div ref={container} className="size-full" dir={rtl ? 'rtl' : 'ltr'} aria-hidden />
        {!ready && !failed && (
          <div className="absolute inset-0 animate-pulse rounded-md bg-muted" aria-hidden>
            <span className="sr-only">{t('loading')}</span>
          </div>
        )}
      </div>
      {description && (
        <p id={`${id}-desc`} className="sr-only">
          {description}
        </p>
      )}
      {failed && <p className="text-sm text-muted-foreground">{t('unavailable')}</p>}
      {table && !failed && (
        <Button
          variant="ghost"
          size="sm"
          className="self-end text-xs"
          aria-expanded={showTable}
          aria-controls={`${id}-table`}
          onClick={() => setShowTable((v) => !v)}
          data-testid={testId ? `${testId}-table-toggle` : undefined}
        >
          <Table2 aria-hidden />
          {showTable ? t('hideTable') : t('showTable')}
        </Button>
      )}
      {table && tableVisible && (
        <div id={`${id}-table`}>
          <ChartDataTable table={table} caption={t('tableCaption', { label })} onClick={onClick} />
        </div>
      )}
    </figure>
  );
}

/** The accessible table of a chart's data. */
export function ChartDataTable({
  table,
  caption,
  onClick,
}: {
  table: ChartTable;
  caption: string;
  onClick?: ((event: ChartClickEvent) => void) | undefined;
}) {
  const { t, i18n } = useTranslation('charts');
  const cell = (value: ChartTable['rows'][number][number]) =>
    value === null ? '—' : typeof value === 'number' ? formatNumber(value, i18n.language) : value;
  return (
    <Table>
      <caption className="sr-only">{caption}</caption>
      <TableHead>
        <TableRow>
          {table.columns.map((column, i) => (
            <TableHeaderCell key={i} className={i > 0 ? 'text-end' : undefined}>
              {column || (i === 0 ? t('category') : '')}
            </TableHeaderCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        {table.rows.map((row, r) => {
          const key = table.rowKeys?.[r];
          return (
            <TableRow key={key ?? r}>
              {row.map((value, c) =>
                c === 0 ? (
                  <th key={c} scope="row" className="px-4 py-2 text-start font-normal">
                    {onClick && key !== undefined && key !== '__others' ? (
                      <button
                        type="button"
                        className="text-start text-primary hover:underline"
                        onClick={() =>
                          onClick({
                            key,
                            name: String(value ?? ''),
                            seriesName: undefined,
                            seriesIndex: undefined,
                            dataIndex: r,
                            value: row[1] ?? null,
                          })
                        }
                      >
                        {cell(value)}
                      </button>
                    ) : (
                      cell(value)
                    )}
                  </th>
                ) : (
                  <TableCell key={c} className="py-2 text-end tabular-nums">
                    {cell(value)}
                  </TableCell>
                ),
              )}
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
