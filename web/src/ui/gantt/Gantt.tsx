import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
  type RefObject,
} from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  layoutGantt,
  preferredWidth,
  type GanttDependency,
  type GanttRow,
  type GanttZoom,
  type RowLayout,
} from './ganttLayout';

export interface GanttProps {
  rows: readonly GanttRow[];
  dependencies?: readonly GanttDependency[] | undefined;
  start?: string | null | undefined;
  end?: string | null | undefined;
  today: string;
  /** Accessible name (the project or portfolio shown). */
  title: string;
  testId?: string | undefined;
  /** Rows collapsed at first render (e.g. the projects of a portfolio Gantt). */
  defaultCollapsed?: readonly string[] | undefined;
  /** The rendered <svg>, for PNG / PDF export. */
  svgRef?: RefObject<SVGSVGElement | null> | undefined;
  /** Width used before the container is measured (and in tests). */
  initialWidth?: number | undefined;
}

const LABEL_WIDTH = 260;
const ROW_HEIGHT = 34;
const HEADER_HEIGHT = 46;

function isSummary(r: RowLayout): boolean {
  return r.hasChildren || r.row.kind === 'phase' || r.row.kind === 'project';
}

/**
 * Read-only SVG Gantt (docs/design/portfolio.md §36, ADR 0096): quarter bands over month ticks, planned / actual /
 * baseline bars, achievement fill and %, milestone diamonds, today line, finish-to-start arrows, collapsible phases.
 * RTL mirrors the time axis and moves the labels to the right. Rows are focusable; the tooltip opens on hover and focus.
 */
