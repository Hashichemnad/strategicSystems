import { Link, useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber, pickText } from '@/lib/format';
import { cn } from '@/lib/cn';
import { barOption, Chart, gaugeOption, lineOption, bubbleOption, type ChartClickEvent } from '@/ui/charts';
import { HeatGrid, RagGrid, StrategyMap, Tile, type Rag } from '@/ui/dashboard';
import { EmptyState, type Tone } from '@/ui';
import type { DashboardData, WidgetConfig, WidgetData } from './api';

type Units = DashboardData['units'];

/** Formats a value by its kind (ratio / score as %, money, counts) in the user's language (CLAUDE.md §10). */
export function formatValue(
  value: number | null | undefined,
  kind: string | null | undefined,
  language: string,
): string {
  if (value === null || value === undefined) return '—';
  const round = (v: number, digits: number) => Math.round(v * 10 ** digits) / 10 ** digits;
  switch (kind) {
    case 'ratio':
    case 'score':
      return `${formatNumber(round(value * 100, 1), language)} %`;
    case 'money':
      return formatNumber(round(value, 0), language);
    case 'hours':
    case 'days':
      return formatNumber(round(value, 1), language);
    default:
      return formatNumber(round(value, 2), language);
  }
}

export function toRag(rag: string | null | undefined): Rag | undefined {
  return rag === 'green' || rag === 'amber' || rag === 'red' ? rag : rag === 'none' ? 'grey' : undefined;
}

/** Label of a dimension value: the metric's labels, unit names for the pseudo-dimension "unit", else the code. */
export function useDimensionLabel(data: WidgetData, units: Units) {
  const { t, i18n } = useTranslation('reporting');
  return (dimension: string, code: string) => {
    if (dimension === 'unit') {
      const unit = units.find((u) => u.id === code);
      return unit
        ? pickText(i18n.language, unit.nameAr, unit.nameEn) || unit.code
        : code
          ? code.slice(0, 8)
          : t('widgets.noUnit');
    }

    const label = data.labels?.find((l) => l.dimension === dimension && l.code === code);
    if (label) return pickText(i18n.language, label.labelAr, label.labelEn) || code;
    if (code === 'true') return t('widgets.yes');
    if (code === 'false') return t('widgets.no');
    return code || '—';
  };
}

function title(config: WidgetConfig, data: WidgetData, language: string) {
  return (
    pickText(language, config.titleAr ?? data.metricLabelAr, config.titleEn ?? data.metricLabelEn) ||
    config.id
  );
}

const toneDot: Partial<Record<Tone, string>> = {
  danger: 'bg-danger',
  warning: 'bg-warning',
  success: 'bg-success',
  info: 'bg-info',
};

const ragColor: Record<string, string> = {
  green: 'var(--color-success)',
  amber: 'var(--color-warning)',
  red: 'var(--color-danger)',
};

