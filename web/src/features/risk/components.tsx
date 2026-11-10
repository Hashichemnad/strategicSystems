import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import { HeatGrid, StatusChip } from '@/ui';
import {
  useRiskOptions,
  type HeatCell,
  type MatrixCell,
  type RatingBand,
  type RiskPerson,
  type RiskUnit,
} from './api';
import { textOn } from './scoring';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, views, kinds…); the message-key test proves they all exist. */
export function useRiskT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('risk');
  return { k: t as unknown as Dyn, language: i18n.language };
}

export function useNum() {
  const { i18n } = useTranslation();
  return (value: number | null | undefined) => (value == null ? '—' : formatNumber(value, i18n.language));
}

export function usePct() {
  const { i18n } = useTranslation();
  return (ratio: number | null | undefined) =>
    ratio == null ? '—' : `${formatNumber(Math.round(ratio * 1000) / 10, i18n.language)} %`;
}

/** Name in the user's language for units / people / options with AR + EN labels. */
export function useNames() {
  const { i18n } = useTranslation();
  return {
    unit: (u: RiskUnit | null | undefined) =>
      u ? pickText(i18n.language, u.nameAr, u.nameEn) || u.code || '—' : '—',
    person: (p: RiskPerson | null | undefined) =>
      p ? pickText(i18n.language, p.nameAr, p.nameEn) || '—' : '—',
    text: (ar: string | null | undefined, en: string | null | undefined) => pickText(i18n.language, ar, en),
  };
}

/** Labels for lookup codes from GET /options (lookups, categories, objectives). */
export function useRiskLabels() {
  const options = useRiskOptions();
  const { language } = useRiskT();
  const { t } = useTranslation('risk');
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
    category: (code: string | null | undefined) => {
      if (!code) return '—';
      if (code === 'project') return t('common.projectCategory');
      const c = options.data?.categories.find((x) => x.code === code);
      return c ? pickText(language, c.nameAr, c.nameEn) || code : code;
    },
    objective: (code: string) => {
      const o = options.data?.objectives.find((x) => x.code === code);
      return o ? pickText(language, o.labelAr, o.labelEn) || code : code;
    },
    effectiveness: (code: string | null | undefined) => {
      if (!code) return '—';
      const e = options.data?.framework.content.controlEffectiveness.find((x) => x.code === code);
      return e ? pickText(language, e.labelAr, e.labelEn) || code : code;
    },
  };
}

/** Band label / colour from the framework (never hard-coded: codes are for logic only). */
export function useBands(bands?: readonly RatingBand[]) {
  const options = useRiskOptions();
  const { language } = useRiskT();
  const list = bands ?? options.data?.framework.content.bands ?? [];
  return {
    bands: list,
    band: (code: string | null | undefined) => list.find((b) => b.code === code),
    label: (code: string | null | undefined) => {
      const b = list.find((x) => x.code === code);
      return b ? pickText(language, b.labelAr, b.labelEn) || b.code : (code ?? '—');
    },
  };
}

/** A rating chip coloured by its band: "High · 12". */
export function RatingChip({
  band,
  score,
  bands,
  prefix,
  testId,
  className,
}: {
  band: string | null | undefined;
  score: number | null | undefined;
  bands?: readonly RatingBand[];
  prefix?: string;
  testId?: string;
  className?: string;
}) {
  const { band: find, label } = useBands(bands);
  const num = useNum();
  if (!band) return <span className="text-muted-foreground">—</span>;
  const color = find(band)?.color;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium',
        className,
      )}
      style={color ? { backgroundColor: color, color: textOn(color), borderColor: color } : undefined}
      data-testid={testId}
      data-band={band}
    >
      {prefix && <span className="opacity-80">{prefix}</span>}
      {label(band)}
      {score != null && <bdi className="tabular-nums">· {num(score)}</bdi>}
    </span>
  );
}

