/**
 * The ECharts runtime, loaded on demand by the Chart wrapper (its own `echarts` chunk, vite.config.ts). Only the chart types and
 * components the builders use are registered, with the SVG renderer (crisp, accessible, exportable). Add a type here when a builder
 * needs it (ADR 0148).
 */
import { BarChart, FunnelChart, GaugeChart, LineChart, ScatterChart } from 'echarts/charts';
import {
  AriaComponent,
  GridComponent,
  LegendComponent,
  MarkLineComponent,
  TooltipComponent,
} from 'echarts/components';
import { init, use as register } from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';

register([
  BarChart,
  LineChart,
  ScatterChart,
  GaugeChart,
  FunnelChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  MarkLineComponent,
  AriaComponent,
  SVGRenderer,
]);

export { init };
