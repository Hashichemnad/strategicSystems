import type { EChartsOption } from 'echarts';

/** An ECharts option (type only: the library itself is loaded lazily by {@link Chart}). Written left-to-right; RTL is applied centrally. */
export type ChartOption = EChartsOption;

/** One cell of the accessible table: numbers are formatted in the user's language, `null` shows "—". */
export type ChartCell = string | number | null;

/** The tabular equivalent of a chart ("Show as table", screen readers, PDF). */
export interface ChartTable {
  columns: readonly string[];
  rows: readonly (readonly ChartCell[])[];
  /** Stable key per row (category code); with `onClick` on the chart the first cell becomes a drill-down button. */
  rowKeys?: readonly string[];
}

/** What every option builder returns: the option and the same data as a table. */
export interface ChartSpec {
  option: ChartOption;
  table: ChartTable;
}

/** A click on a data item (drill-down). `key` is the category / item code the builder put on the data item. */
export interface ChartClickEvent {
  key: string | undefined;
  name: string;
  seriesName: string | undefined;
  seriesIndex: number | undefined;
  dataIndex: number;
  value: unknown;
}

/** Imperative access to a rendered chart (export). */
export interface ChartHandle {
  /** The chart as a data URL (SVG, the renderer used), or undefined before it is ready. */
  getDataURL: () => string | undefined;
  /** The chart rasterised to a PNG data URL (2×), or undefined before it is ready. */
  toPng: () => Promise<string | undefined>;
}
