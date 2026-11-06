import { useId, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import type { Rag } from './Tile';

export interface StrategyPerspective {
  key: string;
  label: string;
}

export interface StrategyObjective {
  key: string;
  perspective: string;
  label: string;
  /** Capped score as a ratio (1 = 100 %), or null when it cannot be calculated. */
  score: number | null;
  rag: Rag;
}

export interface StrategyLink {
  from: string;
  to: string;
}

export interface StrategyMapLayout {
  width: number;
  height: number;
  bands: { key: string; label: string; y: number; height: number; labelX: number }[];
  nodes: { key: string; x: number; y: number; width: number; height: number; objective: StrategyObjective }[];
  arrows: { from: string; to: string; x1: number; y1: number; x2: number; y2: number }[];
}

const bandHeight = 112;
const labelWidth = 150;
const nodeHeight = 60;

/**
 * Positions of the perspective bands (top to bottom), objective nodes (evenly spaced from the start side) and cause-effect arrows.
 * RTL mirrors every x so the band labels sit on the right and the reading order runs right to left. Pure; exported for tests.
 */
export function strategyMapLayout(
  perspectives: readonly StrategyPerspective[],
  objectives: readonly StrategyObjective[],
  links: readonly StrategyLink[],
  { rtl, width = 960 }: { rtl: boolean; width?: number },
): StrategyMapLayout {
  const mirror = (x: number) => (rtl ? width - x : x);
  const areaStart = labelWidth + 8;
  const area = width - areaStart - 8;
  const bands = perspectives.map((p, i) => ({
    key: p.key,
    label: p.label,
    y: i * bandHeight,
    height: bandHeight,
    labelX: mirror(12),
  }));
  const nodes = perspectives.flatMap((p, i) => {
    const inBand = objectives.filter((o) => o.perspective === p.key);
    const slot = area / Math.max(1, inBand.length);
    const nodeWidth = Math.min(190, slot - 12);
    return inBand.map((objective, j) => ({
      key: objective.key,
      x: mirror(areaStart + slot * (j + 0.5)),
      y: i * bandHeight + bandHeight / 2,
      width: nodeWidth,
      height: nodeHeight,
      objective,
    }));
  });
  const byKey = new Map(nodes.map((n) => [n.key, n]));
  const arrows = links.flatMap((link) => {
    const from = byKey.get(link.from);
    const to = byKey.get(link.to);
    if (!from || !to || from === to) return [];
    if (from.y === to.y) {
      const dir = to.x > from.x ? 1 : -1;
      return [
        {
          from: link.from,
          to: link.to,
          x1: from.x + (dir * from.width) / 2,
          y1: from.y,
          x2: to.x - (dir * to.width) / 2,
          y2: to.y,
        },
      ];
    }
    const down = to.y > from.y ? 1 : -1;
    return [
      {
        from: link.from,
        to: link.to,
        x1: from.x,
        y1: from.y + (down * from.height) / 2,
        x2: to.x,
        y2: to.y - (down * to.height) / 2,
      },
    ];
  });
  return { width, height: Math.max(1, perspectives.length) * bandHeight, bands, nodes, arrows };
}

const nodeClasses: Record<Rag, string> = {
  green: 'fill-success-soft stroke-success',
  amber: 'fill-warning-soft stroke-warning',
  red: 'fill-danger-soft stroke-danger',
  grey: 'fill-muted stroke-muted-foreground',
};

function truncate(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

export interface StrategyMapProps {
  perspectives: readonly StrategyPerspective[];
  objectives: readonly StrategyObjective[];
  /** Cause-effect links (from KPI strategy settings). */
  links?: readonly StrategyLink[];
  /** Score format (default: percent in the user's language). */
  format?: (score: number) => string;
  onSelect?: (objective: string) => void;
  label?: string;
  testId?: string;
  className?: string;
}

/**
 * Strategy map (docs/design/reporting.md §3.1): perspective bands as rows, objective nodes coloured by the RAG of their capped score
 * with the score, optional cause-effect arrows; RTL mirrored; nodes are keyboard-focusable buttons with full accessible names.
 */
export function StrategyMap({
  perspectives,
  objectives,
  links = [],
  format,
  onSelect,
  label,
  testId,
  className,
}: StrategyMapProps) {
  const { t, i18n } = useTranslation('charts');
  const rtl = i18n.dir() === 'rtl';
  const id = useId().replace(/:/g, '');
  const fmt = format ?? ((s: number) => `${formatNumber(Math.round(s * 1000) / 10, i18n.language)} %`);
  const layout = strategyMapLayout(perspectives, objectives, links, { rtl });
  const names = new Map(objectives.map((o) => [o.key, o.label]));
  const nodeName = (o: StrategyObjective) =>
    t('strategyMap.node', {
      name: o.label,
      score: o.score === null ? t('strategyMap.noScore') : fmt(o.score),
      rag: t(`rag.${o.rag}`),
    });
  const onKey = (event: KeyboardEvent<SVGGElement>, key: string) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onSelect?.(key);
    }
  };
  return (
    <figure className={cn('m-0 flex flex-col gap-2', className)} data-testid={testId}>
      <svg
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        className="h-auto w-full"
        role="group"
        aria-label={label ?? t('strategyMap.label')}
        direction={rtl ? 'rtl' : 'ltr'}
      >
        <defs>
          <marker
            id={`${id}-arrow`}
            viewBox="0 0 10 10"
            refX="9"
            refY="5"
            markerWidth="7"
            markerHeight="7"
            orient="auto-start-reverse"
          >
            <path d="M 0 0 L 10 5 L 0 10 z" className="fill-muted-foreground" />
          </marker>
        </defs>
        {layout.bands.map((band, i) => (
          <g key={band.key} data-testid="strategy-map-band">
            <rect
              x={0}
              y={band.y + 2}
              width={layout.width}
              height={band.height - 4}
              rx={8}
              className={i % 2 === 0 ? 'fill-muted/60' : 'fill-muted/30'}
            />
            <text
              x={band.labelX}
              y={band.y + band.height / 2}
              dominantBaseline="middle"
              textAnchor="start"
              className="fill-foreground text-[13px] font-semibold"
            >
              {truncate(band.label, 22)}
              <title>{band.label}</title>
            </text>
          </g>
        ))}
        <g aria-hidden>
          {layout.arrows.map((a) => (
            <line
              key={`${a.from}-${a.to}`}
              x1={a.x1}
              y1={a.y1}
              x2={a.x2}
              y2={a.y2}
              className="stroke-muted-foreground"
              strokeWidth={1.5}
              markerEnd={`url(#${id}-arrow)`}
            >
              <title>{t('strategyMap.causes', { from: names.get(a.from), to: names.get(a.to) })}</title>
            </line>
          ))}
        </g>
        {layout.nodes.map((node) => {
          const o = node.objective;
          const name = nodeName(o);
          return (
            <g
              key={node.key}
              role={onSelect ? 'button' : 'img'}
              tabIndex={0}
              aria-label={name}
              className={cn('group outline-none', onSelect && 'cursor-pointer')}
              onClick={onSelect ? () => onSelect(o.key) : undefined}
              onKeyDown={onSelect ? (e) => onKey(e, o.key) : undefined}
              data-testid="strategy-map-node"
              data-rag={o.rag}
            >
              <title>{name}</title>
              <rect
                x={node.x - node.width / 2}
                y={node.y - node.height / 2}
                width={node.width}
                height={node.height}
                rx={10}
                strokeWidth={1.5}
                className={cn(
                  nodeClasses[o.rag],
                  'group-focus-visible:stroke-foreground group-focus-visible:[stroke-width:3]',
                )}
              />
              <text
                x={node.x}
                y={node.y - 8}
                textAnchor="middle"
                dominantBaseline="middle"
                className="fill-foreground text-[12px] font-medium"
              >
                {truncate(o.label, Math.max(8, Math.floor(node.width / 7)))}
              </text>
              <text
                x={node.x}
                y={node.y + 14}
                textAnchor="middle"
                dominantBaseline="middle"
                className="fill-foreground text-[13px] font-semibold"
              >
                {o.score === null ? '—' : fmt(o.score)}
              </text>
            </g>
          );
        })}
      </svg>
    </figure>
  );
}
