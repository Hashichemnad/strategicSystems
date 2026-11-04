import { useNavigate } from '@tanstack/react-router';
import { useState, type ReactElement, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { encodeFieldMessage, errorMessage, translateFieldMessage } from '@/api/problem';
import { AiFieldReview, aiPendingClass, SimilarRecords, type AiSuggestion } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  FormField,
  Input,
  Select,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
  Wizard,
} from '@/ui';
import {
  bandOf,
  liveClassification,
  liveScore,
  requestApi,
  requestResource,
  useCreateRequest,
  useRequestMutation,
  yearTotal,
  type PortfolioOptions,
  type ProgramRequest,
  type RequestValues,
} from './api';

/** Every form value is text while editing (AI proposals are text too); {@link toValues} converts for the server. */
export type FormState = Record<string, string>;

type Kind = 'text' | 'long' | 'number' | 'date' | 'year' | 'yesno' | 'choice';

interface FieldSpec {
  name: string;
  kind: Kind;
  /** Choice fields: options from the configuration (lookups) or fixed codes. */
  choices?: (o: PortfolioOptions) => { value: string; label: string }[];
  dir?: 'rtl' | 'ltr';
}

const textFields = [
  'nameAr',
  'nameEn',
  'description',
  'justifications',
  'strategicObjectiveCode',
  'suggestionSourceCode',
  'suggestionSourceOther',
  'programTypeCode',
  'programNature',
  'previousProgramName',
  'scopeOfWork',
  'mainOptionCode',
  'mainOptionOther',
  'mainAdvantages',
  'mainDisadvantages',
  'altOptionCode',
  'altOptionOther',
  'altAdvantages',
  'altDisadvantages',
  'partnerName',
  'practiceOperation',
  'practiceService',
  'practiceProject',
  'practiceInitiative',
  'costPath',
  'expectedStartDate',
  'expectedEndDate',
  'operatingCostsFrom',
] as const;
const numberFields = [
  'estimatedBudget',
  'year1Cost',
  'year2Cost',
  'year3Cost',
  'year4Cost',
  'sharedCostAmount',
  'annualOperationCost',
  'annualLicenceCost',
  'annualMaintenanceCost',
  'annualOtherCost',
  'returnValue',
  'reductionValue',
] as const;
const yearFields = ['returnYear', 'reductionYear'] as const;
export const screeningFields = [
  'screenCoreWork',
  'screenOneTime',
  'screenTimeBound',
  'screenUniqueOutput',
  'screenMinCost',
  'screenOperationalCosts',
] as const;
const boolFields = [
  'requiresBudget',
  ...screeningFields,
  'othersBearCosts',
  'othersCoverImplementation',
  'returnExpected',
  'reductionExpected',
  'costProposalsRequested',
] as const;
export const priorityPrefix = 'priority:';

/** Server values → form text. */
export function toForm(values: RequestValues | undefined): FormState {
  const form: FormState = {};
  if (!values) return form;
  const v = values as Record<string, unknown>;
  for (const name of [...textFields, ...numberFields, ...yearFields]) {
    const value = v[name];
    form[name] = typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  }
  for (const name of boolFields) {
    const value = v[name];
    form[name] = value === true ? 'yes' : value === false ? 'no' : '';
  }
  if (values.partnerShare !== null && values.partnerShare !== undefined) {
    form.partnerShare = String(Math.round(values.partnerShare * 10000) / 100);
  }
  for (const [code, score] of Object.entries(values.priorityScores ?? {}))
    form[priorityPrefix + code] = String(score);
  return form;
}

const blank = (text: string | undefined) => (text === undefined || text.trim() === '' ? null : text.trim());
const number = (text: string | undefined) => {
  const value = blank(text);
  return value === null ? null : Number(value.replace(/,/gu, ''));
};