export function Gantt({
  rows,
  dependencies,
  start,
  end,
  today,
  title,
  testId,
  defaultCollapsed,
  svgRef,
  initialWidth = 960,
}: GanttProps) {
  const { t, i18n } = useTranslation('portfolioViews');
  const language = i18n.language;
  const rtl = i18n.dir() === 'rtl';
  const clipId = useId().replace(/:/g, '');
  const containerRef = useRef<HTMLDivElement>(null);
  const rowRefs = useRef(new Map<string, SVGGElement>());
  const [available, setAvailable] = useState(initialWidth);
  const [zoom, setZoom] = useState<GanttZoom>('quarter');
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => new Set(defaultCollapsed ?? []));
  const [active, setActive] = useState<string | null>(null);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const measure = () => {
      const width = element.clientWidth;
      if (width > 0) setAvailable(width);
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const width = preferredWidth(rows, start, end, today, zoom, available, LABEL_WIDTH);
  const layout = useMemo(
    () =>
      layoutGantt({
        rows,
        dependencies: dependencies ?? [],
        start: start ?? null,
        end: end ?? null,
        today,
        width,
        rtl,
        collapsed,
        labelWidth: LABEL_WIDTH,
        rowHeight: ROW_HEIGHT,
        headerHeight: HEADER_HEIGHT,
      }),
    [rows, dependencies, start, end, today, width, rtl, collapsed],
  );

  const monthName = useMemo(
    () =>
      new Intl.DateTimeFormat(language === 'ar' ? 'ar-AE-u-nu-latn' : 'en-GB', {
        month: 'short',
        timeZone: 'UTC',
      }),
    [language],
  );

  const toggle = (id: string) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const date = (value: string | null) => (value ? formatDate(value, language) : '—');
  const percent = (ratio: number) =>
    t('gantt.percent', { value: formatNumber(Math.round(ratio * 100), language) });
  const label = (r: RowLayout) => `${r.row.code} ${pickText(language, r.row.titleAr, r.row.titleEn)}`.trim();
  const summary = (r: RowLayout) =>
    [
      label(r),
      `${t('gantt.tooltip.planned')}: ${date(r.row.plannedStart)} – ${date(r.row.plannedEnd)}`,
      `${t('gantt.tooltip.achieved')}: ${percent(r.row.achieved)}`,
      r.row.delayed ? t('gantt.tooltip.delayed') : t('gantt.tooltip.onTrack'),
    ].join(', ');

  const onKey = (event: KeyboardEvent<SVGGElement>, r: RowLayout) => {
    const visible = layout.rows;
    const move = (index: number) => {
      const target = visible[index];
      if (target) rowRefs.current.get(target.row.id)?.focus();
    };
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      move(r.index + 1);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      move(r.index - 1);
    } else if ((event.key === 'Enter' || event.key === ' ') && r.hasChildren) {
      event.preventDefault();
      toggle(r.row.id);
    } else if (event.key === 'Escape') setActive(null);
  };

  const activeRow = layout.rows.find((r) => r.row.id === active) ?? null;
  const anchorX = (r: RowLayout) => {
    const x = r.diamond?.cx ?? (r.planned ? r.planned.x + r.planned.width / 2 : null);
    return x ?? (rtl ? layout.labels.x - 8 : layout.axis.x + 8);
  };

  return (
    <div className="flex flex-col gap-3" data-testid={testId}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div role="group" aria-label={t('gantt.zoom')} className="inline-flex rounded-md border p-0.5">
          {(['month', 'quarter'] as const).map((z) => (
            <button
              key={z}
              type="button"
              aria-pressed={zoom === z}
              data-testid={`gantt-zoom-${z}`}
              onClick={() => setZoom(z)}
              className={cn(
                'rounded px-3 py-1 text-sm focus-visible:outline-2 focus-visible:outline-ring',
                zoom === z ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-muted',
              )}
            >
              {t(`gantt.zoom${z === 'month' ? 'Month' : 'Quarter'}`)}
            </button>
          ))}
        </div>
        <Legend />
      </div>
      <div ref={containerRef} className="relative w-full overflow-x-auto rounded-lg border bg-card">
        {layout.rows.length === 0 ? (
          <p className="p-6 text-sm text-muted-foreground">{t('gantt.empty')}</p>
        ) : (
          <div className="relative" style={{ width: layout.width }}>
            <svg
              ref={svgRef}
              role="img"
              aria-label={t('gantt.ariaLabel', { title, count: layout.rows.length })}
              width={layout.width}
              height={layout.height}
              viewBox={`0 0 ${layout.width} ${layout.height}`}
              className="block text-foreground"
              fontFamily="'IBM Plex Sans Arabic', 'IBM Plex Sans', sans-serif"
              fontSize={12}
              data-gantt=""
            >
              <defs>
                <clipPath id={`${clipId}-labels`}>
                  <rect x={layout.labels.x} y={0} width={layout.labels.width - 8} height={layout.height} />
                </clipPath>
              </defs>
              <rect x={0} y={0} width={layout.width} height={layout.height} className="fill-card" />
              {/* Header: quarter bands over month ticks. */}
              {layout.quarters.map((q) => (
                <g key={`q-${q.start}`}>
                  <rect
                    x={q.x}
                    y={0}
                    width={q.width}
                    height={HEADER_HEIGHT / 2}
                    className={q.index % 2 === 0 ? 'fill-muted' : 'fill-secondary'}
                  />
                  <line
                    x1={q.x}
                    x2={q.x}
                    y1={0}
                    y2={layout.height}
                    className="stroke-border"
                    strokeWidth={1}
                  />
                  {q.width > 40 && (
                    <text
                      x={q.x + q.width / 2}
                      y={HEADER_HEIGHT / 4 + 4}
                      textAnchor="middle"
                      className="fill-foreground"
                      fontWeight={600}
                    >
                      {t('gantt.quarter', {
                        quarter: q.index,
                        year: formatNumber(q.year, language).replace(/[,٬]/g, ''),
                      })}
                    </text>
                  )}
                </g>
              ))}
              {layout.months.map((m) => (
                <g key={`m-${m.start}`}>
                  <line
                    x1={m.x}
                    x2={m.x}
                    y1={HEADER_HEIGHT / 2}
                    y2={layout.height}
                    className="stroke-border"
                    strokeOpacity={0.5}
                    strokeDasharray="2 3"
                  />
                  {m.width >= 26 && (
                    <text
                      x={m.x + m.width / 2}
                      y={HEADER_HEIGHT - 8}
                      textAnchor="middle"
                      className="fill-muted-foreground"
                      fontSize={11}
                    >
                      {monthName.format(new Date(`${m.start}T00:00:00Z`))}
                    </text>
                  )}
                </g>
              ))}
              <line
                x1={0}
                x2={layout.width}
                y1={HEADER_HEIGHT}
                y2={HEADER_HEIGHT}
                className="stroke-border"
              />
              {/* Label column separator. */}
              <line
                x1={rtl ? layout.labels.x : layout.labels.width}
                x2={rtl ? layout.labels.x : layout.labels.width}
                y1={0}
                y2={layout.height}
                className="stroke-border"
              />
              {layout.rows.map((r) => {
                const summaryRow = isSummary(r);
                const barY = summaryRow ? r.cy - 6 : r.cy - 9;
                const barH = summaryRow ? 8 : 12;
                const focused = active === r.row.id;
                const percentX = r.planned
                  ? rtl
                    ? r.planned.x - 4
                    : r.planned.x + r.planned.width + 4
                  : r.diamond
                    ? r.diamond.cx + (rtl ? -r.diamond.size - 4 : r.diamond.size + 4)
                    : null;
                return (
                  <g
                    key={r.row.id}
                    ref={(element) => {
                      if (element) rowRefs.current.set(r.row.id, element);
                      else rowRefs.current.delete(r.row.id);
                    }}
                    role="button"
                    tabIndex={0}
                    aria-label={summary(r)}
                    aria-expanded={r.hasChildren ? !r.collapsed : undefined}
                    data-testid="gantt-row"
                    data-row-id={r.row.id}
                    data-delayed={r.row.delayed ? 'true' : undefined}
                    className="cursor-default outline-none"
                    onMouseEnter={() => setActive(r.row.id)}
                    onMouseLeave={() => setActive((current) => (current === r.row.id ? null : current))}
                    onFocus={() => setActive(r.row.id)}
                    onBlur={() => setActive((current) => (current === r.row.id ? null : current))}
                    onKeyDown={(event) => onKey(event, r)}
                    onClick={() => r.hasChildren && toggle(r.row.id)}
                  >
                    <rect
                      x={0}
                      y={r.y}
                      width={layout.width}
                      height={ROW_HEIGHT}
                      className={focused ? 'fill-accent' : r.index % 2 ? 'fill-muted' : 'fill-card'}
                      fillOpacity={focused ? 1 : 0.5}
                    />
                    {focused && (
                      <rect
                        x={1}
                        y={r.y + 1}
                        width={layout.width - 2}
                        height={ROW_HEIGHT - 2}
                        fill="none"
                        className="stroke-ring"
                        strokeWidth={2}
                      />
                    )}
                    <g clipPath={`url(#${clipId}-labels)`}>
                      {r.hasChildren && (
                        <text
                          x={rtl ? r.labelX + 2 : r.labelX - 2}
                          y={r.cy + 4}
                          textAnchor="end"
                          direction={rtl ? 'rtl' : 'ltr'}
                          className="fill-muted-foreground"
                          aria-hidden
                        >
                          {r.collapsed ? (rtl ? '◂' : '▸') : '▾'}
                        </text>
                      )}
                      <text
                        x={r.hasChildren ? (rtl ? r.labelX - 12 : r.labelX + 12) : r.labelX}
                        y={r.cy + 4}
                        textAnchor="start"
                        direction={rtl ? 'rtl' : 'ltr'}
                        className={r.row.delayed ? 'fill-danger' : 'fill-foreground'}
                        fontWeight={summaryRow ? 600 : 400}
                      >
                        {r.row.delayed ? '⚠ ' : ''}
                        <tspan className="fill-muted-foreground">{r.row.code}</tspan>{' '}
                        {pickText(language, r.row.titleAr, r.row.titleEn)}
                      </text>
                    </g>
                    {r.baseline && (
                      <rect
                        data-part="baseline"
                        x={r.baseline.x}
                        y={r.cy + 6}
                        width={Math.max(r.baseline.width, 1)}
                        height={3}
                        rx={1}
                        className="fill-muted-foreground"
                        fillOpacity={0.55}
                      />
                    )}
                    {r.planned && (
                      <>
                        <rect
                          data-part="planned"
                          x={r.planned.x}
                          y={barY}
                          width={Math.max(r.planned.width, 1)}
                          height={barH}
                          rx={summaryRow ? 2 : 3}
                          className={
                            summaryRow
                              ? 'fill-foreground'
                              : r.row.kind === 'sprint' || r.row.kind === 'release'
                                ? 'fill-info-soft stroke-info'
                                : r.row.delayed
                                  ? 'fill-danger-soft stroke-danger'
                                  : 'fill-accent stroke-primary'
                          }
                          fillOpacity={summaryRow ? 0.25 : 1}
                          strokeWidth={1}
                          strokeDasharray={r.row.delayed ? '4 2' : undefined}
                        />
                        {r.achievedFill && r.achievedFill.width > 0 && (
                          <rect
                            data-part="achieved"
                            x={r.achievedFill.x}
                            y={barY}
                            width={r.achievedFill.width}
                            height={barH}
                            rx={summaryRow ? 2 : 3}
                            className={
                              r.row.delayed ? 'fill-danger' : summaryRow ? 'fill-foreground' : 'fill-primary'
                            }
                            fillOpacity={summaryRow ? 0.75 : 0.9}
                          />
                        )}
                      </>
                    )}
                    {r.actual && (
                      <rect
                        data-part="actual"
                        x={r.actual.x}
                        y={summaryRow ? r.cy + 3 : r.cy + 4}
                        width={Math.max(r.actual.width, 1)}
                        height={2}
                        className="fill-foreground"
                        fillOpacity={0.8}
                      />
                    )}
                    {r.diamond && (
                      <polygon
                        data-part="diamond"
                        points={`${r.diamond.cx},${r.cy - r.diamond.size} ${r.diamond.cx + r.diamond.size},${r.cy} ${r.diamond.cx},${r.cy + r.diamond.size} ${r.diamond.cx - r.diamond.size},${r.cy}`}
                        className={
                          r.row.delayed
                            ? 'fill-danger stroke-danger'
                            : r.row.achieved >= 1
                              ? 'fill-success stroke-success'
                              : 'fill-card stroke-primary'
                        }
                        strokeWidth={1.5}
                      />
                    )}
                    {percentX !== null && (
                      <text
                        x={percentX}
                        y={r.cy + 4}
                        textAnchor={rtl ? 'end' : 'start'}
                        fontSize={11}
                        className={r.row.delayed ? 'fill-danger' : 'fill-muted-foreground'}
                      >
                        {percent(r.row.achieved)}
                      </text>
                    )}
                  </g>
                );
              })}
              {layout.dependencies.map((d) => (
                <g key={`${d.from}-${d.to}`} data-testid="gantt-dependency" aria-hidden>
                  <path d={d.d} fill="none" className="stroke-muted-foreground" strokeWidth={1.2} />
                  <polygon points={d.head} className="fill-muted-foreground" />
                </g>
              ))}
              {layout.todayX !== null && (
                <g data-testid="gantt-today" aria-hidden>
                  <line
                    x1={layout.todayX}
                    x2={layout.todayX}
                    y1={HEADER_HEIGHT / 2}
                    y2={layout.height}
                    className="stroke-warning"
                    strokeWidth={1.5}
                    strokeDasharray="4 3"
                  />
                </g>
              )}
            </svg>
            {activeRow && (
              <div
                role="tooltip"
                data-testid="gantt-tooltip"
                className="pointer-events-none absolute z-10 w-64 -translate-x-1/2 rounded-md border bg-card p-3 text-xs text-card-foreground shadow-lg"
                style={{
                  left: Math.min(Math.max(anchorX(activeRow), 136), layout.width - 136),
                  top: activeRow.y + ROW_HEIGHT + 2,
                }}
              >
                <p className="font-semibold">{label(activeRow)}</p>
                <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-2 gap-y-0.5">
                  <dt className="text-muted-foreground">{t('gantt.tooltip.kind')}</dt>
                  <dd>{t(`gantt.kinds.${activeRow.row.kind}`, { defaultValue: activeRow.row.kind })}</dd>
                  <dt className="text-muted-foreground">{t('gantt.tooltip.planned')}</dt>
                  <dd>
                    {date(activeRow.row.plannedStart)} – {date(activeRow.row.plannedEnd)}
                  </dd>
                  <dt className="text-muted-foreground">{t('gantt.tooltip.actual')}</dt>
                  <dd>
                    {date(activeRow.row.actualStart)} – {date(activeRow.row.actualEnd)}
                  </dd>
                  {(activeRow.row.baselineStart || activeRow.row.baselineEnd) && (
                    <>
                      <dt className="text-muted-foreground">{t('gantt.tooltip.baseline')}</dt>
                      <dd>
                        {date(activeRow.row.baselineStart)} – {date(activeRow.row.baselineEnd)}
                      </dd>
                    </>
                  )}
                  <dt className="text-muted-foreground">{t('gantt.tooltip.achieved')}</dt>
                  <dd>{percent(activeRow.row.achieved)}</dd>
                  {activeRow.row.weight > 0 && (
                    <>
                      <dt className="text-muted-foreground">{t('gantt.tooltip.weight')}</dt>
                      <dd>{percent(activeRow.row.weight)}</dd>
                    </>
                  )}
                  <dt className="text-muted-foreground">{t('gantt.tooltip.status')}</dt>
                  <dd className={activeRow.row.delayed ? 'font-medium text-danger' : undefined}>
                    {activeRow.row.delayed ? t('gantt.tooltip.delayed') : t('gantt.tooltip.onTrack')}
                  </dd>
                </dl>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function Legend() {
  const { t } = useTranslation('portfolioViews');
  const swatch = (content: ReactNode) => (
    <svg width={22} height={12} aria-hidden className="shrink-0">
      {content}
    </svg>
  );
  const items: [string, ReactNode][] = [
    [
      'planned',
      swatch(<rect x={1} y={1} width={20} height={10} rx={2} className="fill-accent stroke-primary" />),
    ],
    ['achieved', swatch(<rect x={1} y={1} width={12} height={10} rx={2} className="fill-primary" />)],
    ['actual', swatch(<rect x={1} y={5} width={20} height={2} className="fill-foreground" />)],
    [
      'baseline',
      swatch(<rect x={1} y={5} width={20} height={3} className="fill-muted-foreground" fillOpacity={0.55} />),
    ],
    ['milestone', swatch(<polygon points="11,1 16,6 11,11 6,6" className="fill-card stroke-primary" />)],
    [
      'delayed',
      swatch(
        <rect
          x={1}
          y={1}
          width={20}
          height={10}
          rx={2}
          className="fill-danger-soft stroke-danger"
          strokeDasharray="4 2"
        />,
      ),
    ],
    [
      'today',
      swatch(
        <line
          x1={11}
          x2={11}
          y1={0}
          y2={12}
          className="stroke-warning"
          strokeWidth={2}
          strokeDasharray="3 2"
        />,
      ),
    ],
  ];
  return (
    <ul
      className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground"
      aria-label={t('gantt.legend.title')}
    >
      {items.map(([key, icon]) => (
        <li key={key} className="flex items-center gap-1.5">
          {icon}
          {t(`gantt.legend.${key}`, { defaultValue: key })}
        </li>
      ))}
    </ul>
  );
}
