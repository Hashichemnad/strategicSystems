import { Link, useNavigate } from '@tanstack/react-router';
import { Download, Play, Plus, Save, Share2, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  useDatasets,
  useReport,
  useReports,
  useRunReport,
  useSaveReport,
  useShareReport,
  type Dataset,
  type ReportDefinition,
} from './api';
import { ScheduleDialog } from './ScheduleDialog';
import { PrincipalListEditor, type PrincipalRef } from './shared';

type Field = Dataset['fields'][number];

interface FilterRow {
  field: string;
  operator: string;
  values: string[];
}

const operatorsByType: Record<string, string[]> = {
  text: ['contains', 'eq', 'ne', 'in', 'empty', 'notEmpty'],
  number: ['eq', 'ne', 'gte', 'lte', 'between', 'in', 'empty', 'notEmpty'],
  money: ['eq', 'gte', 'lte', 'between', 'empty', 'notEmpty'],
  ratio: ['gte', 'lte', 'between', 'empty', 'notEmpty'],
  date: ['eq', 'gte', 'lte', 'between', 'empty', 'notEmpty'],
  dateTime: ['gte', 'lte', 'between', 'empty', 'notEmpty'],
  bool: ['eq'],
  lookup: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
  unit: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
  employee: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
};

/** Operators valid for a field type (mirrors the server's whitelist, ADR 0144). */
export function operatorsFor(type: string): string[] {
  return operatorsByType[type] ?? ['eq'];
}