/** Form text → server values (the server validates everything again). */
export function toValues(form: FormState, unitId: string | null | undefined): RequestValues {
  const values: RequestValues & Record<string, unknown> = {};
  for (const name of textFields) values[name] = blank(form[name]);
  for (const name of [...numberFields, ...yearFields]) values[name] = number(form[name]);
  for (const name of boolFields)
    values[name] = form[name] === 'yes' ? true : form[name] === 'no' ? false : null;
  const share = number(form.partnerShare);
  values.partnerShare = share === null ? null : Math.round(share * 100) / 10000;
  const scores: Record<string, number> = {};
  for (const [key, value] of Object.entries(form)) {
    if (key.startsWith(priorityPrefix) && blank(value) !== null)
      scores[key.slice(priorityPrefix.length)] = Number(value);
  }
  values.priorityScores = scores;
  values.unitId = unitId ?? null;
  return values;
}

export interface RequestWizardProps {
  /** The request to edit; omit to create. */
  request?: ProgramRequest | undefined;
  options: PortfolioOptions;
  /** Starting values (e.g. from documents) and the AI proposals to review. */
  initial?: FormState;
  initialDrafts?: Record<string, AiSuggestion>;
  /** Fields of the form the documents did not fill ("start from a document"). */
  missing?: string[];
  /** Documents the request was started from: attached and assessed after the request is created. */
  documents?: File[];
}

