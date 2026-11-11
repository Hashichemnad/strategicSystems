import { Link, useNavigate } from '@tanstack/react-router';
import { Download, FolderPlus, Lock, Play, Plus, Save, Share2, Trash2, Users } from 'lucide-react';
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
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { useReport, useReports, useShareReport } from './api';
import {
  useFolders,
  useMoveReport,
  useReportTypes,
  useRunTypeReport,
  useSaveFolder,
  useSaveTypeReport,
  useShareFolder,
  saveBlob,
  type ReportFilter,
  type ReportHighlight,
  type ReportSummary,
  type ReportTypeField,
  type TypeDefinition,
  type UsableReportType,
} from './api2';
import { ReportChartView, ReportResultTable } from './ReportResultView';
import { ScheduleDialog } from './ScheduleDialog';
import { PrincipalListEditor, type PrincipalRef } from './shared';

const operatorsByType: Record<string, string[]> = {
  text: ['contains', 'eq', 'ne', 'in', 'empty', 'notEmpty'],
  number: ['eq', 'ne', 'gte', 'lte', 'between', 'in', 'empty', 'notEmpty'],
  money: ['eq', 'gte', 'lte', 'between', 'empty', 'notEmpty'],
  ratio: ['gte', 'lte', 'between', 'empty', 'notEmpty'],
  date: ['relative', 'eq', 'gte', 'lte', 'between', 'empty', 'notEmpty'],
  dateTime: ['relative', 'gte', 'lte', 'between', 'empty', 'notEmpty'],
  bool: ['eq'],
  lookup: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
  unit: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
  employee: ['eq', 'ne', 'in', 'empty', 'notEmpty'],
  richText: ['empty', 'notEmpty'],
};

/** Operators valid for a field type (mirrors the server's whitelist, ADRs 0144 and 0182). */
export function operatorsFor(type: string): string[] {
  return operatorsByType[type] ?? ['eq'];
}

export const relativeDates = [
  'today',
  'yesterday',
  'thisWeek',
  'lastWeek',
  'thisMonth',
  'lastMonth',
  'nextMonth',
  'thisQuarter',
  'lastQuarter',
  'nextQuarter',
  'thisYear',
  'lastYear',
  'lastDays',
  'nextDays',
] as const;

const numericTypes = ['number', 'money', 'ratio'];

/** A Phase 17 definition (one dataset, unqualified columns) as a type-based one (`dataset:<code>`, alias `a`). */
export function toTypeDefinition(saved: Partial<TypeDefinition> & { dataset?: string }): TypeDefinition {
  if (saved.reportType) {
    return {
      reportType: saved.reportType,
      dataset: saved.dataset ?? '',
      columns: saved.columns ?? [],
      filters: saved.filters ?? [],
      groupBy: saved.groupBy ?? [],
      sort: saved.sort ?? [],
      summaries: saved.summaries ?? [],
      chart: saved.chart ?? null,
      highlights: saved.highlights ?? [],
      showDetails: saved.showDetails ?? true,
    };
  }
  const q = (f: string) => (f.includes('.') ? f : `a.${f}`);
  return {
    reportType: `dataset:${saved.dataset ?? ''}`,
    dataset: '',
    columns: (saved.columns ?? []).map(q),
    filters: (saved.filters ?? []).map((f) => ({ ...f, field: q(f.field) })),
    groupBy: (saved.groupBy ?? []).map(q),
    sort: (saved.sort ?? []).map((s) => ({ ...s, field: q(s.field) })),
    summaries: [],
    chart: null,
    highlights: [],
    showDetails: true,
  };
}