/** Saved and shared reports (list). */
export function ReportsPage() {
  const { t, i18n } = useTranslation('reporting');
  const reports = useReports();
  const canBuild = useCan('reporting.report.build');
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('reports.title')}
        description={t('reports.description')}
        actions={
          <Button asChild>
            <Link to="/reports/new" data-testid="report-new">
              <Plus className="size-4" aria-hidden />
              {canBuild ? t('reports.new') : t('reports.run')}
            </Link>
          </Button>
        }
      />
      {reports.isPending ? (
        <Skeleton className="h-40" />
      ) : reports.data && reports.data.length > 0 ? (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('reports.name')}</TableHeaderCell>
              <TableHeaderCell>{t('reports.object')}</TableHeaderCell>
              <TableHeaderCell>{t('reports.owner')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {reports.data.map((report) => (
              <TableRow key={report.id}>
                <TableCell>
                  <Link
                    to="/reports/$reportId"
                    params={{ reportId: report.id }}
                    className="text-primary hover:underline"
                  >
                    {report.name}
                  </Link>
                </TableCell>
                <TableCell>{report.dataset}</TableCell>
                <TableCell>{report.canEdit ? t('reports.mine') : t('reports.shared')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <EmptyState title={t('reports.empty')} />
      )}
      <span className="sr-only">{i18n.language}</span>
    </div>
  );
}

/** The report builder (SPEC §0.4, ADR 0144): object → columns → filters → grouping → sort → preview; save, share, export, schedule. */
export function ReportBuilderPage({ reportId }: { reportId?: string }) {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const datasets = useDatasets();
  const report = useReport(reportId);
  const run = useRunReport();
  const save = useSaveReport(reportId);
  const share = useShareReport();
  const canBuild = useCan('reporting.report.build');
  const canShare = useCan('reporting.report.share');
  const canSchedule = useCan('reporting.schedule.manage');
  const [dataset, setDataset] = useState<string>();
  const [columns, setColumns] = useState<string[]>([]);
  const [filters, setFilters] = useState<FilterRow[]>([]);
  const [groupBy, setGroupBy] = useState<string[]>([]);
  const [sort, setSort] = useState<{ field: string; descending: boolean } | null>(null);
  const [page, setPage] = useState(1);
  const [name, setName] = useState('');
  const [shares, setShares] = useState<PrincipalRef[]>([]);

  useEffect(() => {
    if (report.data) {
      const saved = report.data.definition as ReportDefinition;
      setDataset(saved.dataset);
      setColumns(saved.columns);
      setFilters(saved.filters.map((f) => ({ field: f.field, operator: f.operator, values: [...f.values] })));
      setGroupBy(saved.groupBy);
      setSort(saved.sort[0] ?? null);
      setName(report.data.name);
    }
  }, [report.data]);

  const current = datasets.data?.find((d) => d.code === dataset);
  const fields = useMemo(() => current?.fields ?? [], [current]);
  const label = (f: Field) => pickText(i18n.language, f.labelAr, f.labelEn);
  const definition: ReportDefinition = {
    dataset: dataset ?? '',
    columns,
    filters: filters.filter((f) => f.field && f.operator),
    groupBy,
    sort: sort ? [sort] : [],
  };

  const execute = (p = 1) => {
    setPage(p);
    run.mutate({ definition, page: p, pageSize: 50, language: i18n.language });
  };

  const exportAs = async (format: 'xlsx' | 'pdf') => {
    const { data, error } = await api.POST('/api/v1/reporting/reports/export', {
      body: { definition, format, language: i18n.language, title: name || current?.labelEn || 'report' },
      parseAs: 'blob',
    });
    if (data) {
      const url = URL.createObjectURL(data);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${name || 'report'}.${format}`;
      a.click();
      URL.revokeObjectURL(url);
    } else {
      toast({ tone: 'error', title: errorMessage(tc, error) });
    }
  };

  if (datasets.isPending || (reportId && report.isPending)) return <Skeleton className="h-96" />;
  const result = run.data;
  return (
    <div className="flex flex-col gap-6" data-testid="report-builder">
      <PageHeader
        title={report.data?.name ?? t('builder.title')}
        description={t('builder.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button
              onClick={() => execute(1)}
              disabled={!dataset || columns.length === 0 || run.isPending}
              data-testid="report-run"
            >
              <Play className="size-4" aria-hidden />
              {t('builder.run')}
            </Button>
            <Button
              variant="outline"
              onClick={() => void exportAs('xlsx')}
              disabled={!dataset || columns.length === 0}
              data-testid="report-export-xlsx"
            >
              <Download className="size-4" aria-hidden />
              Excel
            </Button>
            <Button
              variant="outline"
              onClick={() => void exportAs('pdf')}
              disabled={!dataset || columns.length === 0}
            >
              <Download className="size-4" aria-hidden />
              PDF
            </Button>
            {reportId && canSchedule && (
              <ScheduleDialog targetKind="report" targetId={reportId} defaultName={name} />
            )}
          </div>
        }
      />
      {run.isError && <StatusBanner tone="danger" title={errorMessage(tc, run.error)} />}
      <div className="grid gap-6 lg:grid-cols-3">
        <div className="flex flex-col gap-4 rounded-lg border bg-card p-4">
          <FormField label={t('builder.object')}>
            <Select
              aria-label={t('builder.object')}
              value={dataset}
              onValueChange={(v) => {
                setDataset(v);
                setColumns([]);
                setFilters([]);
                setGroupBy([]);
                setSort(null);
              }}
              placeholder={t('builder.chooseObject')}
              options={(datasets.data ?? []).map((d) => ({
                value: d.code,
                label: pickText(i18n.language, d.labelAr, d.labelEn),
              }))}
            />
          </FormField>
          {current && (
            <>
              <fieldset className="flex flex-col gap-2" data-testid="report-columns">
                <legend className="mb-1 text-sm font-medium">{t('builder.columns')}</legend>
                {fields.map((f) => (
                  <Checkbox
                    key={f.code}
                    label={label(f)}
                    checked={columns.includes(f.code)}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      setColumns((cols) => (checked ? [...cols, f.code] : cols.filter((c) => c !== f.code)));
                    }}
                  />
                ))}
              </fieldset>
              <div className="flex flex-col gap-2">
                <span className="text-sm font-medium">{t('builder.filters')}</span>
                {filters.map((filter, index) => {
                  const field = fields.find((f) => f.code === filter.field);
                  const update = (patch: Partial<FilterRow>) =>
                    setFilters((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
                  return (
                    <div key={index} className="flex flex-col gap-1 rounded border p-2">
                      <Select
                        aria-label={t('builder.field')}
                        value={filter.field || undefined}
                        onValueChange={(v) =>
                          update({
                            field: v,
                            operator:
                              operatorsFor(fields.find((f) => f.code === v)?.type ?? 'text')[0] ?? 'eq',
                            values: [],
                          })
                        }
                        options={fields
                          .filter((f) => f.filterable)
                          .map((f) => ({ value: f.code, label: label(f) }))}
                      />
                      {field && (
                        <Select
                          aria-label={t('builder.operator')}
                          value={filter.operator}
                          onValueChange={(v) => update({ operator: v })}
                          options={operatorsFor(field.type).map((op) => ({
                            value: op,
                            label: t(`builder.operators.${op}` as 'builder.operators.eq'),
                          }))}
                        />
                      )}
                      {field && !['empty', 'notEmpty'].includes(filter.operator) && (
                        <Input
                          aria-label={t('builder.value')}
                          value={filter.values.join(filter.operator === 'in' ? ',' : '|')}
                          placeholder={
                            filter.operator === 'between'
                              ? t('builder.betweenHint')
                              : filter.operator === 'in'
                                ? t('builder.inHint')
                                : ''
                          }
                          onChange={(e) =>
                            update({
                              values: e.target.value

                                .split(
                                  filter.operator === 'in' ? ',' : filter.operator === 'between' ? '|' : '\n',
                                )
                                .map((v) => v.trim())
                                .filter((v, i) => v.length > 0 || i === 0),
                            })
                          }
                        />
                      )}
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => setFilters((rows) => rows.filter((_, i) => i !== index))}
                      >
                        <Trash2 className="size-4" aria-hidden />
                        {t('builder.removeFilter')}
                      </Button>
                    </div>
                  );
                })}
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => setFilters((rows) => [...rows, { field: '', operator: '', values: [] }])}
                >
                  <Plus className="size-4" aria-hidden />
                  {t('builder.addFilter')}
                </Button>
              </div>
              <FormField label={t('builder.groupBy')}>
                <Select
                  aria-label={t('builder.groupBy')}
                  value={groupBy[0] ?? '-'}
                  onValueChange={(v) => setGroupBy(v === '-' ? [] : [v])}
                  options={[
                    { value: '-', label: t('builder.noGrouping') },
                    ...fields.filter((f) => f.groupable).map((f) => ({ value: f.code, label: label(f) })),
                  ]}
                />
              </FormField>
              <FormField label={t('builder.sort')}>
                <div className="flex gap-2">
                  <Select
                    aria-label={t('builder.sort')}
                    value={sort?.field ?? '-'}
                    onValueChange={(v) =>
                      setSort(v === '-' ? null : { field: v, descending: sort?.descending ?? false })
                    }
                    options={[
                      { value: '-', label: t('builder.noSort') },
                      ...fields.filter((f) => f.sortable).map((f) => ({ value: f.code, label: label(f) })),
                    ]}
                  />
                  <Select
                    aria-label={t('builder.direction')}
                    value={sort?.descending ? 'desc' : 'asc'}
                    onValueChange={(v) => sort && setSort({ ...sort, descending: v === 'desc' })}
                    options={[
                      { value: 'asc', label: t('builder.ascending') },
                      { value: 'desc', label: t('builder.descending') },
                    ]}
                  />
                </div>
              </FormField>
              {canBuild && (
                <div className="flex flex-col gap-2 border-t pt-3">
                  <FormField label={t('builder.name')}>
                    <Input
                      value={name}
                      onChange={(e) => setName(e.target.value)}
                      maxLength={200}
                      data-testid="report-name"
                    />
                  </FormField>
                  <Button
                    variant="outline"
                    disabled={!name.trim() || columns.length === 0 || save.isPending}
                    data-testid="report-save"
                    onClick={() =>
                      save.mutate(
                        { name, definition, version: report.data?.version },
                        {
                          onSuccess: (saved) => {
                            toast({ title: t('builder.saved') });
                            if (!reportId)
                              void navigate({ to: '/reports/$reportId', params: { reportId: saved.id } });
                          },
                          onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                        },
                      )
                    }
                  >
                    <Save className="size-4" aria-hidden />
                    {t('builder.save')}
                  </Button>
                </div>
              )}
              {reportId && canShare && report.data?.canEdit && (
                <div className="flex flex-col gap-2 border-t pt-3">
                  <span className="text-sm font-medium">{t('builder.share')}</span>
                  <PrincipalListEditor value={shares} onChange={setShares} />
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      share.mutate(
                        {
                          id: reportId,
                          shares: shares.map((s) => ({ principalKind: s.kind, principalRef: s.ref })),
                        },
                        { onSuccess: () => toast({ title: t('builder.shared') }) },
                      )
                    }
                  >
                    <Share2 className="size-4" aria-hidden />
                    {t('builder.applyShare')}
                  </Button>
                  <p className="text-xs text-muted-foreground">{t('builder.shareHint')}</p>
                </div>
              )}
            </>
          )}
        </div>
        <div className="flex flex-col gap-3 lg:col-span-2">
          {result && result.droppedColumns.length > 0 && (
            <StatusBanner tone="info" title={t('builder.dropped', { count: result.droppedColumns.length })} />
          )}
          {!result ? (
            <EmptyState title={t('builder.previewEmpty')} />
          ) : (
            <>
              <div className="overflow-x-auto">
                <Table data-testid="report-preview">
                  <TableHead>
                    <TableRow>
                      {result.columns.map((c) => (
                        <TableHeaderCell key={c.code}>
                          {pickText(i18n.language, c.labelAr, c.labelEn)}
                        </TableHeaderCell>
                      ))}
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {result.rows.map((row, index) => (
                      <TableRow key={row.id ?? index}>
                        {row.cells.map((cell, i) => (
                          <TableCell key={i} dir="auto">
                            {i === 0 && row.link ? (
                              <Link to={row.link as '/'} className="text-primary hover:underline">
                                {cell || '—'}
                              </Link>
                            ) : (
                              cell || '—'
                            )}
                          </TableCell>
                        ))}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
              <Pagination
                page={result.page}
                totalPages={Math.max(1, Math.ceil(result.total / result.pageSize))}
                totalCount={result.total}
                onPageChange={(p) => execute(p)}
              />
            </>
          )}
        </div>
      </div>
      <span className="sr-only">{page}</span>
    </div>
  );
}