/** One widget of a dashboard (docs/design/reporting.md §3.1): every type, accessible, RTL-aware, with drill-down. */
export function WidgetView({
  config,
  data,
  units,
  unitId,
  period,
}: {
  config: WidgetConfig;
  data: WidgetData;
  units: Units;
  unitId?: string;
  period: { kind: string; from: string; to: string };
}) {
  const { t, i18n } = useTranslation('reporting');
  const navigate = useNavigate();
  const label = useDimensionLabel(data, units);
  const name = title(config, data, i18n.language);
  const fmt = (v: number) => formatValue(v, data.valueKind, i18n.language);
  const drill = (key: Record<string, string> | undefined, link?: string | null) => {
    if (key && 'unit' in key && key.unit) {
      void navigate({
        to: '/dashboards/$key',
        params: { key: 'executive' },
        search: { unit: key.unit, kind: period.kind, from: period.from, to: period.to } as never,
      });
      return;
    }

    if (link) void navigate({ to: link as '/' });
  };

  const body = useMemo(() => {
    if (data.state === 'unavailable')
      return (
        <p className="text-sm text-muted-foreground" data-testid="widget-unavailable">
          {t('widgets.unavailable')}
        </p>
      );

    switch (config.type) {
      case 'tile':
        return (
          <Tile
            label={name}
            value={data.value ?? '—'}
            format={fmt}
            previous={data.previous}
            higherIsBetter={data.higherIsBetter ?? true}
            rag={toRag(data.rag)}
            sparkline={data.trend?.map((p) => p.value) ?? undefined}
            to={data.drillLink ?? undefined}
            testId={`widget-${config.id}-tile`}
          />
        );
      case 'gauge': {
        const spec = gaugeOption({
          name,
          value: data.value ?? null,
          min: 0,
          max: data.valueKind === 'ratio' || data.valueKind === 'score' ? 1 : undefined,
          format: fmt,
          color: data.rag ? ragColor[data.rag] : undefined,
        });
        return (
          <Chart
            option={spec.option}
            table={spec.table}
            label={name}
            height={180}
            testId={`widget-${config.id}-chart`}
          />
        );
      }
      case 'line': {
        const points = data.trend ?? [];
        const spec = lineOption({
          categories: points.map((p) => ({
            key: p.month,
            label: new Intl.DateTimeFormat(i18n.language, { month: 'short', year: '2-digit' }).format(
              new Date(`${p.month}-01T00:00:00`),
            ),
          })),
          series: [{ name, values: points.map((p) => p.value ?? null), area: true }],
          format: fmt,
          categoryHeader: t('widgets.month'),
        });
        return (
          <Chart
            option={spec.option}
            table={spec.table}
            label={name}
            height={240}
            testId={`widget-${config.id}-chart`}
          />
        );
      }
      case 'bar':
      case 'table': {
        const rows = data.rows ?? [];
        if (rows.length === 0) return <EmptyState title={t('widgets.empty')} />;
        const dim = config.dimension ?? '';
        const series = config.series ?? undefined;
        const categoryKeys = [...new Set(rows.map((r) => r.key[dim] ?? ''))];
        const seriesKeys = series ? [...new Set(rows.map((r) => r.key[series] ?? ''))] : [''];
        const value = (c: string, s: string) =>
          rows.find((r) => r.key[dim] === c && (!series || r.key[series] === s))?.value ?? null;
        const categories = categoryKeys.map((key) => ({ key, label: label(dim, key) }));
        const spec = barOption({
          categories,
          series: seriesKeys.map((s) => ({
            name: series ? label(series, s) : name,
            values: categoryKeys.map((c) => value(c, s)),
          })),
          orientation: config.variant === 'horizontal' ? 'horizontal' : 'vertical',
          stacked: config.variant === 'stacked' || (!!series && config.variant !== 'grouped'),
          topN: config.top ?? undefined,
          othersLabel: t('widgets.others'),
          format: fmt,
          categoryHeader:
            label(dim, '__header__') === '__header__'
              ? t(`dimensions.${dim}` as 'dimensions.status', { defaultValue: dim })
              : dim,
          legend: !!series,
        });
        const onClick = (event: ChartClickEvent) => {
          const row = rows.find((r) => r.key[dim] === event.key);
          drill(row?.key, row?.link ?? data.drillLink);
        };
        return (
          <Chart
            option={spec.option}
            table={spec.table}
            label={name}
            height={Math.max(220, config.variant === 'horizontal' ? categoryKeys.length * 26 + 60 : 260)}
            onClick={onClick}
            tableOpen={config.type === 'table'}
            testId={`widget-${config.id}-chart`}
          />
        );
      }
      case 'heatmap': {
        const rows = data.rows ?? [];
        const dim = config.dimension ?? '';
        const series = config.series ?? '';
        const rowKeys = [...new Set(rows.map((r) => r.key[dim] ?? ''))].sort();
        const columnKeys = [...new Set(rows.map((r) => r.key[series] ?? ''))].sort();
        const max = Math.max(1, ...rows.map((r) => r.value ?? 0));
        return (
          <HeatGrid
            label={name}
            rows={rowKeys.map((key) => ({ key, label: label(dim, key), title: label(dim, key) }))}
            columns={columnKeys.map((key) => ({ key, label: label(series, key), title: label(series, key) }))}
            cell={(row, column) => {
              const v = rows.find((r) => r.key[dim] === row.key && r.key[series] === column.key)?.value ?? 0;
              const intensity = v / max;
              return {
                content: v ? formatValue(v, data.valueKind, i18n.language) : '',
                label: `${row.title ?? row.key} · ${column.title ?? column.key}: ${formatValue(v, data.valueKind, i18n.language)}`,
                background: v
                  ? `color-mix(in srgb, var(--color-primary) ${Math.round(15 + intensity * 65)}%, transparent)`
                  : undefined,
                color: intensity > 0.55 ? 'var(--color-primary-foreground)' : undefined,
                muted: !v,
              };
            }}
            onCellClick={(row, column) => drill({ [dim]: row, [series]: column }, data.drillLink)}
            testId={`widget-${config.id}-heat`}
          />
        );
      }
      case 'bubble': {
        const points = (data.bubbles ?? []).map((b) => ({
          key: b.code,
          label: label(config.dimension ?? '', b.code),
          x: b.x ?? 0,
          y: b.y ?? 0,
          size: b.size ?? null,
        }));
        const spec = bubbleOption({
          groups: [{ name, points }],
          x: { name: config.metrics?.[0] ?? 'x' },
          y: { name: config.metrics?.[1] ?? 'y' },
          size: { name: config.metrics?.[2] ?? 'size' },
        });
        return (
          <Chart
            option={spec.option}
            table={spec.table}
            label={name}
            height={280}
            testId={`widget-${config.id}-chart`}
          />
        );
      }
      case 'strategyMap': {
        const rows = data.rows ?? [];
        const perspectives = [...new Set(rows.map((r) => r.key.perspective ?? ''))].map((key) => ({
          key,
          label: label('perspective', key),
        }));
        const objectives = rows
          .filter((r) => r.key.objective)
          .map((r) => {
            const score = r.value ?? null;
            const rag: Rag =
              score === null ? 'grey' : score >= 0.9 ? 'green' : score >= 0.7 ? 'amber' : 'red';
            return {
              key: r.key.objective ?? '',
              perspective: r.key.perspective ?? '',
              label: label('objective', r.key.objective ?? ''),
              score,
              rag,
            };
          });
        if (objectives.length === 0) return <EmptyState title={t('widgets.empty')} />;
        return (
          <StrategyMap
            perspectives={perspectives}
            objectives={objectives}
            label={name}
            onSelect={(objective) =>
              drill(undefined, rows.find((r) => r.key.objective === objective)?.link ?? data.drillLink)
            }
            testId={`widget-${config.id}-map`}
          />
        );
      }
      case 'list': {
        const items = data.items ?? [];
        if (items.length === 0)
          return <p className="text-sm text-muted-foreground">{t('widgets.emptyList')}</p>;
        return (
          <ul className="flex flex-col divide-y" data-testid={`widget-${config.id}-list`}>
            {items.map((item) => (
              <li
                key={`${item.resourceType}:${item.id}`}
                className="flex flex-wrap items-center justify-between gap-x-2 gap-y-0.5 py-2 text-sm"
              >
                <Link
                  to={item.link as '/'}
                  className="min-w-0 max-w-full truncate text-primary hover:underline"
                  data-testid="widget-list-link"
                >
                  {[item.number, pickText(i18n.language, item.titleAr, item.titleEn)]
                    .filter(Boolean)
                    .join(' · ')}
                </Link>
                <span className="flex min-w-0 max-w-full items-center gap-2 text-xs text-muted-foreground">
                  <span className="min-w-0 truncate">
                    {pickText(i18n.language, item.detailAr, item.detailEn)}
                  </span>
                  {item.date && (
                    <span className="shrink-0">
                      {new Intl.DateTimeFormat(i18n.language).format(new Date(`${item.date}T00:00:00`))}
                    </span>
                  )}
                  {item.value !== null && (
                    <span className="tabular-nums">{formatNumber(item.value, i18n.language)}</span>
                  )}
                  <span
                    aria-hidden
                    className={cn(
                      'size-2 rounded-full',
                      toneDot[item.tone as Tone] ?? 'bg-muted-foreground/40',
                    )}
                  />
                </span>
              </li>
            ))}
          </ul>
        );
      }
      case 'scorecard': {
        const card = data.scorecard;
        if (!card || card.rows.length === 0) return <EmptyState title={t('widgets.empty')} />;
        return (
          <RagGrid
            label={name}
            rowHeader={t('widgets.department')}
            columns={card.columns.map((c) => ({
              key: c.code,
              label: pickText(i18n.language, c.labelAr, c.labelEn),
            }))}
            rows={card.rows.map((row) => ({
              key: row.unitId,
              label: pickText(i18n.language, row.nameAr, row.nameEn) || row.code,
              text: pickText(i18n.language, row.nameAr, row.nameEn) || row.code,
              cells: Object.fromEntries(
                row.cells.map((cell) => {
                  const column = card.columns.find((c) => c.code === cell.column);
                  return [
                    cell.column,
                    {
                      rag: toRag(cell.rag) ?? 'grey',
                      value: formatValue(cell.value, column?.valueKind, i18n.language),
                      text: formatValue(cell.value, column?.valueKind, i18n.language),
                    },
                  ];
                }),
              ),
            }))}
            onRowClick={(row) => drill({ unit: row })}
            onCellClick={(row, column) => {
              const link = card.rows
                .find((r) => r.unitId === row)
                ?.cells.find((c) => c.column === column)?.link;
              if (link) void navigate({ to: link as '/' });
            }}
            testId="department-scorecard"
          />
        );
      }
      default:
        return null;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config, data, units, i18n.language, unitId, period.kind, period.from, period.to]);

  if (config.type === 'tile' || config.type === 'brief')
    return <div data-testid={`widget-${config.id}`}>{body}</div>;
  return (
    <section
      className={cn('flex h-full flex-col gap-2 rounded-lg border bg-card p-4')}
      aria-label={name}
      data-testid={`widget-${config.id}`}
    >
      <header className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">{name}</h3>
        {data.live && <span className="text-xs text-muted-foreground">{t('widgets.live')}</span>}
      </header>
      {body}
    </section>
  );
}
