import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import * as echarts from 'echarts';
import { useTheme } from '@/app/theme';

/** Apache ECharts (ADR 0096) with the page direction and theme applied. Options come from the caller; RTL inverts category axes. */
export function EChart({
  option,
  height = 280,
  label,
  testId,
}: {
  option: echarts.EChartsOption;
  height?: number;
  /** Accessible name of the chart (the figure is described by the caller's table or text). */
  label: string;
  testId?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const { i18n } = useTranslation();
  const rtl = i18n.dir() === 'rtl';
  const { theme } = useTheme();
  const dark =
    theme === 'dark' ||
    (theme === 'system' &&
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches);
  useEffect(() => {
    if (!ref.current) return;
    const chart = echarts.init(ref.current, dark ? 'dark' : undefined, { renderer: 'svg' });
    chart.setOption({
      backgroundColor: 'transparent',
      textStyle: { fontFamily: "'IBM Plex Sans Arabic', 'IBM Plex Sans', sans-serif" },
      ...option,
    });
    const observer = new ResizeObserver(() => chart.resize());
    observer.observe(ref.current);
    return () => {
      observer.disconnect();
      chart.dispose();
    };
  }, [option, dark, rtl]);
  return (
    <div
      ref={ref}
      role="img"
      aria-label={label}
      data-testid={testId}
      style={{ height, width: '100%' }}
      dir={rtl ? 'rtl' : 'ltr'}
    />
  );
}

/** True when the page is right-to-left (for inverting chart axes). */
export function useRtl(): boolean {
  const { i18n } = useTranslation();
  return i18n.dir() === 'rtl';
}