/** The Program Request wizard (SPEC §1.4 A, §1.8): six steps, save as draft at every step, AI-filled values reviewed one by one. */
export function RequestWizard({
  request,
  options,
  initial,
  initialDrafts,
  missing,
  documents,
}: RequestWizardProps) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: ta } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const { t: tv } = useTranslation('validation');
  const toast = useToast();
  const navigate = useNavigate();
  const create = useCreateRequest();
  const update = useRequestMutation(requestApi.update);
  const assessEnabled = useAiFeature('portfolio-assessment');
  const [form, setForm] = useState<FormState>(() => ({ ...toForm(request?.values), ...initial }));
  const [drafts, setDrafts] = useState<Record<string, AiSuggestion>>(initialDrafts ?? {});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [busyText, setBusyText] = useState<string>();
  const lang = i18n.language;
  const pending = Object.entries(drafts)
    .filter(([, d]) => !d.accepted)
    .map(([name]) => name);
  const accept = (name: string) =>
    setDrafts((current) =>
      current[name] ? { ...current, [name]: { ...current[name], accepted: true } } : current,
    );
  const set = (name: string, value: string) => {
    setForm((current) => ({ ...current, [name]: value }));
    accept(name); // editing a proposed value counts as reviewing it
    setErrors((current) =>
      name in current ? Object.fromEntries(Object.entries(current).filter(([key]) => key !== name)) : current,
    );
  };
  const money = (value: number) => `${formatNumber(value, lang)} ${options.currencyCode}`;
  const yesNo = [
    { value: 'yes', label: t('values.yes') },
    { value: 'no', label: t('values.no') },
  ];
  const lookup = (items: PortfolioOptions['objectives']) =>
    items.map((o) => ({ value: o.code, label: pickText(lang, o.labelAr, o.labelEn) }));
  const values = toValues(form, request?.unit?.id);
  const total = yearTotal(values);
  const budget = values.estimatedBudget ?? null;
  const score = liveScore(values.priorityScores, options.criteria);
  const band = bandOf(score, options.bands);
  const bandRecord = options.bands.find((b) => b.code === band);
  const classification = liveClassification(values, options.thresholdAmount);

  const control = (spec: FieldSpec, label?: string): ReactNode => {
    const name = spec.name;
    const value = form[name] ?? '';
    const invalid = Boolean(errors[name]);
    const highlight = drafts[name] && !drafts[name].accepted ? aiPendingClass : undefined;
    let input: ReactNode;
    switch (spec.kind) {
      case 'long':
        input = (
          <Textarea
            dir={spec.dir ?? 'auto'}
            rows={4}
            value={value}
            aria-invalid={invalid}
            className={cn(highlight)}
            onChange={(e) => set(name, e.target.value)}
          />
        );
        break;
      case 'date':
        input = (
          <DatePicker
            value={value || undefined}
            aria-invalid={invalid}
            onChange={(next) => set(name, next ?? '')}
          />
        );
        break;
      case 'yesno':
      case 'choice':
        input = (
          <Select
            value={value || undefined}
            aria-invalid={invalid}
            className={cn(highlight)}
            placeholder={t('values.choose')}
            onValueChange={(next) => set(name, next)}
            options={spec.kind === 'yesno' ? yesNo : (spec.choices?.(options) ?? [])}
          />
        );
        break;
      default:
        input = (
          <Input
            dir={spec.kind === 'text' ? (spec.dir ?? 'auto') : 'ltr'}
            inputMode={spec.kind === 'number' ? 'decimal' : spec.kind === 'year' ? 'numeric' : undefined}
            autoComplete="off"
            value={value}
            aria-invalid={invalid}
            className={cn(highlight)}
            onChange={(e) => set(name, e.target.value)}
          />
        );
    }
    return (
      <div
        className={cn(
          'flex flex-col gap-1 rounded-md',
          spec.kind === 'date' && highlight ? `${highlight} p-1` : undefined,
        )}
        data-field={name}
      >
        <FormField
          label={label ?? t(`fields.${name}` as 'fields.nameAr')}
          error={translateFieldMessage(tc, errors[name])}
        >
          {input as ReactElement<{ id?: string }>}
        </FormField>
        {drafts[name] && <AiFieldReview suggestion={drafts[name]} onAccept={() => accept(name)} />}
      </div>
    );
  };
  const f = (name: string, kind: Kind = 'text', extra?: Partial<FieldSpec>) =>
    control({ name, kind, ...extra });
  const section = (key: string) =>
    errors[key] ? <p className="text-sm text-danger">{translateFieldMessage(tc, errors[key])}</p> : null;

  async function save(): Promise<ProgramRequest | undefined> {
    if (pending.length > 0) return undefined;
    setBusy(true);
    try {
      const body = toValues(form, request?.unit?.id);
      if (request) {
        const saved = await update.mutateAsync({ id: request.id, values: body, version: request.version });
        toast({ title: t('toast.saved') });
        setErrors({});
        return saved;
      }
      const created = await create.mutateAsync({
        values: body,
        source: documents?.length ? 'document' : 'manual',
      });
      toast({ title: t('toast.created', { number: created.number }) });
      if (documents?.length) {
        for (const file of documents) await requestApi.upload({ id: created.id, kind: 'document', file });
        if (options.assessmentOnCreate && assessEnabled) {
          setBusyText(t('start.assessing'));
          try {
            const assessment = await requestApi.assess({ id: created.id });
            toast({ title: t('toast.assessed', { version: assessment.versionNo }) });
          } catch (failure) {
            toast({ tone: 'error', title: errorMessage(tc, failure) }); // AI never blocks the request
          }
        }
      }
      return created;
    } catch (failure) {
      if (failure instanceof ApiError && failure.problem.errors?.length) {
        setErrors(Object.fromEntries(failure.problem.errors.map((e) => [e.field, encodeFieldMessage(e)])));
      }
      toast({ tone: 'error', title: errorMessage(tc, failure) });
      return undefined;
    } finally {
      setBusy(false);
      setBusyText(undefined);
    }
  }

  async function finish(open: boolean) {
    const saved = await save();
    if (!saved) return;
    if (open || !request) {
      await navigate({
        to: '/portfolio/requests/$requestId',
        params: { requestId: saved.id },
        search: { tab: documents?.length && !request ? 'assessment' : 'overview' },
      });
    }
  }

  const steps = [
    {
      key: 'basics',
      title: t('steps.basics'),
      validate: () => {
        if (blank(form.nameAr) === null && blank(form.nameEn) === null) {
          setErrors((e) => ({ ...e, nameEn: 'validation.atLeastOneLanguage' }));
          return false;
        }
        return true;
      },
      content: (
        <div className="grid gap-4 md:grid-cols-2">
          {f('nameAr', 'text', { dir: 'rtl' })}
          {f('nameEn', 'text', { dir: 'ltr' })}
          {!request && (
            <div className="md:col-span-2">
              <SimilarRecords
                resourceType={requestResource}
                text={`${form.nameEn ?? ''} ${form.nameAr ?? ''}`.trim()}
              />
            </div>
          )}
          <div className="md:col-span-2">{f('description', 'long')}</div>
          <div className="md:col-span-2">{f('justifications', 'long')}</div>
          {f('strategicObjectiveCode', 'choice', { choices: (o) => lookup(o.objectives) })}
          {f('programTypeCode', 'choice', { choices: (o) => lookup(o.programTypes) })}
          {f('suggestionSourceCode', 'choice', { choices: (o) => lookup(o.sources) })}
          {form.suggestionSourceCode === 'other' ? f('suggestionSourceOther') : <div />}
          {f('programNature', 'choice', {
            choices: () => [
              { value: 'new', label: t('values.new') },
              { value: 'continuous', label: t('values.continuous') },
            ],
          })}
          {form.programNature === 'continuous' ? f('previousProgramName') : <div />}
          <div className="md:col-span-2">{f('scopeOfWork', 'long')}</div>
          {f('expectedStartDate', 'date')}
          {f('expectedEndDate', 'date')}
          <p className="text-xs text-muted-foreground md:col-span-2">
            {t('hints.dates', { years: options.startYearsAhead })}
          </p>
          {options.objectives.length === 0 && (
            <StatusBanner className="md:col-span-2" tone="warning" title={t('hints.noObjectives')} />
          )}
        </div>
      ),
    },
    {
      key: 'budget',
      title: t('steps.budget'),
      content: (
        <div className="grid gap-4 md:grid-cols-2">
          {f('requiresBudget', 'yesno')}
          {form.requiresBudget === 'yes' ? f('estimatedBudget', 'number') : <div />}
          {form.requiresBudget === 'yes' && (
            <>
              {f('year1Cost', 'number')}
              {f('year2Cost', 'number')}
              {f('year3Cost', 'number')}
              {f('year4Cost', 'number')}
              <div className="md:col-span-2" data-testid="year-total">
                <StatusBanner
                  tone={total !== null && budget !== null && total === budget ? 'success' : 'warning'}
                  title={t('live.total', { total: money(total ?? 0), budget: money(budget ?? 0) })}
                  description={
                    total !== null && budget !== null && total === budget
                      ? t('live.totalOk')
                      : t('live.totalMismatch')
                  }
                />
                {section('yearCosts')}
              </div>
              {f('costPath', 'choice', {
                choices: () => [
                  { value: 'cost-estimation', label: t('values.cost-estimation') },
                  { value: 'project-scope', label: t('values.project-scope') },
                ],
              })}
              {f('costProposalsRequested', 'yesno')}
              <p className="text-xs text-muted-foreground md:col-span-2">{t('hints.filesAfterSave')}</p>
              {section('costEstimation')}
              {section('quotations')}
            </>
          )}
        </div>
      ),
    },
    {
      key: 'screening',
      title: t('steps.screening'),
      content: (
        <div className="flex flex-col gap-6">
          <fieldset className="grid gap-4 md:grid-cols-2">
            <legend className="mb-2 font-medium">{t('fields.screening')}</legend>
            {screeningFields.map((name) =>
              control(
                { name, kind: 'yesno' },
                name === 'screenMinCost'
                  ? t('fields.screenMinCost', { amount: money(options.thresholdAmount) })
                  : undefined,
              ),
            )}
            <div className="md:col-span-2">{section('screening')}</div>
          </fieldset>
          <fieldset className="grid gap-4 md:grid-cols-2">
            <legend className="mb-2 font-medium">{t('fields.priorityScores')}</legend>
            {options.criteria.map((c) =>
              control(
                {
                  name: priorityPrefix + c.code,
                  kind: 'choice',
                  choices: () =>
                    ['1', '2', '3', '4', '5'].map((v) => ({
                      value: v,
                      label: formatNumber(Number(v), lang),
                    })),
                },
                pickText(lang, c.labelAr, c.labelEn),
              ),
            )}
            <div className="md:col-span-2">{section('priorityScores')}</div>
          </fieldset>
          <div
            className="grid gap-3 rounded-lg border bg-muted/40 p-4 md:grid-cols-2"
            data-testid="live-evaluation"
          >
            <div>
              <p className="font-medium">
                {t('live.classification', { value: t(`classification.${classification}`) })}
              </p>
              <p className="text-xs text-muted-foreground">
                {t('live.classificationHint', { amount: money(options.thresholdAmount) })}
              </p>
            </div>
            <div>
              {score === null ? (
                <p className="text-sm text-muted-foreground">{t('live.scorePending')}</p>
              ) : (
                <StatusChip tone={band === 'high' ? 'danger' : band === 'medium' ? 'warning' : 'neutral'}>
                  {t('live.score', {
                    score: formatNumber(Math.round(score * 10000) / 100, lang),
                    band: bandRecord ? pickText(lang, bandRecord.labelAr, bandRecord.labelEn) : '',
                  })}
                </StatusChip>
              )}
            </div>
          </div>
        </div>
      ),
    },
    {
      key: 'delivery',
      title: t('steps.delivery'),
      content: (
        <div className="grid gap-4 md:grid-cols-2">
          {f('mainOptionCode', 'choice', { choices: (o) => lookup(o.deliveryOptions) })}
          {form.mainOptionCode === 'other' ? f('mainOptionOther') : <div />}
          {f('mainAdvantages', 'long')}
          {f('mainDisadvantages', 'long')}
          {f('altOptionCode', 'choice', { choices: (o) => lookup(o.deliveryOptions) })}
          {form.altOptionCode === 'other' ? f('altOptionOther') : <div />}
          {f('altAdvantages', 'long')}
          {f('altDisadvantages', 'long')}
          {f('othersBearCosts', 'yesno')}
          {f('othersCoverImplementation', 'yesno')}
          {f('sharedCostAmount', 'number')}
          {f('partnerName')}
          {f('partnerShare', 'number')}
        </div>
      ),
    },
    {
      key: 'operations',
      title: t('steps.operations'),
      content: (
        <div className="grid gap-4 md:grid-cols-2">
          {f('annualOperationCost', 'number')}
          {f('annualLicenceCost', 'number')}
          {f('annualMaintenanceCost', 'number')}
          {f('annualOtherCost', 'number')}
          {f('operatingCostsFrom', 'date')}
          <div />
          {f('returnExpected', 'yesno')}
          <div />
          {form.returnExpected === 'yes' && (
            <>
              {f('returnValue', 'number')}
              {f('returnYear', 'year')}
            </>
          )}
          {f('reductionExpected', 'yesno')}
          <div />
          {form.reductionExpected === 'yes' && (
            <>
              {f('reductionValue', 'number')}
              {f('reductionYear', 'year')}
            </>
          )}
        </div>
      ),
    },
    {
      key: 'practices',
      title: t('steps.practices'),
      content: (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">{t('fields.practices')}</p>
          <div className="grid gap-4 md:grid-cols-2">
            {f('practiceOperation')}
            {f('practiceService')}
            {f('practiceProject')}
            {f('practiceInitiative')}
          </div>
          <StatusBanner tone="info" title={t('hints.review')} />
        </div>
      ),
    },
  ];

  const errorList = Object.entries(errors);
  return (
    <div className="flex flex-col gap-4">
      {missing && missing.length > 0 && (
        <StatusBanner
          tone="warning"
          title={t('start.missingTitle')}
          description={`${t('start.missingHint')} ${missing.map((m) => t(`fields.${m}` as 'fields.nameAr', { amount: '' })).join('، ')}`}
        />
      )}
      {pending.length > 0 && (
        <StatusBanner
          tone="info"
          title={ta('reviewPending', { count: pending.length })}
          actions={
            <Button size="sm" variant="outline" onClick={() => pending.forEach(accept)}>
              {ta('acceptAll')}
            </Button>
          }
        />
      )}
      {errorList.length > 0 && (
        <StatusBanner
          tone="danger"
          title={tv('title')}
          description={errorList
            .map(
              ([field, message]) =>
                `${field.startsWith(priorityPrefix) || field.startsWith('priorityScores.') ? t('fields.priorityScores') : t(`fields.${field}` as 'fields.nameAr', { amount: '' })}: ${translateFieldMessage(tc, message) ?? ''}`,
            )
            .join(' · ')}
        />
      )}
      {busyText && <StatusBanner tone="info" title={busyText} />}
      <Wizard
        steps={steps}
        busy={busy}
        finishDisabled={pending.length > 0}
        finishLabel={request ? t('actions.saveAndOpen') : t('actions.createDraft')}
        onSaveDraft={async () => {
          const saved = await save();
          if (saved && !request) {
            await navigate({ to: '/portfolio/requests/$requestId/edit', params: { requestId: saved.id } });
          }
        }}
        onFinish={() => finish(true)}
      />
    </div>
  );
}
