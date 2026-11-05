import { Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { UnitSelect } from '@/features/access/pickers';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { useCan } from '@/features/access/session';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import {
  Button,
  Checkbox,
  DatePicker,
  FileDrop,
  FormField,
  Input,
  Select,
  StatusBanner,
  Textarea,
} from '@/ui';
import {
  auditApi,
  lookupTypes,
  useControlOptions,
  useDocumentOptions,
  useRiskOptions,
  type AuditFindingValues,
  type AuditRecommendation,
  type AuditRecommendationValues,
  type AuditReportValues,
  type EvidenceReview,
} from './api';
import {
  SimpleDialog,
  isoToday,
  useAuditLabels,
  useAuditT,
  useDialogMutation,
  useNames,
  useToastMutation,
} from './components';

const n = (s: string | null | undefined) => (s && s.trim() ? s.trim() : null);

export function emptyReport(): AuditReportValues {
  return {
    titleAr: '',
    titleEn: '',
    typeCode: 'internal-audit',
    auditorBody: '',
    leadAuditorEmployeeId: null,
    auditedUnitIds: [],
    periodFrom: null,
    periodTo: null,
    reportDate: isoToday(),
    distributionEmployeeIds: [],
    distributionNote: '',
    summary: '',
  };
}

/** New or edited audit report (type, auditor / body, audited units, period, report date, distribution). */
export function ReportDialog({
  initial,
  reportId,
  version,
  onClose,
  onSaved,
}: {
  initial?: AuditReportValues;
  reportId?: string;
  version?: number;
  onClose: () => void;
  onSaved?: (id: string) => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  const [v, setV] = useState<AuditReportValues>(initial ?? emptyReport());
  const [unit, setUnit] = useState<string>();
  const [person, setPerson] = useState<string>();
  const create = useDialogMutation(auditApi.createReport, onClose);
  const update = useDialogMutation(auditApi.updateReport, onClose);
  const set = (patch: Partial<AuditReportValues>) => setV((x) => ({ ...x, ...patch }));
  const values: AuditReportValues = {
    ...v,
    titleAr: n(v.titleAr),
    titleEn: n(v.titleEn),
    auditorBody: n(v.auditorBody),
    distributionNote: n(v.distributionNote),
    summary: n(v.summary),
  };
  const units = v.auditedUnitIds ?? [];
  const people = v.distributionEmployeeIds ?? [];
  return (
    <SimpleDialog
      title={reportId ? t('report.edit') : t('reports.new')}
      description={t('reports.newHint')}
      onClose={onClose}
      busy={create.busy || update.busy}
      submitLabel={reportId ? t('common.save') : t('common.create')}
      disabled={!(v.titleAr || v.titleEn)}
      testId="audit-report-save"
      wide
      onSubmit={() =>
        reportId
          ? update.run({ id: reportId, values, version: version ?? 0 }, (r) => onSaved?.(r.id))
          : create.run(values, (r) => onSaved?.(r.id))
      }
    >
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.titleAr')} required>
          <Input
            dir="rtl"
            value={v.titleAr ?? ''}
            onChange={(e) => set({ titleAr: e.target.value })}
            data-testid="audit-report-title-ar"
          />
        </FormField>
        <FormField label={t('fields.titleEn')}>
          <Input
            dir="ltr"
            value={v.titleEn ?? ''}
            onChange={(e) => set({ titleEn: e.target.value })}
            data-testid="audit-report-title-en"
          />
        </FormField>
        <FormField label={t('fields.reportType')} required>
          <Select
            value={v.typeCode}
            onValueChange={(typeCode) => set({ typeCode })}
            options={labels.lookupOptions(lookupTypes.reportType)}
          />
        </FormField>
        <FormField label={t('fields.auditorBody')}>
          <Input
            value={v.auditorBody ?? ''}
            onChange={(e) => set({ auditorBody: e.target.value })}
            data-testid="audit-report-body"
          />
        </FormField>
        <FormField label={t('fields.leadAuditor')}>
          <EmployeePicker
            value={v.leadAuditorEmployeeId ?? undefined}
            onChange={(e) => set({ leadAuditorEmployeeId: e ?? null })}
          />
        </FormField>
        <FormField label={t('fields.reportDate')}>
          <DatePicker value={v.reportDate ?? undefined} onChange={(d) => set({ reportDate: d ?? null })} />
        </FormField>
        <FormField label={t('fields.periodFrom')}>
          <DatePicker value={v.periodFrom ?? undefined} onChange={(d) => set({ periodFrom: d ?? null })} />
        </FormField>
        <FormField label={t('fields.periodTo')}>
          <DatePicker value={v.periodTo ?? undefined} onChange={(d) => set({ periodTo: d ?? null })} />
        </FormField>
      </div>
      <FormField label={t('fields.auditedUnits')} hint={t('report.unitsHint', { count: units.length })}>
        <div className="flex gap-2">
          <div className="flex-1">
            <UnitSelect value={unit} onChange={setUnit} />
          </div>
          <Button
            variant="outline"
            disabled={!unit || units.includes(unit)}
            onClick={() => unit && set({ auditedUnitIds: [...units, unit] })}
          >
            {t('common.add')}
          </Button>
          {units.length > 0 && (
            <Button variant="ghost" onClick={() => set({ auditedUnitIds: [] })}>
              {t('common.clear')}
            </Button>
          )}
        </div>
      </FormField>
      <FormField
        label={t('fields.distribution')}
        hint={t('report.distributionHint', { count: people.length })}
      >
        <div className="flex gap-2">
          <div className="flex-1">
            <EmployeePicker value={person} onChange={setPerson} />
          </div>
          <Button
            variant="outline"
            disabled={!person || people.includes(person)}
            onClick={() => person && set({ distributionEmployeeIds: [...people, person] })}
          >
            {t('common.add')}
          </Button>
          {people.length > 0 && (
            <Button variant="ghost" onClick={() => set({ distributionEmployeeIds: [] })}>
              {t('common.clear')}
            </Button>
          )}
        </div>
      </FormField>
      <FormField label={t('fields.summary')}>
        <Textarea value={v.summary ?? ''} onChange={(e) => set({ summary: e.target.value })} />
      </FormField>
    </SimpleDialog>
  );
}

export function emptyFinding(): AuditFindingValues {
  return {
    titleAr: '',
    titleEn: '',
    typeCode: 'finding',
    condition: '',
    criteria: '',
    criteriaDocumentId: null,
    cause: '',
    effect: '',
    findingDate: isoToday(),
    responsibleUnitId: '',
    priority: 'medium',
    repeat: false,
    repeatOfFindingId: null,
    expectedClosingOn: null,
    auditorEmployeeId: null,
  };
}

export function emptyRecommendation(): AuditRecommendationValues {
  return {
    actionAr: '',
    actionEn: '',
    managementResponseCode: null,
    managementComment: '',
    implementationPlan: '',
    contactEmployeeId: null,
    dueOn: '',
    notes: '',
  };
}

export function cleanRecommendation(v: AuditRecommendationValues): AuditRecommendationValues {
  return {
    ...v,
    actionAr: n(v.actionAr),
    actionEn: n(v.actionEn),
    managementComment: n(v.managementComment),
    implementationPlan: n(v.implementationPlan),
    notes: n(v.notes),
  };
}

export function cleanFinding(v: AuditFindingValues): AuditFindingValues {
  return {
    ...v,
    titleAr: n(v.titleAr),
    titleEn: n(v.titleEn),
    condition: n(v.condition),
    criteria: n(v.criteria),
    cause: n(v.cause),
    effect: n(v.effect),
  };
}

/** Criteria document picker (Document Registry; only documents the auditor may see). */
function DocumentPicker({
  value,
  onChange,
}: {
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const names = useNames();
  const [search, setSearch] = useState('');
  const options = useDocumentOptions(search);
  return (
    <div className="flex flex-col gap-2">
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder={t('finding.searchDocument')}
        data-testid="audit-document-search"
      />
      {(options.data ?? []).length > 0 && (
        <Select
          value={value ?? '-'}
          onValueChange={(id) => onChange(id === '-' ? null : id)}
          options={[
            { value: '-', label: t('common.none') },
            ...(options.data ?? []).map((d) => ({
              value: d.id,
              label: `${d.documentNo ?? ''} ${names.text(d.nameAr, d.nameEn)}`.trim(),
            })),
          ]}
        />
      )}
      {value && !(options.data ?? []).some((d) => d.id === value) && (
        <p className="text-xs text-muted-foreground">
          {t('finding.documentLinked')}{' '}
          <button type="button" className="text-primary underline" onClick={() => onChange(null)}>
            {t('common.clear')}
          </button>
        </p>
      )}
    </div>
  );
}

function FindingFields({
  v,
  set,
}: {
  v: AuditFindingValues;
  set: (patch: Partial<AuditFindingValues>) => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  return (
    <>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.titleAr')} required>
          <Input
            dir="rtl"
            value={v.titleAr ?? ''}
            onChange={(e) => set({ titleAr: e.target.value })}
            data-testid="audit-finding-title-ar"
          />
        </FormField>
        <FormField label={t('fields.titleEn')}>
          <Input
            dir="ltr"
            value={v.titleEn ?? ''}
            onChange={(e) => set({ titleEn: e.target.value })}
            data-testid="audit-finding-title-en"
          />
        </FormField>
      </div>
      <FormField label={t('fields.condition')} required hint={t('finding.conditionHint')}>
        <Textarea
          value={v.condition ?? ''}
          onChange={(e) => set({ condition: e.target.value })}
          data-testid="audit-finding-condition"
        />
      </FormField>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.criteria')}>
          <Textarea
            value={v.criteria ?? ''}
            onChange={(e) => set({ criteria: e.target.value })}
            data-testid="audit-finding-criteria"
          />
        </FormField>
        <FormField label={t('fields.criteriaDocument')}>
          <DocumentPicker
            value={v.criteriaDocumentId}
            onChange={(criteriaDocumentId) => set({ criteriaDocumentId })}
          />
        </FormField>
        <FormField label={t('fields.cause')}>
          <Textarea
            value={v.cause ?? ''}
            onChange={(e) => set({ cause: e.target.value })}
            data-testid="audit-finding-cause"
          />
        </FormField>
        <FormField label={t('fields.effect')}>
          <Textarea
            value={v.effect ?? ''}
            onChange={(e) => set({ effect: e.target.value })}
            data-testid="audit-finding-effect"
          />
        </FormField>
        <FormField label={t('fields.responsibleUnit')} required>
          <UnitSelect
            value={v.responsibleUnitId || undefined}
            onChange={(u) => set({ responsibleUnitId: u ?? '' })}
          />
        </FormField>
        <FormField label={t('fields.priority')} required>
          <Select
            value={v.priority}
            onValueChange={(priority) => set({ priority })}
            options={labels.lookupOptions(lookupTypes.priority)}
          />
        </FormField>
        <FormField label={t('fields.findingType')}>
          <Select
            value={v.typeCode}
            onValueChange={(typeCode) => set({ typeCode })}
            options={labels.lookupOptions(lookupTypes.findingType)}
          />
        </FormField>
        <FormField label={t('fields.findingDate')} required>
          <DatePicker value={v.findingDate} onChange={(d) => d && set({ findingDate: d })} />
        </FormField>
        <FormField label={t('fields.expectedClosingOn')}>
          <DatePicker
            value={v.expectedClosingOn ?? undefined}
            onChange={(d) => set({ expectedClosingOn: d ?? null })}
          />
        </FormField>
        <FormField label={t('fields.auditor')}>
          <EmployeePicker
            value={v.auditorEmployeeId ?? undefined}
            onChange={(e) => set({ auditorEmployeeId: e ?? null })}
          />
        </FormField>
      </div>
      <Checkbox
        label={t('fields.repeat')}
        checked={v.repeat}
        onChange={(e) => set({ repeat: e.target.checked })}
      />
    </>
  );
}

function RecommendationFields({
  v,
  set,
  testPrefix = 'audit-rec',
}: {
  v: AuditRecommendationValues;
  set: (patch: Partial<AuditRecommendationValues>) => void;
  testPrefix?: string;
}) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  return (
    <>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.actionAr')} required>
          <Textarea
            dir="rtl"
            value={v.actionAr ?? ''}
            onChange={(e) => set({ actionAr: e.target.value })}
            data-testid={`${testPrefix}-action-ar`}
          />
        </FormField>
        <FormField label={t('fields.actionEn')}>
          <Textarea
            dir="ltr"
            value={v.actionEn ?? ''}
            onChange={(e) => set({ actionEn: e.target.value })}
            data-testid={`${testPrefix}-action-en`}
          />
        </FormField>
        <FormField label={t('fields.dueOn')} required hint={t('recommendation.dueHint')}>
          <DatePicker value={v.dueOn || undefined} onChange={(d) => set({ dueOn: d ?? '' })} />
        </FormField>
        <FormField label={t('fields.contact')}>
          <EmployeePicker
            value={v.contactEmployeeId ?? undefined}
            onChange={(e) => set({ contactEmployeeId: e ?? null })}
          />
        </FormField>
        <FormField label={t('fields.managementResponse')}>
          <Select
            value={v.managementResponseCode ?? '-'}
            onValueChange={(c) => set({ managementResponseCode: c === '-' ? null : c })}
            options={[
              { value: '-', label: t('common.notReceived') },
              ...labels.lookupOptions(lookupTypes.response),
            ]}
          />
        </FormField>
        <FormField label={t('fields.managementComment')}>
          <Input
            value={v.managementComment ?? ''}
            onChange={(e) => set({ managementComment: e.target.value })}
          />
        </FormField>
      </div>
      <FormField label={t('fields.implementationPlan')}>
        <Textarea
          value={v.implementationPlan ?? ''}
          onChange={(e) => set({ implementationPlan: e.target.value })}
          data-testid={`${testPrefix}-plan`}
        />
      </FormField>
    </>
  );
}

/** Add a finding (with its first recommendations) or edit one. Every recommendation needs its own due date — no default. */
export function FindingDialog({
  reportId,
  findingId,
  initial,
  version,
  onClose,
  onSaved,
}: {
  reportId?: string;
  findingId?: string;
  initial?: AuditFindingValues;
  version?: number;
  onClose: () => void;
  onSaved?: (id: string) => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const [v, setV] = useState<AuditFindingValues>(initial ?? emptyFinding());
  const [recs, setRecs] = useState<AuditRecommendationValues[]>(findingId ? [] : [emptyRecommendation()]);
  const create = useDialogMutation(auditApi.createFinding, onClose);
  const update = useDialogMutation(auditApi.updateFinding, onClose);
  const set = (patch: Partial<AuditFindingValues>) => setV((x) => ({ ...x, ...patch }));
  const valid =
    !!(v.titleAr || v.titleEn) &&
    !!v.condition &&
    !!v.responsibleUnitId &&
    recs.every((r) => (r.actionAr || r.actionEn) && r.dueOn);
  return (
    <SimpleDialog
      title={findingId ? t('finding.edit') : t('finding.add')}
      onClose={onClose}
      busy={create.busy || update.busy}
      submitLabel={findingId ? t('common.save') : t('common.create')}
      disabled={!valid}
      testId="audit-finding-save"
      wide
      onSubmit={() =>
        findingId
          ? update.run({ id: findingId, values: cleanFinding(v), version: version ?? 0 }, (f) =>
              onSaved?.(f.id),
            )
          : create.run(
              {
                reportId: reportId ?? '',
                input: { finding: cleanFinding(v), recommendations: recs.map(cleanRecommendation) },
              },
              (f) => onSaved?.(f.id),
            )
      }
    >
      <FindingFields v={v} set={set} />
      {!findingId &&
        recs.map((r, i) => (
          <fieldset key={i} className="flex flex-col gap-3 rounded-md border p-3">
            <legend className="px-1 text-sm font-semibold">
              {t('recommendation.numbered', { n: i + 1 })}
            </legend>
            <RecommendationFields
              v={r}
              testPrefix={`audit-rec-${i}`}
              set={(patch) => setRecs((list) => list.map((x, j) => (j === i ? { ...x, ...patch } : x)))}
            />
            {recs.length > 1 && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setRecs((list) => list.filter((_, j) => j !== i))}
              >
                {t('common.remove')}
              </Button>
            )}
          </fieldset>
        ))}
      {!findingId && (
        <Button
          variant="outline"
          size="sm"
          onClick={() => setRecs((list) => [...list, emptyRecommendation()])}
          data-testid="audit-add-rec-row"
        >
          {t('recommendation.addAnother')}
        </Button>
      )}
    </SimpleDialog>
  );
}

export function RecommendationDialog({
  findingId,
  recommendation,
  onClose,
}: {
  findingId: string;
  recommendation?: AuditRecommendation;
  onClose: () => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const [v, setV] = useState<AuditRecommendationValues>(recommendation?.values ?? emptyRecommendation());
  const create = useDialogMutation(auditApi.createRecommendation, onClose);
  const update = useDialogMutation(auditApi.updateRecommendation, onClose);
  return (
    <SimpleDialog
      title={recommendation ? t('recommendation.edit') : t('recommendation.add')}
      onClose={onClose}
      busy={create.busy || update.busy}
      submitLabel={recommendation ? t('common.save') : t('common.create')}
      disabled={!(v.actionAr || v.actionEn) || !v.dueOn}
      testId="audit-recommendation-save"
      wide
      onSubmit={() =>
        recommendation
          ? update.run({
              id: recommendation.id,
              values: cleanRecommendation(v),
              version: recommendation.version,
            })
          : create.run({ findingId, values: cleanRecommendation(v) })
      }
    >
      <RecommendationFields v={v} set={(patch) => setV((x) => ({ ...x, ...patch }))} />
    </SimpleDialog>
  );
}

/**
 * Record a follow-up (Audit Office): follow-up and achieved dates, %, what was implemented, evidence received, next follow-up date (required
 * below 100 %), Audit Office comment. The AI evidence review drafts the comment; the auditor edits it.
 */
export function FollowUpDialog({
  recommendation,
  onClose,
}: {
  recommendation: AuditRecommendation;
  onClose: () => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const { k, language } = useAuditT();
  const aiFeature = useAiFeature('auditmgmt-evidence');
  const aiAllowed = useCan('auditmgmt.ai.evidence');
  const aiOn = aiFeature && aiAllowed;
  const [followUpOn, setFollowUpOn] = useState(isoToday());
  const [achievedOn, setAchievedOn] = useState<string>();
  const [pct, setPct] = useState(Math.round(recommendation.latestPct * 100));
  const [actions, setActions] = useState('');
  const [evidence, setEvidence] = useState('');
  const [next, setNext] = useState<string>();
  const [comment, setComment] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [review, setReview] = useState<EvidenceReview>();
  const m = useDialogMutation(auditApi.recordFollowUp, onClose);
  const ai = useToastMutation(auditApi.evidenceReview);
  const needsNext = pct < 100;
  return (
    <SimpleDialog
      title={t('followUp.title', { number: recommendation.number })}
      description={t('followUp.hint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('followUp.record')}
      disabled={pct < 0 || pct > 100 || (needsNext && !next)}
      testId="audit-follow-up-save"
      wide
      onSubmit={() =>
        m.run({
          id: recommendation.id,
          values: {
            followUpOn,
            achievedOn: achievedOn ?? (pct >= 100 ? followUpOn : null),
            pct: pct / 100,
            actionsImplemented: n(actions),
            evidenceText: n(evidence),
            nextFollowUpOn: needsNext ? (next ?? null) : null,
            comment: n(comment),
          },
        })
      }
    >
      <div className="grid gap-3 md:grid-cols-3">
        <FormField label={t('fields.followUpOn')} required>
          <DatePicker value={followUpOn} onChange={(d) => d && setFollowUpOn(d)} />
        </FormField>
        <FormField label={t('fields.achievedOn')}>
          <DatePicker value={achievedOn} onChange={setAchievedOn} />
        </FormField>
        <FormField label={t('fields.pct')} required>
          <Input
            type="number"
            min={0}
            max={100}
            value={pct}
            onChange={(e) => setPct(Number(e.target.value))}
            data-testid="audit-follow-up-pct"
          />
        </FormField>
      </div>
      <FormField label={t('fields.actionsImplemented')}>
        <Textarea
          value={actions}
          onChange={(e) => setActions(e.target.value)}
          data-testid="audit-follow-up-actions"
        />
      </FormField>
      <FormField label={t('fields.evidenceText')} hint={t('followUp.evidenceHint')}>
        <Textarea
          value={evidence}
          onChange={(e) => setEvidence(e.target.value)}
          data-testid="audit-follow-up-evidence"
        />
      </FormField>
      {aiOn && (
        <div className="flex flex-col gap-2 rounded-md border border-dashed p-3">
          <FileDrop
            label={t('followUp.evidenceFile')}
            accept=".pdf,.docx,.png,.jpg,.jpeg,.txt"
            file={file}
            onFile={setFile}
          />
          <Button
            variant="outline"
            size="sm"
            loading={ai.busy}
            onClick={() =>
              ai.run(
                {
                  id: recommendation.id,
                  pct: pct / 100,
                  actionsImplemented: actions,
                  evidenceText: evidence,
                  file,
                },
                (r) => {
                  setReview(r);
                  if (!comment) setComment(language === 'ar' ? r.commentAr : r.commentEn);
                },
              )
            }
            data-testid="audit-ai-evidence"
          >
            <Sparkles aria-hidden />
            {t('ai.evidenceReview')}
          </Button>
          {review && (
            <div data-testid="audit-ai-evidence-result" className="flex flex-col gap-1 text-sm">
              <span className="flex items-center gap-2 font-semibold">
                {k(`evidenceSupport.${review.support}`)} <AiBadge />
              </span>
              {review.gaps.length > 0 && (
                <ul className="list-disc ps-5 text-muted-foreground">
                  {review.gaps.map((g) => (
                    <li key={g}>{g}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      )}
      <div className="grid gap-3 md:grid-cols-2">
        <FormField
          label={t('fields.nextFollowUpOn')}
          required={needsNext}
          hint={needsNext ? t('followUp.nextRequired') : undefined}
        >
          <DatePicker value={next} onChange={setNext} disabled={!needsNext} />
        </FormField>
      </div>
      <FormField label={t('fields.officeComment')}>
        <Textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          data-testid="audit-follow-up-comment"
        />
      </FormField>
      <p className="text-xs text-muted-foreground">{t('followUp.evidenceAfter')}</p>
    </SimpleDialog>
  );
}

/** Reason-only dialogs: cancel a finding / recommendation / report, reopen a risk acceptance. */
export function ReasonDialog({
  title,
  description,
  submitLabel,
  testId,
  run,
  onClose,
}: {
  title: string;
  description?: string;
  submitLabel: string;
  testId: string;
  run: (reason: string) => Promise<unknown>;
  onClose: () => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const [reason, setReason] = useState('');
  const m = useDialogMutation(run, onClose);
  return (
    <SimpleDialog
      title={title}
      description={description}
      onClose={onClose}
      busy={m.busy}
      submitLabel={submitLabel}
      disabled={!reason.trim()}
      testId={testId}
      onSubmit={() => m.run(reason.trim())}
    >
      <FormField label={t('fields.reason')} required>
        <Textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          data-testid={`${testId}-reason`}
        />
      </FormField>
    </SimpleDialog>
  );
}

export function ReviseDialog({
  recommendation,
  onClose,
}: {
  recommendation: AuditRecommendation;
  onClose: () => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const [due, setDue] = useState<string>();
  const [reason, setReason] = useState('');
  const m = useDialogMutation(auditApi.revise, onClose);
  return (
    <SimpleDialog
      title={t('recommendation.revise')}
      description={t('recommendation.reviseHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.save')}
      disabled={!due || !reason.trim()}
      testId="audit-revise-save"
      onSubmit={() => due && m.run({ id: recommendation.id, newDueOn: due, reason: reason.trim() })}
    >
      <FormField label={t('fields.newDueOn')} required>
        <DatePicker value={due} onChange={setDue} />
      </FormField>
      <FormField label={t('fields.reason')} required>
        <Textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          data-testid="audit-revise-reason"
        />
      </FormField>
    </SimpleDialog>
  );
}

export function AcceptRiskDialog({
  recommendation,
  onClose,
}: {
  recommendation: AuditRecommendation;
  onClose: () => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const [justification, setJustification] = useState('');
  const [reference, setReference] = useState('');
  const m = useDialogMutation(auditApi.acceptRisk, onClose);
  return (
    <SimpleDialog
      title={t('recommendation.acceptRisk')}
      description={t('recommendation.acceptRiskHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('recommendation.acceptRisk')}
      disabled={!justification.trim()}
      testId="audit-accept-risk-save"
      onSubmit={() =>
        m.run({ id: recommendation.id, justification: justification.trim(), reference: n(reference) })
      }
    >
      <FormField label={t('fields.justification')} required>
        <Textarea
          value={justification}
          onChange={(e) => setJustification(e.target.value)}
          data-testid="audit-accept-risk-justification"
        />
      </FormField>
      <FormField label={t('fields.reference')}>
        <Input value={reference} onChange={(e) => setReference(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

/** Link a risk (and one of its controls) — optionally flag the risk for reassessment and lower the control's effectiveness. */
export function RiskLinkDialog({ findingId, onClose }: { findingId: string; onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const names = useNames();
  const [search, setSearch] = useState('');
  const [riskId, setRiskId] = useState<string>();
  const [controlId, setControlId] = useState<string>();
  const [flag, setFlag] = useState(true);
  const [lower, setLower] = useState(false);
  const risks = useRiskOptions(search, true);
  const controls = useControlOptions(riskId);
  const m = useDialogMutation(auditApi.linkRisk, onClose);
  return (
    <SimpleDialog
      title={t('risks.link')}
      description={t('risks.linkHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('risks.link')}
      disabled={!riskId}
      testId="audit-risk-link-save"
      onSubmit={() =>
        riskId &&
        m.run({
          id: findingId,
          riskId,
          controlId: controlId ?? null,
          flagForReassessment: flag,
          lowerControl: lower && !!controlId,
        })
      }
    >
      <FormField label={t('risks.search')}>
        <Input value={search} onChange={(e) => setSearch(e.target.value)} data-testid="audit-risk-search" />
      </FormField>
      {risks.data && risks.data.length === 0 && <StatusBanner tone="info" title={t('risks.none')} />}
      {risks.data && risks.data.length > 0 && (
        <FormField label={t('risks.risk')} required>
          <Select
            value={riskId ?? ''}
            onValueChange={(id) => {
              setRiskId(id);
              setControlId(undefined);
            }}
            options={risks.data.map((r) => ({
              value: r.id,
              label: `${r.number} — ${names.text(r.titleAr, r.titleEn)}`,
            }))}
          />
        </FormField>
      )}
      {controls.data && controls.data.length > 0 && (
        <FormField label={t('risks.control')}>
          <Select
            value={controlId ?? '-'}
            onValueChange={(id) => setControlId(id === '-' ? undefined : id)}
            options={[
              { value: '-', label: t('common.none') },
              ...controls.data.map((c) => ({ value: c.id, label: names.text(c.nameAr, c.nameEn) || c.id })),
            ]}
          />
        </FormField>
      )}
      <Checkbox label={t('risks.flag')} checked={flag} onChange={(e) => setFlag(e.target.checked)} />
      <Checkbox
        label={t('risks.lower')}
        checked={lower}
        disabled={!controlId}
        onChange={(e) => setLower(e.target.checked)}
      />
    </SimpleDialog>
  );
}
