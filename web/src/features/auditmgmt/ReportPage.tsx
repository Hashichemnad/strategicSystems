import { getRouteApi, Link } from '@tanstack/react-router';
import { Ban, Download, FileSpreadsheet, Pencil, Plus, Send, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { useCan } from '@/features/access/session';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { UnitSelect } from '@/features/access/pickers';
import {
  Button,
  Checkbox,
  DatePicker,
  FileDrop,
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import {
  auditApi,
  auditTemplateUrl,
  downloadExport,
  lookupTypes,
  useAuditFindings,
  useAuditReport,
  type AuditImportReport,
  type AuditReport,
  type ExtractedFinding,
} from './api';
import {
  AuditPageLayout,
  FindingStatusChip,
  PctBar,
  PriorityChip,
  ReportStatusChip,
  Rows,
  Section,
  SimpleDialog,
  isoToday,
  useAuditLabels,
  useAuditT,
  useDate,
  useDialogMutation,
  useNames,
  useToastMutation,
} from './components';
import { FindingDialog, ReasonDialog, ReportDialog } from './forms';

const route = getRouteApi('/app/audit-management/reports/$reportId');

type Dialog = 'edit' | 'finding' | 'cancel' | 'import' | 'extract';

/** Audit report: header, findings register, file, distribution, Excel import and AI extraction (drafts confirmed by the Audit Office). */
export function AuditReportPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { language } = useAuditT();
  const { reportId } = route.useParams();
  const report = useAuditReport(reportId);
  const findings = useAuditFindings({ reportId, pageSize: 200, view: 'all', sort: 'number' });
  const labels = useAuditLabels();
  const names = useNames();
  const date = useDate();
  const issue = useToastMutation(auditApi.issueReport);
  const [dialog, setDialog] = useState<Dialog>();
  const extractOn = useAiFeature('auditmgmt-extract');
  const canExtract = useCan('auditmgmt.ai.extract');
  const canExport = useCan('auditmgmt.report.export');
  const close = () => setDialog(undefined);

  if (report.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (report.isError) return <StatusBanner tone="danger" title={errorMessage(tc, report.error)} />;
  const r = report.data;
  const items = findings.data?.items ?? [];

  return (
    <AuditPageLayout testId="audit-report-page">
      <PageHeader
        title={<bdi data-testid="audit-report-number">{r.number}</bdi>}
        description={names.text(r.values.titleAr, r.values.titleEn)}
        meta={
          <div className="flex flex-wrap items-center gap-3">
            <ReportStatusChip status={r.status} />
            <span className="text-sm text-muted-foreground">
              {labels.lookup(lookupTypes.reportType, r.values.typeCode)}
            </span>
            <span className="text-sm text-muted-foreground">
              {t('reports.counts', {
                total: r.findings,
                open: r.openFindings,
                late: 0,
                closed: r.closedFindings,
              })}
            </span>
          </div>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {r.can.issue && (
              <Button loading={issue.busy} onClick={() => issue.run(r.id)} data-testid="audit-report-issue">
                <Send aria-hidden />
                {t('report.issue')}
              </Button>
            )}
            {r.can.addFinding && (
              <>
                <Button
                  variant="outline"
                  onClick={() => setDialog('finding')}
                  data-testid="audit-add-finding"
                >
                  <Plus aria-hidden />
                  {t('finding.add')}
                </Button>
                <Button variant="outline" onClick={() => setDialog('import')} data-testid="audit-import">
                  <FileSpreadsheet aria-hidden />
                  {t('import.title')}
                </Button>
                {extractOn && canExtract && (
                  <Button
                    variant="outline"
                    onClick={() => setDialog('extract')}
                    data-testid="audit-ai-extract"
                  >
                    <Sparkles aria-hidden />
                    {t('ai.extract')}
                  </Button>
                )}
              </>
            )}
            {canExport && (
              <Button
                variant="outline"
                onClick={() => void downloadExport('register', { format: 'xlsx', language, reportId: r.id })}
                data-testid="audit-export-register"
              >
                <Download aria-hidden />
                {t('exports.register')}
              </Button>
            )}
            {r.can.edit && (
              <Button variant="ghost" onClick={() => setDialog('edit')}>
                <Pencil aria-hidden />
                {tc('actions.edit')}
              </Button>
            )}
            {r.can.cancel && (
              <Button variant="ghost" onClick={() => setDialog('cancel')}>
                <Ban aria-hidden />
                {t('report.cancel')}
              </Button>
            )}
          </div>
        }
      />
      {r.status === 'draft' && <StatusBanner tone="info" title={t('report.draftHint')} />}
      <Tabs defaultValue="findings">
        <TabsList>
          <TabsTrigger value="findings">{t('tabs.findings')}</TabsTrigger>
          <TabsTrigger value="details">{t('tabs.details')}</TabsTrigger>
          <TabsTrigger value="documents">{t('tabs.documents')}</TabsTrigger>
          <TabsTrigger value="history">{t('tabs.history')}</TabsTrigger>
        </TabsList>
        <TabsContent value="findings">
          <Section title={t('report.register')} testId="audit-report-findings">
            {findings.isPending ? (
              <Skeleton className="h-40" />
            ) : items.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('report.noFindings')}</p>
            ) : (
              <Table>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.responsibleUnit')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.priority')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.completion')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.nextDue')}</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {items.map((f) => (
                    <TableRow key={f.id}>
                      <TableCell>
                        <Link
                          to="/audit-management/findings/$findingId"
                          params={{ findingId: f.id }}
                          className="text-primary hover:underline"
                        >
                          <bdi>{f.number}</bdi>
                        </Link>
                      </TableCell>
                      <TableCell>{names.text(f.titleAr, f.titleEn)}</TableCell>
                      <TableCell>{names.unit(f.responsibleUnit)}</TableCell>
                      <TableCell>
                        <PriorityChip priority={f.priority} />
                      </TableCell>
                      <TableCell>
                        <FindingStatusChip status={f.status} />
                      </TableCell>
                      <TableCell>
                        <PctBar value={f.completion} late={f.status === 'late'} />
                      </TableCell>
                      <TableCell>{date(f.nextDueOn)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </Section>
        </TabsContent>
        <TabsContent value="details">
          <Details report={r} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel ownerType="auditmgmt.report" ownerId={r.id} canEdit={r.can.edit} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="auditReport" id={r.id} />
        </TabsContent>
      </Tabs>
      {dialog === 'edit' && (
        <ReportDialog reportId={r.id} initial={r.values} version={r.version} onClose={close} />
      )}
      {dialog === 'finding' && <FindingDialog reportId={r.id} onClose={close} />}
      {dialog === 'cancel' && (
        <ReasonDialog
          title={t('report.cancel')}
          submitLabel={t('report.cancel')}
          testId="audit-report-cancel-save"
          run={(reason) => auditApi.cancelReport({ id: r.id, reason })}
          onClose={close}
        />
      )}
      {dialog === 'import' && <ImportDialog reportId={r.id} onClose={close} />}
      {dialog === 'extract' && <ExtractDialog report={r} onClose={close} />}
    </AuditPageLayout>
  );
}

function Details({ report: r }: { report: AuditReport }) {
  const { t } = useTranslation('auditmgmt');
  const names = useNames();
  const date = useDate();
  return (
    <Rows
      rows={[
        [t('fields.auditorBody'), r.values.auditorBody],
        [t('fields.leadAuditor'), names.person(r.leadAuditor)],
        [t('fields.reportDate'), date(r.values.reportDate)],
        [t('fields.period'), `${date(r.values.periodFrom)} – ${date(r.values.periodTo)}`],
        [t('fields.auditedUnits'), r.auditedUnits.map((u) => names.unit(u)).join('، ') || '—'],
        [t('fields.distribution'), r.distribution.map((p) => names.person(p)).join('، ') || '—'],
        [t('fields.summary'), r.values.summary],
        [t('fields.cancelReason'), r.cancelReason],
      ]}
    />
  );
}

/** Excel import: template → validate (row / column report, downloadable) → commit all or nothing. */
function ImportDialog({ reportId, onClose }: { reportId: string; onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const { k } = useAuditT();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<AuditImportReport>();
  const validate = useToastMutation(auditApi.importFindings);
  const commit = useDialogMutation(auditApi.importFindings, () => undefined);
  const toast = useToast();
  return (
    <SimpleDialog
      title={t('import.title')}
      description={t('import.hint')}
      onClose={onClose}
      busy={commit.busy}
      submitLabel={t('import.commit')}
      disabled={!file || !result || result.issues.length > 0}
      testId="audit-import-commit"
      wide
      onSubmit={() =>
        file &&
        commit.run({ reportId, file, mode: 'commit' }, (r) => {
          setResult(r);
          if (r.committed) {
            toast({ tone: 'success', title: t('import.done', { count: r.findings }) });
            onClose();
          }
        })
      }
    >
      <a
        href={auditTemplateUrl}
        className="text-sm text-primary hover:underline"
        data-testid="audit-import-template"
      >
        {t('import.template')}
      </a>
      <FileDrop
        label={t('import.file')}
        accept=".xlsx,.csv"
        file={file}
        onFile={(f) => {
          setFile(f);
          setResult(undefined);
        }}
      />
      <div className="flex gap-2">
        <Button
          variant="outline"
          disabled={!file}
          loading={validate.busy}
          onClick={() => file && validate.run({ reportId, file, mode: 'validate' }, setResult)}
          data-testid="audit-import-validate"
        >
          {t('import.validate')}
        </Button>
        {result && result.issues.length > 0 && file && (
          <Button variant="ghost" onClick={() => void auditApi.importReport({ reportId, file })}>
            {t('import.report')}
          </Button>
        )}
      </div>
      {result && (
        <div data-testid="audit-import-result" className="flex flex-col gap-2 text-sm">
          <StatusBanner
            tone={result.issues.length > 0 ? 'warning' : 'success'}
            title={t('import.summary', {
              rows: result.totalRows,
              findings: result.findings,
              recommendations: result.recommendations,
              issues: result.issues.length,
            })}
          />
          {result.issues.length > 0 && (
            <ul className="max-h-48 list-disc overflow-y-auto ps-5">
              {result.issues.map((i, n) => (
                <li key={n}>
                  {t('import.row', { row: i.row })} · {i.column ?? '—'} · {k(`importIssue.${i.code}`)}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </SimpleDialog>
  );
}

type Row = ExtractedFinding & { keep: boolean; dueOn: string; responsibleUnitId: string | null };

/**
 * AI report-to-findings extraction: upload the report (PDF / Word / scan) or paste text → draft findings. Nothing is saved until the
 * Audit Office keeps rows, sets a due date per recommendation and confirms.
 */
function ExtractDialog({ report, onClose }: { report: AuditReport; onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  const names = useNames();
  const [text, setText] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [rows, setRows] = useState<Row[]>();
  const fromText = useToastMutation(auditApi.extract);
  const fromFile = useToastMutation(auditApi.extractDocument);
  const confirm = useDialogMutation(auditApi.createFindings, onClose);
  const take = (r: { findings: ExtractedFinding[] }) =>
    setRows(
      r.findings.map((f) => ({
        ...f,
        keep: true,
        dueOn: '',
        responsibleUnitId: f.responsibleUnitId ?? null,
      })),
    );
  const kept = (rows ?? []).filter((r) => r.keep);
  const ready =
    kept.length > 0 && kept.every((r) => r.responsibleUnitId && (r.recommendations.length === 0 || r.dueOn));
  const update = (i: number, patch: Partial<Row>) =>
    setRows((list) => list?.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  return (
    <SimpleDialog
      title={t('ai.extract')}
      description={t('ai.extractHint')}
      onClose={onClose}
      busy={confirm.busy}
      submitLabel={t('ai.confirmSelected', { count: kept.length })}
      disabled={!ready}
      testId="audit-ai-confirm"
      wide
      onSubmit={() =>
        confirm.run({
          reportId: report.id,
          findings: kept.map((r) => ({
            finding: {
              titleAr: r.titleAr ?? null,
              titleEn: r.titleEn ?? null,
              typeCode: r.typeCode,
              condition: r.condition ?? null,
              criteria: r.criteria ?? null,
              criteriaDocumentId: null,
              cause: r.cause ?? null,
              effect: r.effect ?? null,
              findingDate: report.values.reportDate ?? isoToday(),
              responsibleUnitId: r.responsibleUnitId ?? '',
              priority: r.priority,
              repeat: false,
              repeatOfFindingId: null,
              expectedClosingOn: null,
              auditorEmployeeId: null,
            },
            recommendations: r.recommendations.map((x) => ({
              actionAr: x.actionAr ?? null,
              actionEn: x.actionEn ?? null,
              managementResponseCode: null,
              managementComment: null,
              implementationPlan: null,
              contactEmployeeId: null,
              dueOn: r.dueOn,
              notes: null,
            })),
          })),
        })
      }
    >
      {!rows && (
        <>
          <FileDrop
            label={t('ai.reportFile')}
            accept=".pdf,.docx,.png,.jpg,.jpeg,.txt"
            file={file}
            onFile={setFile}
          />
          <FormField label={t('ai.orPaste')}>
            <Textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              rows={6}
              data-testid="audit-ai-text"
            />
          </FormField>
          <Button
            variant="outline"
            loading={fromText.busy || fromFile.busy}
            disabled={!file && !text.trim()}
            onClick={() =>
              file
                ? fromFile.run({ reportId: report.id, file }, take)
                : fromText.run({ reportId: report.id, text }, take)
            }
            data-testid="audit-ai-run"
          >
            <Sparkles aria-hidden />
            {t('ai.extractRun')}
          </Button>
        </>
      )}
      {rows && (
        <div className="flex flex-col gap-3" data-testid="audit-ai-review">
          <StatusBanner
            tone="info"
            title={t('ai.reviewTitle', { count: rows.length })}
            description={t('ai.reviewHint')}
          />
          {rows.map((r, i) => (
            <fieldset
              key={i}
              className="flex flex-col gap-2 rounded-md border p-3"
              data-testid="audit-ai-row"
            >
              <div className="flex items-center justify-between gap-2">
                <Checkbox
                  label={names.text(r.titleAr, r.titleEn) || t('ai.untitled')}
                  checked={r.keep}
                  onChange={(e) => update(i, { keep: e.target.checked })}
                />
                <AiBadge />
              </div>
              <p className="text-sm">{r.condition}</p>
              {r.criteria && (
                <p className="text-xs text-muted-foreground">
                  {t('fields.criteria')}: {r.criteria}
                </p>
              )}
              <div className="grid gap-2 md:grid-cols-3">
                <FormField label={t('fields.responsibleUnit')} required>
                  <UnitSelect
                    value={r.responsibleUnitId ?? undefined}
                    onChange={(u) => update(i, { responsibleUnitId: u ?? null })}
                  />
                </FormField>
                <FormField label={t('fields.priority')}>
                  <Select
                    value={r.priority}
                    onValueChange={(priority) => update(i, { priority })}
                    options={labels.lookupOptions(lookupTypes.priority)}
                  />
                </FormField>
                <FormField label={t('fields.dueOn')} required hint={t('recommendation.dueHint')}>
                  <DatePicker value={r.dueOn || undefined} onChange={(d) => update(i, { dueOn: d ?? '' })} />
                </FormField>
              </div>
              {r.recommendations.length > 0 && (
                <ul className="list-disc ps-5 text-sm text-muted-foreground">
                  {r.recommendations.map((x, j) => (
                    <li key={j}>{names.text(x.actionAr, x.actionEn)}</li>
                  ))}
                </ul>
              )}
            </fieldset>
          ))}
        </div>
      )}
    </SimpleDialog>
  );
}
