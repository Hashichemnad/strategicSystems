import { Download, FileText, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatMonth, formatNumber } from '@/lib/format';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import {
  barOption,
  Button,
  Chart,
  isMonth,
  lineOption,
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
  Textarea,
  Section,
  Tile,
} from '@/ui';
import {
  auditApi,
  downloadExport,
  lookupTypes,
  useAuditDashboard,
  type AuditCount,
  type AuditSummaryDraft,
} from './api';
import { AuditPageLayout, useAuditLabels, useAuditT, useNames, usePct, useToastMutation } from './components';
import { FindingsTable } from './FindingsPage';

/**
 * Audit dashboard (SPEC §10.3 C): by status, year, department, priority, report type; open vs closed trend; overdue and ageing; closure rate
 * by department; repeat and risk-accepted findings; bilingual reports and the audit committee pack (with an accepted AI summary).
 */
export function AuditDashboardPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc, i18n } = useTranslation();
  const { k, language } = useAuditT();
  const labels = useAuditLabels();
  const names = useNames();
  const pct = usePct();
  const year0 = new Date().getFullYear();
  const [unitId, setUnitId] = useState<string>();
  const [year, setYear] = useState<number>();
  const [priority, setPriority] = useState<string>();
  const [reportType, setReportType] = useState<string>();
  const dashboard = useAuditDashboard({
    ...(unitId ? { unitId } : {}),
    ...(year ? { year } : {}),
    ...(priority ? { priority } : {}),
    ...(reportType ? { reportType } : {}),
  });
  const aiFeature = useAiFeature('auditmgmt-summary');
  const aiAllowed = useCan('auditmgmt.ai.summary');
  const canExport = useCan('auditmgmt.report.export');
  const summary = useToastMutation(auditApi.summary);
  const [draft, setDraft] = useState<AuditSummaryDraft>();
  const [accepted, setAccepted] = useState<{ ar: string; en: string }>();
  const [exporting, setExporting] = useState<string>();

  const exportReport = async (kind: 'department' | 'revenue' | 'committee', format: 'pdf' | 'xlsx') => {
    setExporting(`${kind}-${format}`);
    try {
      await downloadExport(kind, {
        format,
        language,
        unitId: unitId ?? null,
        year: year ?? null,
        summaryAr: kind === 'committee' ? (accepted?.ar ?? null) : null,
        summaryEn: kind === 'committee' ? (accepted?.en ?? null) : null,
      });
    } finally {
      setExporting(undefined);
    }
  };

  const bars = (rows: readonly AuditCount[], label: (code: string) => string, name: string) => (
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
    <AuditPageLayout testId="audit-dashboard">
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            {canExport && (
              <>
                <Button
                  variant="outline"
                  size="sm"
                  loading={exporting === 'department-xlsx'}
                  onClick={() => void exportReport('department', 'xlsx')}
                  data-testid="audit-export-department"
                >
                  <Download aria-hidden />
                  {t('exports.department')}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  loading={exporting === 'revenue-pdf'}
                  onClick={() => void exportReport('revenue', 'pdf')}
                  data-testid="audit-export-revenue"
                >
                  <Download aria-hidden />
                  {t('exports.revenue')}
                </Button>
                <Button
                  size="sm"
                  loading={exporting === 'committee-pdf'}
                  onClick={() => void exportReport('committee', 'pdf')}
                  data-testid="audit-export-committee"
                >
                  <FileText aria-hidden />
                  {t('exports.committee')}
                </Button>
              </>
            )}
            {aiFeature && aiAllowed && (
              <Button
                variant="outline"
                size="sm"
                loading={summary.busy}
                onClick={() => summary.run({ unitId: unitId ?? null, year: year ?? null }, setDraft)}
                data-testid="audit-ai-summary"
              >
                <Sparkles aria-hidden />
                {t('ai.summary')}
              </Button>
            )}
          </div>
        }
      />
      <div className="grid gap-3 md:grid-cols-4">
        <UnitSelect allowEmpty value={unitId} onChange={(v) => setUnitId(v === '-' ? undefined : v)} />
        <Select
          aria-label={t('fields.year')}
          value={year ? String(year) : 'all'}
          onValueChange={(v) => setYear(v === 'all' ? undefined : Number(v))}
          options={[
            { value: 'all', label: t('common.allYears') },
            ...[year0, year0 - 1, year0 - 2, year0 - 3].map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
        <Select
          aria-label={t('fields.priority')}
          value={priority ?? 'all'}
          onValueChange={(v) => setPriority(v === 'all' ? undefined : v)}
          options={[
            { value: 'all', label: t('common.allPriorities') },
            ...labels.lookupOptions(lookupTypes.priority),
          ]}
        />
        <Select
          aria-label={t('fields.reportType')}
          value={reportType ?? 'all'}
          onValueChange={(v) => setReportType(v === 'all' ? undefined : v)}
          options={[
            { value: 'all', label: t('common.allTypes') },
            ...labels.lookupOptions(lookupTypes.reportType),
          ]}
        />
      </div>
      {draft && (
        <Section
          title={
            <span className="flex items-center gap-2">
              {t('ai.summary')} <AiBadge />
            </span>
          }
          testId="audit-ai-summary-draft"
          actions={
            <Button
              size="sm"
              onClick={() => setAccepted({ ar: draft.summaryAr, en: draft.summaryEn })}
              data-testid="audit-ai-summary-accept"
            >
              {accepted ? t('ai.accepted') : t('ai.acceptForPack')}
            </Button>
          }
        >
          <Textarea
            value={language === 'ar' ? draft.summaryAr : draft.summaryEn}
            onChange={(e) =>
              setDraft(
                language === 'ar'
                  ? { ...draft, summaryAr: e.target.value }
                  : { ...draft, summaryEn: e.target.value },
              )
            }
            rows={6}
          />
        </Section>
      )}
      {dashboard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />
      ) : !dashboard.data ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" data-testid="audit-tiles">
            <Tile
              label={t('dashboard.findings')}
              value={dashboard.data.findings}
              testId="audit-tile-findings"
            />
            <Tile label={t('dashboard.open')} value={dashboard.data.open} />
            <Tile label={t('dashboard.late')} value={dashboard.data.late} tone="danger" />
            <Tile label={t('dashboard.closed')} value={dashboard.data.closed} tone="success" />
            <Tile label={t('dashboard.riskAccepted')} value={dashboard.data.riskAccepted} tone="warning" />
            <Tile label={t('dashboard.repeat')} value={dashboard.data.repeat} />
            <Tile
              label={t('dashboard.overdueRecommendations')}
              value={dashboard.data.overdueRecommendations}
              tone="danger"
            />
            <Tile label={t('dashboard.closureRate')} value={pct(dashboard.data.closureRate)} />
          </div>
          <div className="grid gap-4 xl:grid-cols-3">
            <Section title={t('dashboard.byStatus')}>
              {bars(dashboard.data.byStatus, (c) => k(`findingStatus.${c}`), t('dashboard.byStatus'))}
            </Section>
            <Section title={t('dashboard.byPriority')}>
              {bars(
                dashboard.data.byPriority,
                (c) => labels.lookup(lookupTypes.priority, c),
                t('dashboard.byPriority'),
              )}
            </Section>
            <Section title={t('dashboard.byReportType')}>
              {bars(
                dashboard.data.byReportType,
                (c) => labels.lookup(lookupTypes.reportType, c),
                t('dashboard.byReportType'),
              )}
            </Section>
            <Section title={t('dashboard.byYear')}>
              {bars(dashboard.data.byYear, (c) => c, t('dashboard.byYear'))}
            </Section>
            <Section title={t('dashboard.ageing')}>
              {bars(dashboard.data.ageing, (c) => t('dashboard.days', { range: c }), t('dashboard.ageing'))}
            </Section>
            <Section title={t('dashboard.trend')} testId="audit-trend">
              <Chart
                label={t('dashboard.trend')}
                height={220}
                {...lineOption({
                  categories: dashboard.data.trend.map((m) => ({
                    key: m.month,
                    label: isMonth(m.month) ? formatMonth(m.month, i18n.language) : m.month,
                  })),
                  series: [
                    { name: t('dashboard.opened'), values: dashboard.data.trend.map((m) => m.opened) },
                    { name: t('dashboard.closedSeries'), values: dashboard.data.trend.map((m) => m.closed) },
                  ],
                  integer: true,
                  format: (v) => formatNumber(v, i18n.language),
                })}
              />
            </Section>
          </div>
          <Section title={t('dashboard.byDepartment')} testId="audit-by-department">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('fields.department')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.findings')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.open')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.late')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.closed')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.riskAccepted')}</TableHeaderCell>
                  <TableHeaderCell>{t('dashboard.closureRate')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {dashboard.data.byDepartment.map((d, i) => (
                  <TableRow key={d.unit?.id ?? i}>
                    <TableCell>{names.unit(d.unit)}</TableCell>
                    <TableCell>{d.findings}</TableCell>
                    <TableCell>{d.open}</TableCell>
                    <TableCell className={d.late ? 'text-danger' : undefined}>{d.late}</TableCell>
                    <TableCell>{d.closed}</TableCell>
                    <TableCell>{d.riskAccepted}</TableCell>
                    <TableCell>{pct(d.closureRate)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Section>
          <Section title={t('dashboard.overdue')} testId="audit-overdue">
            {dashboard.data.overdue.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('dashboard.noOverdue')}</p>
            ) : (
              <FindingsTable items={dashboard.data.overdue} showComment={false} />
            )}
          </Section>
          <div className="grid gap-4 xl:grid-cols-2">
            <Section title={t('dashboard.repeatFindings')}>
              {dashboard.data.repeatFindings.length === 0 ? (
                <p className="text-sm text-muted-foreground">—</p>
              ) : (
                <FindingsTable items={dashboard.data.repeatFindings} showComment={false} />
              )}
            </Section>
            <Section title={t('dashboard.riskAcceptedFindings')}>
              {dashboard.data.riskAcceptedFindings.length === 0 ? (
                <p className="text-sm text-muted-foreground">—</p>
              ) : (
                <FindingsTable items={dashboard.data.riskAcceptedFindings} showComment={false} />
              )}
            </Section>
          </div>
        </>
      )}
    </AuditPageLayout>
  );
}
