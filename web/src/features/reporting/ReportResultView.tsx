import { Link } from '@tanstack/react-router';
import { Fragment, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import { cn } from '@/lib/cn';
import {
  barOption,
  Chart,
  lineOption,
  pieOption,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  type ChartSpec,
} from '@/ui';
import type { ReportGroup, ReportTypeRun } from './api2';

const toneClass: Record<string, string> = {
  good: 'bg-success-soft',
  warning: 'bg-warning-soft',
  bad: 'bg-danger-soft',
  info: 'bg-info-soft',
};

/** The chart of a run (ADR 0182): the server computed the values; ECharts draws them (ADR 0148). */
export function ReportChartView({ run }: { run: ReportTypeRun }) {
  const { t, i18n } = useTranslation('reportBuilder');
  const chart = run.chart;
  const spec = useMemo<ChartSpec | null>(() => {
    if (!chart || chart.categories.length === 0) return null;
    const measure = pickText(i18n.language, chart.measureLabelAr, chart.measureLabelEn);
    const categories = chart.categories.map((c, i) => ({ key: String(i), label: c || '—' }));
    const series = chart.series.map((s) => ({ name: s.label ?? measure, values: s.values.map(Number) }));
    switch (chart.type) {
      case 'pie':
      case 'donut':
        return pieOption({
          donut: chart.type === 'donut',
          slices: categories.map((c, i) => ({
            key: c.key,
            label: c.label,
            value: series[0]?.values[i] ?? 0,
          })),
          valueHeader: measure,
        });
      case 'line':
        return lineOption({ categories, series, valueName: measure });
      default:
        return barOption({ categories, series, stacked: chart.type === 'stackedBar', valueName: measure });
    }
  }, [chart, i18n.language]);
  if (!spec) return null;
  return (
    <div className="rounded-lg border bg-card p-3" data-testid="report-chart">
      <Chart option={spec.option} table={spec.table} label={t('chart.title')} height={280} />
    </div>
  );
}

function Aggregates({ group, run }: { group: ReportGroup; run: ReportTypeRun }) {
  const { t } = useTranslation('reportBuilder');
  return (
    <>
      {run.columns.map((column, i) => {
        const values = group.aggregates.filter((a) => a.column === column.code && a.value != null);
        return (
          <TableCell key={column.code} className="font-semibold" dir="auto">
            {i === 0 && <span className="me-2">{t('result.count', { n: group.count })}</span>}
            {values.map((a) => `${t(`summary.${a.function}` as 'summary.sum')}: ${a.value}`).join(' · ')}
          </TableCell>
        );
      })}
    </>
  );
}

/**
 * The rows of a run with group headings and subtotals (up to three levels) and the grand total; links to each object's record;
 * conditional highlighting from the server's tones.
 */
export function ReportResultTable({ run }: { run: ReportTypeRun }) {
  const { t, i18n } = useTranslation('reportBuilder');
  const groupsByPath = useMemo(() => {
    const map = new Map<string, ReportGroup>();
    for (const g of run.groups) map.set(g.keys.join('\u001f'), g);
    return map;
  }, [run.groups]);
  const levels = run.groupBy.length;
  const headings = (keys: string[], previous: string[] | null) => {
    const out: ReportGroup[] = [];
    for (let level = 1; level <= levels; level++) {
      const path = keys.slice(0, level);
      if (previous && previous.slice(0, level).join('\u001f') === path.join('\u001f')) continue;
      const group = groupsByPath.get(path.join('\u001f'));
      if (group) out.push(group);
    }
    return out;
  };
  const groupRow = (group: ReportGroup) => (
    <TableRow key={'g' + group.keys.join('|')} className="bg-muted/60" data-testid="report-group">
      <TableCell colSpan={1} className="font-semibold" dir="auto">
        <span style={{ paddingInlineStart: `${(group.level - 1) * 1.25}rem` }}>{group.label ?? '—'}</span>
      </TableCell>
      <Aggregates group={group} run={run} />
    </TableRow>
  );

  // Without details: the subtotals only, in tree order.
  const onlyGroups = levels > 0 && run.rows.length === 0;
  const lastPage = run.page * run.pageSize >= run.total;
  return (
    <div className="overflow-x-auto">
      <Table data-testid="report-preview">
        <TableHead>
          <TableRow>
            {levels > 0 && <TableHeaderCell>{t('result.group')}</TableHeaderCell>}
            {run.columns.map((c) => (
              <TableHeaderCell key={c.code}>
                <span className="block text-xs font-normal text-muted-foreground">
                  {pickText(i18n.language, c.objectLabelAr, c.objectLabelEn)}
                </span>
                {pickText(i18n.language, c.labelAr, c.labelEn)}
              </TableHeaderCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {onlyGroups
            ? run.groups.map(groupRow)
            : run.rows.map((row, index) => {
                const previous = index > 0 ? (run.rows[index - 1]?.groupKeys ?? null) : null;
                const firstOfObject = new Set<string>();
                return (
                  <Fragment key={index}>
                    {headings(row.groupKeys, previous).map(groupRow)}
                    <TableRow>
                      {levels > 0 && <TableCell />}
                      {row.cells.map((cell, i) => {
                        const column = run.columns[i];
                        const link =
                          column && !firstOfObject.has(column.alias) ? row.links[column.alias] : undefined;
                        if (column) firstOfObject.add(column.alias);
                        const tone = row.tones[i];
                        return (
                          <TableCell
                            key={i}
                            dir="auto"
                            className={cn(tone && toneClass[tone])}
                            data-tone={tone ?? undefined}
                          >
                            {link && cell ? (
                              <Link to={link as '/'} className="text-primary hover:underline">
                                {cell}
                              </Link>
                            ) : (
                              cell || '—'
                            )}
                          </TableCell>
                        );
                      })}
                    </TableRow>
                  </Fragment>
                );
              })}
          {(levels > 0 || run.grandTotal.aggregates.length > 0) && (onlyGroups || lastPage) && (
            <TableRow className="bg-muted" data-testid="report-total">
              {levels > 0 && <TableCell className="font-semibold">{t('result.total')}</TableCell>}
              <Aggregates group={run.grandTotal} run={run} />
            </TableRow>
          )}
        </TableBody>
      </Table>
    </div>
  );
}
