import { Download } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
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
} from '@/ui';
import { currentYear, exportUrl, useKpiCards, useKpiDataQuality, useKpiOptions } from './api';
import { KpiPage, PerformanceValue, RagChip, usePercent, useKpiT, yearOptions } from './components';
import { Narrative } from './DashboardPage';

const ALL = '__all';

/** Reports (SPEC §2.5, §2.7): data quality per department, excellence (GEM / Global Star) report, bilingual exports. */
export function KpiReportsPage() {
  const { t } = useTranslation('kpi');
  const { language } = useKpiT();
  const canExport = useCan('kpi.report.export');
  const [year, setYear] = useState(currentYear());
  const exportButton = (kind: string, format: 'xlsx' | 'pdf', extra: Record<string, string> = {}) =>
    canExport && (
      <Button key={`${kind}-${format}`} variant="outline" size="sm" asChild>
        <a
          href={exportUrl(kind, format, language, { year: String(year), ...extra })}
          data-testid={`kpi-export-${kind}-${format}`}
        >
          <Download aria-hidden />
          {t(`reports.formats.${format}`)}
        </a>
      </Button>
    );
  return (
    <KpiPage testId="kpi-reports">
      <PageHeader title={t('reports.title')} description={t('reports.description')} />
      <FormField label={t('common.year')} className="max-w-40">
        <Select
          value={String(year)}
          onValueChange={(v) => setYear(Number(v))}
          options={yearOptions(currentYear())}
        />
      </FormField>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('reports.exports')}</h2>
        {(['cards', 'results', 'scorecard', 'data-quality'] as const).map((kind) => (
          <div
            key={kind}
            className="flex flex-wrap items-center justify-between gap-2 border-b py-2 last:border-b-0"
          >
            <span>{t(`reports.kinds.${kind}`)}</span>
            <span className="flex gap-2">
              {exportButton(kind, 'xlsx')}
              {exportButton(kind, 'pdf')}
            </span>
          </div>
        ))}
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">{t('reports.dataQuality')}</h2>
          <span className="flex gap-2">
            {exportButton('data-quality', 'xlsx')}
            {exportButton('data-quality', 'pdf')}
          </span>
        </header>
        <DataQualityTable year={year} />
      </section>
      <Excellence year={year} exportButton={exportButton} />
      <Narrative year={year} />
    </KpiPage>
  );
}

export function DataQualityTable({ year }: { year: number }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { language } = useKpiT();
  const pct = usePercent();
  const quality = useKpiDataQuality(year);
  if (quality.isError) return <StatusBanner tone="danger" title={errorMessage(tc, quality.error)} />;
  if (!quality.data) return <Skeleton className="h-32" />;
  if (quality.data.length === 0) return <p className="text-sm text-muted-foreground">{t('common.noData')}</p>;
  return (
    <div className="overflow-x-auto">
      <Table data-testid="kpi-data-quality">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('approvals.department')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.due')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.submitted')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.onTime')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.rejection')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.evidence')}</TableHeaderCell>
            <TableHeaderCell>{t('reports.score')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {quality.data.map((q) => (
            <TableRow key={q.unit.id}>
              <TableCell>{pickText(language, q.unit.nameAr, q.unit.nameEn)}</TableCell>
              <TableCell className="tabular-nums">{q.due}</TableCell>
              <TableCell className="tabular-nums">{q.submitted}</TableCell>
              <TableCell className="tabular-nums">{pct(q.onTimeRate)}</TableCell>
              <TableCell className="tabular-nums">{pct(q.rejectionRate)}</TableCell>
              <TableCell className="tabular-nums">{pct(q.evidenceCompleteness)}</TableCell>
              <TableCell className="font-semibold tabular-nums">{pct(q.score)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function Excellence({
  year,
  exportButton,
}: {
  year: number;
  exportButton: (kind: string, format: 'xlsx' | 'pdf', extra?: Record<string, string>) => React.ReactNode;
}) {
  const { t } = useTranslation('kpi');
  const { language } = useKpiT();
  const options = useKpiOptions();
  const [gem, setGem] = useState<string>();
  const [star, setStar] = useState(false);
  const cards = useKpiCards({
    page: 1,
    pageSize: 200,
    year,
    status: 'active',
    ...(gem ? { gem } : {}),
    ...(star ? { globalStar: true } : {}),
  });
  const items = (cards.data?.items ?? []).filter((c) => c.isGlobalStar || c.gemCriteria.length > 0);
  const extra = { ...(gem ? { gem } : {}), ...(star ? { globalStar: 'true' } : {}) };
  const gemLabel = (code: string) => {
    const o = options.data?.lookups['kpi.gem-criterion']?.find((x) => x.code === code);
    return o ? pickText(language, o.labelAr, o.labelEn) || code : code;
  };
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-excellence">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('reports.excellence')}</h2>
        <span className="flex gap-2">
          {exportButton('excellence', 'xlsx', extra)}
          {exportButton('excellence', 'pdf', extra)}
        </span>
      </header>
      <div className="flex flex-wrap items-end gap-4">
        <FormField label={t('fields.gemCriteriaCodes')} className="min-w-56">
          <Select
            value={gem ?? ALL}
            onValueChange={(v) => setGem(v === ALL ? undefined : v)}
            options={[
              { value: ALL, label: t('cards.allGem') },
              ...(options.data?.lookups['kpi.gem-criterion'] ?? []).map((o) => ({
                value: o.code,
                label: pickText(language, o.labelAr, o.labelEn) || o.code,
              })),
            ]}
          />
        </FormField>
        <Checkbox
          label={t('fields.isGlobalStar')}
          checked={star}
          onChange={(e) => setStar(e.target.checked)}
        />
      </div>
      {!cards.data ? (
        <Skeleton className="h-32" />
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('reports.noExcellence')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.gemCriteriaCodes')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.isGlobalStar')}</TableHeaderCell>
              <TableHeaderCell>{t('cards.performance')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map((c) => (
              <TableRow key={c.id}>
                <TableCell>
                  <bdi>{c.code}</bdi>
                </TableCell>
                <TableCell>{pickText(language, c.nameAr, c.nameEn)}</TableCell>
                <TableCell>{c.gemCriteria.map(gemLabel).join('، ')}</TableCell>
                <TableCell>{c.isGlobalStar ? '★' : ''}</TableCell>
                <TableCell>
                  <span className="flex items-center gap-2">
                    <PerformanceValue capped={c.capped} actual={c.actual} />
                    <RagChip rag={c.rag} />
                  </span>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