/** "My reports" and reports shared with me, by folder (ADR 0183). */
export function ReportsPage() {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const reports = useReports();
  const folders = useFolders();
  const types = useReportTypes();
  const canShare = useCan('reporting.report.share');
  const canDossier = useCan('reporting.dossier.run');
  const saveFolder = useSaveFolder();
  const shareFolder = useShareFolder();
  const [folder, setFolder] = useState<string>('mine');
  const [newName, setNewName] = useState('');
  const [shares, setShares] = useState<PrincipalRef[]>([]);
  const current = folders.data?.find((f) => f.id === folder);
  const typeLabel = (code: string) => {
    const type = types.data?.find((x) => x.code === code || x.code === `dataset:${code}`);
    return type ? pickText(i18n.language, type.labelAr, type.labelEn) : code;
  };
  const shown = (reports.data ?? []).filter((r) =>
    folder === 'mine'
      ? r.canEdit && !r.folderId
      : folder === 'shared'
        ? !r.canEdit && !folders.data?.some((f) => f.id === r.folderId)
        : r.folderId === folder,
  );
  return (
    <div className="flex flex-col gap-6" data-testid="reports-home">
      <PageHeader
        title={t('home.title')}
        description={t('home.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            {canDossier && (
              <Button asChild variant="outline">
                <Link to="/reports/dossiers">{t('home.dossiers')}</Link>
              </Button>
            )}
            <Button asChild>
              <Link to="/reports/new" data-testid="report-new">
                <Plus className="size-4" aria-hidden />
                {t('home.new')}
              </Link>
            </Button>
          </div>
        }
      />
      <div className="grid gap-6 lg:grid-cols-4">
        <nav
          aria-label={t('home.folders')}
          className="flex flex-col gap-1 rounded-lg border bg-card p-3"
          data-testid="report-folders"
        >
          {[
            { id: 'mine', name: t('home.myReports'), kind: 'private' },
            { id: 'shared', name: t('home.sharedWithMe'), kind: 'shared' },
            ...(folders.data ?? []).map((f) => ({ id: f.id, name: f.name, kind: f.kind })),
          ].map((f) => (
            <button
              key={f.id}
              type="button"
              aria-current={folder === f.id ? 'page' : undefined}
              onClick={() => setFolder(f.id)}
              className="flex items-center gap-2 rounded px-2 py-1.5 text-start text-sm hover:bg-muted aria-[current=page]:bg-muted aria-[current=page]:font-semibold"
            >
              {f.kind === 'shared' ? (
                <Users className="size-4" aria-hidden />
              ) : (
                <Lock className="size-4" aria-hidden />
              )}
              {f.name}
            </button>
          ))}
          <div className="mt-3 flex flex-col gap-2 border-t pt-3">
            <Input
              aria-label={t('home.folderName')}
              placeholder={t('home.folderName')}
              value={newName}
              maxLength={200}
              onChange={(e) => setNewName(e.target.value)}
            />
            <div className="flex flex-wrap gap-2">
              {(['private', ...(canShare ? ['shared'] : [])] as const).map((kind) => (
                <Button
                  key={kind}
                  size="sm"
                  variant="outline"
                  disabled={!newName.trim()}
                  onClick={() =>
                    saveFolder.mutate(
                      { name: newName.trim(), kind: kind as 'private' | 'shared' },
                      {
                        onSuccess: (f) => {
                          setNewName('');
                          setFolder(f.id);
                        },
                        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                      },
                    )
                  }
                >
                  <FolderPlus className="size-4" aria-hidden />
                  {t(kind === 'private' ? 'home.newPrivate' : 'home.newShared')}
                </Button>
              ))}
            </div>
          </div>
        </nav>
        <div className="flex flex-col gap-4 lg:col-span-3">
          {current?.kind === 'shared' && current.canEdit && (
            <div className="flex flex-col gap-2 rounded-lg border bg-card p-3">
              <span className="text-sm font-medium">{t('home.shareFolder')}</span>
              <PrincipalListEditor value={shares} onChange={setShares} />
              <Button
                size="sm"
                variant="outline"
                className="self-start"
                onClick={() =>
                  shareFolder.mutate(
                    {
                      id: current.id,
                      shares: shares.map((s) => ({ principalKind: s.kind, principalRef: s.ref })),
                    },
                    { onSuccess: () => toast({ title: t('home.folderShared') }) },
                  )
                }
              >
                <Share2 className="size-4" aria-hidden />
                {t('home.applyShare')}
              </Button>
            </div>
          )}
          {reports.isPending ? (
            <Skeleton className="h-40" />
          ) : shown.length > 0 ? (
            <Table data-testid="report-list">
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('home.name')}</TableHeaderCell>
                  <TableHeaderCell>{t('home.type')}</TableHeaderCell>
                  <TableHeaderCell>{t('home.owner')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {shown.map((report) => (
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
                    <TableCell>{typeLabel(report.dataset)}</TableCell>
                    <TableCell>{report.canEdit ? t('home.mine') : t('home.sharedByOthers')}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <EmptyState title={t('home.empty')} />
          )}
        </div>
      </div>
    </div>
  );
}

interface FilterRow extends ReportFilter {
  key: number;
}

/** Report builder (SPEC-ADDENDUM E2, ADRs 0181–0183): report type → columns per object → filters → grouping → summaries → chart → highlights. */
export function ReportBuilderPage({ reportId }: { reportId?: string }) {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const types = useReportTypes();
  const folders = useFolders();
  const report = useReport(reportId);
  const run = useRunTypeReport();
  const save = useSaveTypeReport(reportId);
  const move = useMoveReport();
  const share = useShareReport();
  const canShare = useCan('reporting.report.share');
  const canSchedule = useCan('reporting.schedule.manage');
  const [def, setDef] = useState<TypeDefinition>(() => toTypeDefinition({}));
  const [filters, setFilters] = useState<FilterRow[]>([]);
  const [name, setName] = useState('');
  const [folderId, setFolderId] = useState<string>('-');
  const [shares, setShares] = useState<PrincipalRef[]>([]);
  const [page, setPage] = useState(1);

  useEffect(() => {
    if (report.data) {
      const loaded = toTypeDefinition(report.data.definition as Partial<TypeDefinition>);
      setDef(loaded);
      setFilters(loaded.filters.map((f, key) => ({ ...f, key })));
      setName(report.data.name);
      setFolderId(report.data.folderId ?? '-');
    }
  }, [report.data]);

  const type: UsableReportType | undefined = types.data?.find((x) => x.code === def.reportType);
  const fields = useMemo(
    () => (type?.objects ?? []).flatMap((o) => o.fields.map((f) => ({ ...f, object: o }))),
    [type],
  );
  const byCode = useMemo(() => new Map(fields.map((f) => [f.code, f])), [fields]);
  const label = (f: ReportTypeField & { object?: { labelAr: string; labelEn: string } }) =>
    (f.object && (type?.objects.length ?? 0) > 1
      ? pickText(i18n.language, f.object.labelAr, f.object.labelEn) + ' › '
      : '') + pickText(i18n.language, f.labelAr, f.labelEn);
  const fieldLabel = (code: string) => {
    const field = byCode.get(code);
    return field ? label(field) : code;
  };
  const definition: TypeDefinition = {
    ...def,
    filters: filters
      .filter((f) => f.field && f.operator)
      .map(({ field, operator, values }) => ({ field, operator, values })),
  };
  const patch = (p: Partial<TypeDefinition>) => setDef((d) => ({ ...d, ...p }));
  const execute = (p = 1) => {
    setPage(p);
    run.mutate({ definition, page: p, pageSize: 50, language: i18n.language });
  };
  const exportAs = async (format: 'xlsx' | 'pdf' | 'csv') => {
    const title = name || (type ? pickText(i18n.language, type.labelAr, type.labelEn) : 'report');
    const { data, error } = await api.POST('/api/v1/reporting/reports/export', {
      body: { definition, format, language: i18n.language, title },
      parseAs: 'blob',
    });
    if (data) saveBlob(data, `${name || 'report'}.${format}`);
    else toast({ tone: 'error', title: errorMessage(tc, error) });
  };

  if (types.isPending || (reportId && report.isPending)) return <Skeleton className="h-96" />;
  const result = run.data;
  const groupable = fields.filter((f) => f.groupable);
  const numericColumns = def.columns.filter((c) => numericTypes.includes(byCode.get(c)?.type ?? ''));
  const ready = !!type && def.columns.length > 0;
  const objectLabel = (o: { labelAr: string; labelEn: string }) =>
    pickText(i18n.language, o.labelAr, o.labelEn);
  return (
    <div className="flex flex-col gap-6" data-testid="report-builder">
      <PageHeader
        title={report.data?.name ?? t('builder.title')}
        description={t('builder.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => execute(1)} disabled={!ready || run.isPending} data-testid="report-run">
              <Play className="size-4" aria-hidden />
              {t('builder.run')}
            </Button>
            {(['xlsx', 'pdf', 'csv'] as const).map((format) => (
              <Button
                key={format}
                variant="outline"
                onClick={() => void exportAs(format)}
                disabled={!ready}
                data-testid={`report-export-${format}`}
              >
                <Download className="size-4" aria-hidden />
                {format === 'xlsx' ? 'Excel' : format.toUpperCase()}
              </Button>
            ))}
            {reportId && canSchedule && (
              <ScheduleDialog targetKind="report" targetId={reportId} defaultName={name} />
            )}
          </div>
        }
      />
      {run.isError && <StatusBanner tone="danger" title={errorMessage(tc, run.error)} />}
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="flex flex-col gap-4 rounded-lg border bg-card p-4">
          <FormField label={t('builder.reportType')}>
            <Select
              aria-label={t('builder.reportType')}
              value={def.reportType.length > 'dataset:'.length ? def.reportType : undefined}
              onValueChange={(v) => {
                setDef({ ...toTypeDefinition({}), reportType: v });
                setFilters([]);
              }}
              placeholder={t('builder.chooseType')}
              options={(types.data ?? []).map((x) => ({
                value: x.code,
                label:
                  (x.implicit ? t('builder.singleObject') + ' — ' : '') +
                  pickText(i18n.language, x.labelAr, x.labelEn),
              }))}
            />
          </FormField>
          {type && (
            <>
              {type.objects.length > 1 && (
                <ul
                  className="flex flex-col gap-1 text-xs text-muted-foreground"
                  data-testid="report-type-objects"
                >
                  {type.objects.map((o) => (
                    <li key={o.alias}>
                      {o.parent
                        ? (o.join === 'inner' ? t('builder.with') : t('builder.withOrWithout')) + ' '
                        : ''}
                      <span className="font-medium text-foreground">{objectLabel(o)}</span>
                      {!o.readable && ` — ${t('builder.notReadable')}`}
                    </li>
                  ))}
                </ul>
              )}
              {type.objects.map((o) => (
                <fieldset
                  key={o.alias}
                  className="flex flex-col gap-1.5"
                  data-testid={`report-columns-${o.alias}`}
                >
                  <legend className="mb-1 text-sm font-medium">
                    {t('builder.columnsOf', { object: objectLabel(o) })}
                  </legend>
                  {o.fields.map((f) => (
                    <Checkbox
                      key={f.code}
                      label={pickText(i18n.language, f.labelAr, f.labelEn)}
                      checked={def.columns.includes(f.code)}
                      onChange={(e) => {
                        const checked = e.target.checked;
                        patch({
                          columns: checked
                            ? [...def.columns, f.code]
                            : def.columns.filter((c) => c !== f.code),
                        });
                      }}
                    />
                  ))}
                </fieldset>
              ))}

              <section className="flex flex-col gap-2" aria-label={t('builder.filters')}>
                <span className="text-sm font-medium">{t('builder.filters')}</span>
                {filters.map((filter) => {
                  const field = byCode.get(filter.field);
                  const update = (p: Partial<FilterRow>) =>
                    setFilters((rows) =>
                      rows.map((row) => (row.key === filter.key ? { ...row, ...p } : row)),
                    );
                  return (
                    <div key={filter.key} className="flex flex-col gap-1 rounded border p-2">
                      <Select
                        aria-label={t('builder.field')}
                        value={filter.field || undefined}
                        onValueChange={(v) =>
                          update({
                            field: v,
                            operator: operatorsFor(byCode.get(v)?.type ?? 'text')[0] ?? 'eq',
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
                          onValueChange={(v) =>
                            update({ operator: v, values: v === 'relative' ? ['thisMonth'] : [] })
                          }
                          options={operatorsFor(field.type).map((op) => ({
                            value: op,
                            label: t(`operators.${op}` as 'operators.eq'),
                          }))}
                        />
                      )}
                      {field && filter.operator === 'relative' && (
                        <div className="flex gap-2">
                          <Select
                            aria-label={t('builder.relative')}
                            value={filter.values[0] ?? 'thisMonth'}
                            onValueChange={(v) =>
                              update({ values: v === 'lastDays' || v === 'nextDays' ? [v, '30'] : [v] })
                            }
                            options={relativeDates.map((r) => ({ value: r, label: t(`relative.${r}`) }))}
                          />
                          {(filter.values[0] === 'lastDays' || filter.values[0] === 'nextDays') && (
                            <Input
                              aria-label={t('builder.days')}
                              type="number"
                              min={1}
                              className="w-24"
                              value={filter.values[1] ?? '30'}
                              onChange={(e) =>
                                update({ values: [filter.values[0] ?? 'lastDays', e.target.value] })
                              }
                            />
                          )}
                        </div>
                      )}
                      {field && !['empty', 'notEmpty', 'relative'].includes(filter.operator) && (
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
                        onClick={() => setFilters((rows) => rows.filter((row) => row.key !== filter.key))}
                      >
                        <Trash2 className="size-4" aria-hidden />
                        {t('builder.remove')}
                      </Button>
                    </div>
                  );
                })}
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    setFilters((rows) => [...rows, { key: Date.now(), field: '', operator: '', values: [] }])
                  }
                >
                  <Plus className="size-4" aria-hidden />
                  {t('builder.addFilter')}
                </Button>
              </section>

              <fieldset className="flex flex-col gap-2" data-testid="report-grouping">
                <legend className="mb-1 text-sm font-medium">{t('builder.groupBy')}</legend>
                {[0, 1, 2].map((level) =>
                  level <= def.groupBy.length ? (
                    <Select
                      key={level}
                      aria-label={t('builder.groupLevel', { level: level + 1 })}
                      value={def.groupBy[level] ?? '-'}
                      onValueChange={(v) => {
                        const next = def.groupBy.slice(0, level);
                        if (v !== '-') next.push(v);
                        patch({ groupBy: next });
                      }}
                      options={[
                        { value: '-', label: t('builder.noGrouping') },
                        ...groupable
                          .filter((f) => !def.groupBy.slice(0, level).includes(f.code))
                          .map((f) => ({ value: f.code, label: label(f) })),
                      ]}
                    />
                  ) : null,
                )}
                {def.groupBy.length > 0 && (
                  <Switch
                    label={t('builder.showDetails')}
                    checked={def.showDetails}
                    onCheckedChange={(checked) => patch({ showDetails: checked })}
                  />
                )}
              </fieldset>

              <FormField label={t('builder.sort')}>
                <div className="flex gap-2">
                  <Select
                    aria-label={t('builder.sort')}
                    value={def.sort[0]?.field ?? '-'}
                    onValueChange={(v) =>
                      patch({
                        sort: v === '-' ? [] : [{ field: v, descending: def.sort[0]?.descending ?? false }],
                      })
                    }
                    options={[
                      { value: '-', label: t('builder.noSort') },
                      ...fields.filter((f) => f.sortable).map((f) => ({ value: f.code, label: label(f) })),
                    ]}
                  />
                  <Select
                    aria-label={t('builder.direction')}
                    value={def.sort[0]?.descending ? 'desc' : 'asc'}
                    onValueChange={(v) =>
                      def.sort[0] && patch({ sort: [{ ...def.sort[0], descending: v === 'desc' }] })
                    }
                    options={[
                      { value: 'asc', label: t('builder.ascending') },
                      { value: 'desc', label: t('builder.descending') },
                    ]}
                  />
                </div>
              </FormField>

              {numericColumns.length > 0 && (
                <fieldset className="flex flex-col gap-1.5" data-testid="report-summaries">
                  <legend className="mb-1 text-sm font-medium">{t('builder.summaries')}</legend>
                  {numericColumns.map((column) => {
                    const current: ReportSummary | undefined = def.summaries.find((s) => s.column === column);
                    const functions = current?.functions ?? [];
                    return (
                      <div key={column} className="flex flex-wrap items-center gap-3">
                        <span className="min-w-28 text-sm">{fieldLabel(column)}</span>
                        {(['sum', 'avg', 'min', 'max'] as const)
                          .filter((fn) => fn !== 'sum' || byCode.get(column)?.type !== 'ratio')
                          .map((fn) => (
                            <Checkbox
                              key={fn}
                              label={t(`summary.${fn}`)}
                              checked={functions.includes(fn)}
                              onChange={(e) => {
                                const next = e.target.checked
                                  ? [...functions, fn]
                                  : functions.filter((x) => x !== fn);
                                patch({
                                  summaries: [
                                    ...def.summaries.filter((s) => s.column !== column),
                                    ...(next.length > 0 ? [{ column, functions: next }] : []),
                                  ],
                                });
                              }}
                            />
                          ))}
                      </div>
                    );
                  })}
                </fieldset>
              )}

              <fieldset className="flex flex-col gap-2" data-testid="report-chart-settings">
                <legend className="mb-1 text-sm font-medium">{t('chart.title')}</legend>
                <Select
                  aria-label={t('chart.type')}
                  value={def.chart?.type ?? '-'}
                  onValueChange={(v) =>
                    patch({
                      chart:
                        v === '-'
                          ? null
                          : {
                              type: v,
                              category: def.chart?.category ?? def.groupBy[0] ?? groupable[0]?.code ?? '',
                              series:
                                v === 'stackedBar' ? (def.chart?.series ?? def.groupBy[1] ?? null) : null,
                              measure: def.chart?.measure ?? null,
                              aggregate: def.chart?.aggregate ?? 'sum',
                            },
                    })
                  }
                  options={[
                    { value: '-', label: t('chart.none') },
                    ...(['bar', 'stackedBar', 'line', 'pie', 'donut'] as const).map((c) => ({
                      value: c,
                      label: t(`chart.types.${c}`),
                    })),
                  ]}
                />
                {def.chart && (
                  <>
                    <Select
                      aria-label={t('chart.category')}
                      value={def.chart.category || undefined}
                      onValueChange={(v) => def.chart && patch({ chart: { ...def.chart, category: v } })}
                      options={groupable.map((f) => ({ value: f.code, label: label(f) }))}
                    />
                    {def.chart.type === 'stackedBar' && (
                      <Select
                        aria-label={t('chart.series')}
                        value={def.chart.series ?? '-'}
                        onValueChange={(v) =>
                          def.chart && patch({ chart: { ...def.chart, series: v === '-' ? null : v } })
                        }
                        options={[
                          { value: '-', label: t('chart.noSeries') },
                          ...groupable.map((f) => ({ value: f.code, label: label(f) })),
                        ]}
                      />
                    )}
                    <div className="flex gap-2">
                      <Select
                        aria-label={t('chart.measure')}
                        value={def.chart.measure ?? '-'}
                        onValueChange={(v) =>
                          def.chart && patch({ chart: { ...def.chart, measure: v === '-' ? null : v } })
                        }
                        options={[
                          { value: '-', label: t('chart.count') },
                          ...fields
                            .filter((f) => numericTypes.includes(f.type))
                            .map((f) => ({ value: f.code, label: label(f) })),
                        ]}
                      />
                      {def.chart.measure && (
                        <Select
                          aria-label={t('chart.aggregate')}
                          value={def.chart.aggregate ?? 'sum'}
                          onValueChange={(v) => def.chart && patch({ chart: { ...def.chart, aggregate: v } })}
                          options={(['sum', 'avg', 'min', 'max'] as const).map((fn) => ({
                            value: fn,
                            label: t(`summary.${fn}`),
                          }))}
                        />
                      )}
                    </div>
                  </>
                )}
              </fieldset>

              <section
                className="flex flex-col gap-2"
                aria-label={t('builder.highlights')}
                data-testid="report-highlights"
              >
                <span className="text-sm font-medium">{t('builder.highlights')}</span>
                {def.highlights.map((h, index) => {
                  const update = (p: Partial<ReportHighlight>) =>
                    patch({ highlights: def.highlights.map((x, i) => (i === index ? { ...x, ...p } : x)) });
                  return (
                    <div key={index} className="flex flex-col gap-1 rounded border p-2">
                      <Select
                        aria-label={t('builder.field')}
                        value={h.column || undefined}
                        onValueChange={(v) => update({ column: v })}
                        options={def.columns
                          .filter((c) => byCode.has(c))
                          .map((c) => ({ value: c, label: fieldLabel(c) }))}
                      />
                      <div className="flex gap-2">
                        <Select
                          aria-label={t('builder.operator')}
                          value={h.operator ?? 'gte'}
                          onValueChange={(v) => update({ operator: v })}
                          options={['eq', 'ne', 'gte', 'lte', 'between', 'contains', 'empty', 'notEmpty'].map(
                            (op) => ({
                              value: op,
                              label: t(`operators.${op}` as 'operators.eq'),
                            }),
                          )}
                        />
                        <Input
                          aria-label={t('builder.value')}
                          value={(h.values ?? []).join('|')}
                          onChange={(e) => update({ values: e.target.value.split('|').map((v) => v.trim()) })}
                        />
                      </div>
                      <div className="flex gap-2">
                        <Select
                          aria-label={t('builder.tone')}
                          value={h.tone ?? 'bad'}
                          onValueChange={(v) => update({ tone: v })}
                          options={(['good', 'warning', 'bad', 'info'] as const).map((tone) => ({
                            value: tone,
                            label: t(`tones.${tone}`),
                          }))}
                        />
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => patch({ highlights: def.highlights.filter((_, i) => i !== index) })}
                        >
                          <Trash2 className="size-4" aria-hidden />
                          {t('builder.remove')}
                        </Button>
                      </div>
                    </div>
                  );
                })}
                <Button
                  size="sm"
                  variant="outline"
                  disabled={def.columns.length === 0}
                  onClick={() =>
                    patch({
                      highlights: [
                        ...def.highlights,
                        { column: def.columns[0] ?? '', operator: 'gte', values: [], tone: 'bad' },
                      ],
                    })
                  }
                >
                  <Plus className="size-4" aria-hidden />
                  {t('builder.addHighlight')}
                </Button>
              </section>

              <div className="flex flex-col gap-2 border-t pt-3">
                <FormField label={t('builder.name')}>
                  <Input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    data-testid="report-name"
                  />
                </FormField>
                <FormField label={t('builder.folder')}>
                  <Select
                    aria-label={t('builder.folder')}
                    value={folderId}
                    onValueChange={setFolderId}
                    options={[
                      { value: '-', label: t('home.myReports') },
                      ...(folders.data ?? [])
                        .filter((f) => f.canEdit && (f.kind === 'private' || canShare))
                        .map((f) => ({
                          value: f.id,
                          label: f.name + (f.kind === 'shared' ? ` (${t('home.shared')})` : ''),
                        })),
                    ]}
                  />
                </FormField>
                {(!report.data || report.data.canEdit) && (
                  <Button
                    variant="outline"
                    disabled={!name.trim() || !ready || save.isPending}
                    data-testid="report-save"
                    onClick={() =>
                      save.mutate(
                        {
                          name,
                          definition,
                          folderId: folderId === '-' ? null : folderId,
                          version: report.data?.version,
                        },
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
                )}
                {reportId && report.data?.canEdit && report.data.folderId && (
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => move.mutate({ id: reportId, folderId: null })}
                  >
                    {t('builder.moveToMine')}
                  </Button>
                )}
              </div>
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
        <div className="flex min-w-0 flex-col gap-3 xl:col-span-2">
          {result && result.droppedColumns.length > 0 && (
            <StatusBanner tone="info" title={t('builder.dropped', { n: result.droppedColumns.length })} />
          )}
          {result?.truncated && <StatusBanner tone="warning" title={t('builder.truncated')} />}
          {!result ? (
            <EmptyState title={t('builder.previewEmpty')} />
          ) : (
            <>
              <ReportChartView run={result} />
              <ReportResultTable run={result} />
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
