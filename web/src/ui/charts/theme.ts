/**
 * Chart colours from the design tokens (src/styles/index.css), light and dark. ECharts needs sRGB colours, the tokens are OKLCH, so
 * they are resolved in the browser through a 1-pixel canvas; without a canvas (tests) the static approximations below are used.
 */
export interface ChartTokens {
  foreground: string;
  muted: string;
  mutedForeground: string;
  border: string;
  card: string;
  primary: string;
  info: string;
  success: string;
  warning: string;
  danger: string;
  /** Categorical palette for series, in order. */
  series: readonly string[];
}

const fallback: Record<'light' | 'dark', Omit<ChartTokens, 'series'> & { extra: readonly string[] }> = {
  light: {
    foreground: '#1e2433',
    muted: '#f1f3f6',
    mutedForeground: '#5c6779',
    border: '#e0e4ea',
    card: '#ffffff',
    primary: '#1a7a75',
    info: '#2e6cb5',
    success: '#2e8a52',
    warning: '#9b6a12',
    danger: '#c53a2f',
    extra: ['#7c5cc4', '#c2477a', '#64748b', '#0e8fa8'],
  },
  dark: {
    foreground: '#eef1f6',
    muted: '#2a2f3a',
    mutedForeground: '#a7afbc',
    border: '#383e4b',
    card: '#242833',
    primary: '#4dbfb5',
    info: '#6fa8e8',
    success: '#5cc27c',
    warning: '#e3b14f',
    danger: '#f2735f',
    extra: ['#a78bfa', '#f472b6', '#94a3b8', '#38bdf8'],
  },
};

/** RAG / tone colours by code, for series or cells that carry a status. */
export type ChartTone = 'primary' | 'info' | 'success' | 'warning' | 'danger' | 'neutral';

export function toneColor(tokens: ChartTokens, tone: ChartTone): string {
  return tone === 'neutral' ? tokens.mutedForeground : tokens[tone];
}

let probe: CanvasRenderingContext2D | null | undefined;

/** Resolves any CSS colour (incl. oklch / var()) to #rrggbb, or undefined when the browser cannot. */
export function resolveColor(css: string): string | undefined {
  if (typeof document === 'undefined' || !css) return undefined;
  if (probe === undefined) {
    try {
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      probe = canvas.getContext('2d', { willReadFrequently: true });
    } catch {
      probe = null;
    }
  }
  if (!probe) return undefined;
  probe.clearRect(0, 0, 1, 1);
  probe.fillStyle = '#000';
  probe.fillStyle = css;
  probe.fillRect(0, 0, 1, 1);
  const [r = 0, g = 0, b = 0, a = 0] = probe.getImageData(0, 0, 1, 1).data;
  if (a === 0) return undefined;
  return `#${[r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')}`;
}

/** The tokens of the current theme (`dark` selects the fallback set; the live values come from the CSS variables). */
export function readChartTokens(dark: boolean): ChartTokens {
  const base = fallback[dark ? 'dark' : 'light'];
  const style = typeof document !== 'undefined' ? getComputedStyle(document.documentElement) : undefined;
  const token = (name: string, value: string) =>
    resolveColor(style?.getPropertyValue(`--${name}`).trim() ?? '') ?? value;
  const tokens = {
    foreground: token('foreground', base.foreground),
    muted: token('muted', base.muted),
    mutedForeground: token('muted-foreground', base.mutedForeground),
    border: token('border', base.border),
    card: token('card', base.card),
    primary: token('primary', base.primary),
    info: token('info', base.info),
    success: token('success', base.success),
    warning: token('warning', base.warning),
    danger: token('danger', base.danger),
  };
  return {
    ...tokens,
    series: [tokens.primary, tokens.info, tokens.warning, ...base.extra, tokens.success, tokens.danger],
  };
}

export const chartFontFamily =
  "'IBM Plex Sans Arabic', 'IBM Plex Sans', ui-sans-serif, system-ui, sans-serif";

/** An ECharts theme object built from the tokens (passed to `init`). */
export function chartTheme(tokens: ChartTokens): Record<string, unknown> {
  const axis = {
    axisLine: { show: true, lineStyle: { color: tokens.border } },
    axisTick: { show: false, lineStyle: { color: tokens.border } },
    axisLabel: { color: tokens.mutedForeground },
    splitLine: { lineStyle: { color: tokens.border, type: 'dashed' } },
    nameTextStyle: { color: tokens.mutedForeground },
  };
  return {
    color: tokens.series,
    backgroundColor: 'transparent',
    textStyle: { color: tokens.foreground, fontFamily: chartFontFamily },
    title: { textStyle: { color: tokens.foreground }, subtextStyle: { color: tokens.mutedForeground } },
    legend: { textStyle: { color: tokens.foreground } },
    tooltip: {
      backgroundColor: tokens.card,
      borderColor: tokens.border,
      textStyle: { color: tokens.foreground, fontFamily: chartFontFamily },
    },
    categoryAxis: { ...axis, splitLine: { show: false } },
    valueAxis: { ...axis, axisLine: { show: false } },
    timeAxis: axis,
    gauge: {
      axisLine: { lineStyle: { color: [[1, tokens.muted]] } },
      axisLabel: { color: tokens.mutedForeground },
      detail: { color: tokens.foreground },
      title: { color: tokens.mutedForeground },
    },
    markLine: { lineStyle: { color: tokens.mutedForeground }, label: { color: tokens.foreground } },
  };
}