export function CodeChip({
  tone,
  label,
}: {
  tone?: 'neutral' | 'info' | 'success' | 'warning' | 'danger';
  label: string;
}) {
  return <StatusChip tone={tone ?? 'neutral'}>{label}</StatusChip>;
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

const navItems = [
  { to: '/risk', key: 'dashboard', exact: true },
  { to: '/risk/risks', key: 'register' },
  { to: '/risk/my-actions', key: 'myActions' },
  { to: '/risk/project-risks', key: 'projectRisks' },
  { to: '/risk/mechanism', key: 'mechanism' },
  { to: '/risk/reports', key: 'reports' },
] as const;

/** Secondary navigation of Risk Management. */
export function RiskNav() {
  const { t } = useTranslation('risk');
  const options = useRiskOptions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  return (
    <nav
      aria-label={t('nav.label')}
      data-module-nav
      className="-mt-2 flex flex-wrap gap-1 border-b pb-2"
      data-testid="risk-nav"
    >
      {navItems
        .filter(
          (item) =>
            (item.key !== 'projectRisks' || options.data?.canReadProjectRisks !== false) &&
            (item.key !== 'dashboard' || options.data?.canDashboard !== false) &&
            (item.key !== 'reports' || options.data?.canExport !== false),
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
              data-testid={`risk-nav-${item.key}`}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
    </nav>
  );
}

/** Layout of every risk page: secondary navigation above the page. */
export function RiskPageLayout({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <RiskNav />
      {children}
    </div>
  );
}

interface AxisLabels {
  likelihood?: (value: number) => string;
  impact?: (value: number) => string;
}

function range(max: number): number[] {
  return Array.from({ length: max }, (_, i) => i + 1);
}

/**
 * Heat-map (docs/design/risk.md §12, ADR 0105, 0148): the shared accessible HeatGrid — likelihood rows top = highest, impact columns
 * from the start side (mirrors in RTL by itself), each cell coloured by its band and showing the count (enterprise · P project).
 */
export function HeatMap({
  kind,
  cells,
  bands,
  onSelect,
  selected,
  axis,
}: {
  kind: 'inherent' | 'residual';
  cells: readonly HeatCell[];
  bands: readonly RatingBand[];
  onSelect?: (likelihood: number, impact: number) => void;
  selected?: { likelihood: number; impact: number } | undefined;
  axis?: AxisLabels;
}) {
  const { t } = useTranslation('risk');
  const { label, band } = useBands(bands);
  const num = useNum();
  const maxL = Math.max(5, ...cells.map((c) => c.likelihood));
  const maxI = Math.max(5, ...cells.map((c) => c.impact));
  const cell = (l: number, i: number) => cells.find((c) => c.likelihood === l && c.impact === i);
  return (
    <HeatGrid
      testId={`risk-heatmap-${kind}`}
      label={t(`heatmap.${kind}`)}
      rowAxisLabel={t('heatmap.likelihood')}
      columnAxisLabel={t('heatmap.impact')}
      rows={range(maxL)
        .reverse()
        .map((l) => ({ key: String(l), label: l, title: axis?.likelihood?.(l) }))}
      columns={range(maxI).map((i) => ({ key: String(i), label: i, title: axis?.impact?.(i) }))}
      selected={selected ? { row: String(selected.likelihood), column: String(selected.impact) } : undefined}
      onCellClick={(row, column) => onSelect?.(Number(row), Number(column))}
      cell={(row, column) => {
        const l = Number(row.key);
        const i = Number(column.key);
        const c = cell(l, i);
        const enterprise = c?.enterprise ?? 0;
        const project = c?.project ?? 0;
        const color = band(c?.band)?.color;
        return {
          testId: `risk-heat-${kind}-${l}-${i}`,
          label: t('heatmap.cellLabel', {
            likelihood: l,
            impact: i,
            band: c ? label(c.band) : '—',
            count: enterprise,
            project,
          }),
          muted: enterprise + project === 0,
          ...(color ? { background: color, color: textOn(color) } : {}),
          content:
            enterprise + project > 0 ? (
              <span>
                {num(enterprise)}
                {project > 0 && (
                  <span className="ms-1 text-xs font-normal">
                    · {t('heatmap.projectShort', { count: project })}
                  </span>
                )}
              </span>
            ) : undefined,
        };
      }}
    />
  );
}

export interface MatrixMarker {
  likelihood: number | null | undefined;
  impact: number | null | undefined;
  label: string;
  title: string;
}

/**
 * The framework's L × I matrix as a grid of buttons coloured by band. Clickable when `onChange` is given (wizard / review); otherwise
 * read-only with markers (Assessment tab: I, R, T).
 */
export function RiskMatrix({
  cells,
  bands,
  value,
  onChange,
  markers = [],
  prefix,
  axis,
  label: ariaLabel,
}: {
  cells: readonly MatrixCell[];
  bands: readonly RatingBand[];
  value?: { likelihood: number | null | undefined; impact: number | null | undefined };
  onChange?: (likelihood: number, impact: number) => void;
  markers?: readonly MatrixMarker[];
  prefix: string;
  axis?: AxisLabels;
  label: string;
}) {
  const { t } = useTranslation('risk');
  const { label, band } = useBands(bands);
  const num = useNum();
  const maxL = Math.max(1, ...cells.map((c) => c.likelihood));
  const maxI = Math.max(1, ...cells.map((c) => c.impact));
  // Read-only: a grid of rows (rowheader + gridcells) and a header row; editable: a radio group (WCAG 4.1.2).
  const grid = !onChange;
  return (
    <div className="flex flex-col gap-2" data-testid={`risk-matrix-${prefix}`}>
      <div
        className="grid gap-1"
        style={{ gridTemplateColumns: `minmax(4rem, auto) repeat(${maxI}, minmax(2.75rem, 1fr))` }}
        role={onChange ? 'radiogroup' : 'grid'}
        aria-label={ariaLabel}
      >
        {range(maxL)
          .reverse()
          .map((l) => (
            <div key={l} className="contents" role={grid ? 'row' : undefined}>
              <span
                className="flex items-center justify-end pe-1 text-end text-xs text-muted-foreground"
                role={grid ? 'rowheader' : undefined}
              >
                <span>
                  <bdi className="tabular-nums">{l}</bdi>
                  {axis?.likelihood && <span className="ms-1 hidden sm:inline">{axis.likelihood(l)}</span>}
                </span>
              </span>
              {range(maxI).map((i) => {
                const c = cells.find((x) => x.likelihood === l && x.impact === i);
                const color = band(c?.band)?.color;
                const selected = value?.likelihood === l && value.impact === i;
                const here = markers.filter((m) => m.likelihood === l && m.impact === i);
                const text = t('matrix.cellLabel', {
                  likelihood: l,
                  impact: i,
                  score: c ? num(c.score) : '—',
                  band: c ? label(c.band) : '—',
                });
                const content = (
                  <>
                    {here.length > 0 ? (
                      <span className="flex gap-0.5">
                        {here.map((m) => (
                          <span
                            key={m.label}
                            title={m.title}
                            className="rounded bg-background/90 px-1 text-[0.7rem] font-bold text-foreground"
                          >
                            {m.label}
                          </span>
                        ))}
                      </span>
                    ) : (
                      <bdi className="tabular-nums">{c ? num(c.score) : ''}</bdi>
                    )}
                  </>
                );
                const className = cn(
                  'flex h-11 items-center justify-center rounded text-xs font-semibold',
                  selected && 'ring-2 ring-foreground ring-offset-2',
                );
                const style = color ? { backgroundColor: color, color: textOn(color) } : undefined;
                return onChange ? (
                  <button
                    key={i}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    aria-label={text}
                    title={text}
                    data-testid={`risk-matrix-${prefix}-${l}-${i}`}
                    onClick={() => onChange(l, i)}
                    className={cn(className, 'hover:opacity-90')}
                    style={style}
                  >
                    {content}
                  </button>
                ) : (
                  <div
                    key={i}
                    role="gridcell"
                    aria-label={here.length ? `${text} — ${here.map((m) => m.title).join('، ')}` : text}
                    data-testid={`risk-matrix-${prefix}-${l}-${i}`}
                    className={className}
                    style={style}
                  >
                    {content}
                  </div>
                );
              })}
            </div>
          ))}
        <div className="contents" role={grid ? 'row' : undefined}>
          <span />
          {range(maxI).map((i) => (
            <span
              key={i}
              className="text-center text-xs text-muted-foreground"
              role={grid ? 'columnheader' : undefined}
            >
              <bdi className="tabular-nums">{i}</bdi>
              {axis?.impact && <span className="hidden truncate sm:block">{axis.impact(i)}</span>}
            </span>
          ))}
        </div>
      </div>
      <div className="flex justify-between text-xs text-muted-foreground">
        <span>↑ {t('heatmap.likelihood')}</span>
        <span>{t('heatmap.impact')}</span>
      </div>
    </div>
  );
}

/** Axis labels from the framework scales. */
export function useAxisLabels() {
  const options = useRiskOptions();
  const { language } = useRiskT();
  const content = options.data?.framework.content;
  return {
    likelihood: (v: number) => {
      const l = content?.likelihood.find((x) => x.value === v);
      return l ? pickText(language, l.labelAr, l.labelEn) : String(v);
    },
    impact: (v: number) => {
      const l = content?.impact.find((x) => x.value === v);
      return l ? pickText(language, l.labelAr, l.labelEn) : String(v);
    },
  };
}
