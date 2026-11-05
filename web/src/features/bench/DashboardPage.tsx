import { Link } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber } from '@/lib/format';
import { useNames } from '@/features/cap/components';
import {
  barOption,
  Button,
  Chart,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Section,
  Tile,
} from '@/ui';
import { useBenchDashboard, useBenchOptions, type BenchDashboard } from './api';
import { BenchPageLayout, useBenchT } from './components';

/** Benchmarking dashboard (SPEC §8.6): studies by stage / quarter / unit, linked actions, adopted practices. */
export function BenchDashboardPage() {
  const { t } = useTranslation('bench');
  const { t: tc, i18n } = useTranslation();
  const { k } = useBenchT();
  const options = useBenchOptions();
  const names = useNames();
  const year0 = new Date().getFullYear();
  const [year, setYear] = useState<number>();
  const dashboard = useBenchDashboard(year ? { year } : {});
  const bars = (
    rows: readonly BenchDashboard['byStage'][number][],
    label: (code: string) => string,
    name: string,
  ) => (
    <Chart
      label={name}
      height={220}
      {...barOption({
        categories: rows.map((r) => ({ key: r.code, label: label(r.code) })),
        series: [{ name, values: rows.map((r) => r.count) }],
        integer: true,
        format: (v) => formatNumber(v, i18n.language),
      })}
    />
  );

  return (
    <BenchPageLayout testId="bench-dashboard">
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={
          options.data?.canCreate && (
            <Button asChild>
              <Link
                to="/requests/new/$typeCode"
                params={{ typeCode: options.data.requestTypeCode }}
                data-testid="bench-new-study"
              >
                <Plus aria-hidden />
                {t('studies.new')}
              </Link>
            </Button>
          )
        }
      />
      <div className="max-w-xs">
        <Select
          aria-label={t('fields.year')}
          value={year ? String(year) : 'all'}
          onValueChange={(v) => setYear(v === 'all' ? undefined : Number(v))}
          options={[
            { value: 'all', label: t('common.allYears') },
            ...[year0 + 1, year0, year0 - 1].map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
      </div>
      {dashboard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />
      ) : !dashboard.data ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile label={t('dashboard.studies')} value={dashboard.data.studies} testId="bench-tile-studies" />
            <Tile label={t('dashboard.active')} value={dashboard.data.active} />
            <Tile label={t('dashboard.completed')} value={dashboard.data.completed} tone="success" />
            <Tile label={t('dashboard.cancelled')} value={dashboard.data.cancelled} />
            <Tile label={t('dashboard.actions')} value={dashboard.data.actions} />
            <Tile
              label={t('dashboard.actionsCompleted')}
              value={dashboard.data.actionsCompleted}
              tone="success"
            />
            <Tile label={t('dashboard.actionsLate')} value={dashboard.data.actionsLate} tone="danger" />
            <Tile label={t('dashboard.practicesAdopted')} value={dashboard.data.practicesAdopted} />
          </div>
          <div className="grid gap-4 xl:grid-cols-2">
            <Section title={t('dashboard.byStage')}>
              {bars(dashboard.data.byStage, (c) => k(`stage.${c}`), t('dashboard.byStage'))}
            </Section>
            <Section title={t('dashboard.byQuarter')}>
              {bars(dashboard.data.byQuarter, (c) => c, t('dashboard.byQuarter'))}
            </Section>
          </div>
          <Section title={t('dashboard.byUnit')}>
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('fields.responsibleUnit')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.studies')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.completed')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {dashboard.data.byUnit.map((u, i) => (
                  <TableRow key={u.unit?.id ?? i}>
                    <TableCell>{names.unit(u.unit)}</TableCell>
                    <TableCell>{u.studies}</TableCell>
                    <TableCell>{u.completed}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Section>
        </>
      )}
    </BenchPageLayout>
  );
}
