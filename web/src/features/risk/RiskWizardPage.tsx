import { useNavigate, useParams } from '@tanstack/react-router';
import { Copy, Sparkles, X } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Textarea,
  useToast,
  Wizard,
  type WizardStep,
} from '@/ui';
import {
  IDENTIFIED_RISK_KEY,
  riskApi,
  riskTypes,
  todayIso,
  useRisk,
  useRiskMutation,
  useRiskOptions,
  type DuplicateCheck,
  type RiskOptions,
  type RiskValues,
  type StatementCheck,
  type SuggestedRisk,
} from './api';
import { RatingChip, RiskMatrix, RiskPageLayout, useAxisLabels, useNames, useRiskLabels, useRiskT } from './components';
import { previewRating } from './scoring';

const NONE = '-';

function emptyValues(): RiskValues {
  return {
    titleAr: null,
    titleEn: null,
    cause: null,
    eventText: null,
    consequence: null,
    determinedByCode: null,
    identifiedOn: todayIso(),
    exposureStart: null,
    riskType: 'internal',
    categoryCodes: [],
    emerging: false,
    opportunity: false,
    unitId: '',
    ownerEmployeeId: null,
    coordinatorEmployeeId: null,
    followUpEmployeeId: null,
    objectiveCodes: [],
    processes: null,
    inherentLikelihood: 0,
    inherentImpact: 0,
    inherentImpactArea: null,
    residualLikelihood: null,
    residualImpact: null,
    targetLikelihood: null,
    targetImpact: null,
    targetDate: null,
    responseCode: null,
    needsFollowUp: false,
  };
}

/** An AI-identified risk chosen on the register (a draft: the user reviews every field before saving). */
function takeIdentified(): Partial<RiskValues> | undefined {
  try {
    const raw = sessionStorage.getItem(IDENTIFIED_RISK_KEY);
    if (!raw) return undefined;
    sessionStorage.removeItem(IDENTIFIED_RISK_KEY);
    const r = JSON.parse(raw) as SuggestedRisk;
    return {
      titleAr: r.titleAr,
      titleEn: r.titleEn,
      cause: r.cause,
      eventText: r.eventText,
      consequence: r.consequence,
      categoryCodes: r.categoryCode ? [r.categoryCode] : [],
      inherentLikelihood: r.likelihood ?? 0,
      inherentImpact: r.impact ?? 0,
    };
  } catch {
    return undefined;
  }
}

/** New / edit risk wizard (docs/design/risk.md §12): statement → classification & ownership → assessment → review. */
export function RiskWizardPage() {
  const params = useParams({ strict: false });
  const riskId = params.riskId;
  const risk = useRisk(riskId);
  const options = useRiskOptions();
  const { t: tc } = useTranslation();
  const [prefill] = useState(() => (riskId ? undefined : takeIdentified()));
  if ((riskId && risk.isPending) || options.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (riskId && risk.isError) return <StatusBanner tone="danger" title={errorMessage(tc, risk.error)} />;
  if (options.isError) return <StatusBanner tone="danger" title={errorMessage(tc, options.error)} />;
  return (
    <RiskForm
      key={risk.data?.id ?? 'new'}
      riskId={riskId}
      number={risk.data?.number}
      version={risk.data?.version}
      initial={risk.data?.values ?? { ...emptyValues(), ...prefill }}
      fromAi={!!prefill}
      options={options.data}
    />
  );
}

function Grid({ children }: { children: ReactNode }) {
  return <div className="grid gap-4 md:grid-cols-2">{children}</div>;
}

function RiskForm({
  riskId,
  number,
  version,
  initial,
  fromAi,
  options,
}: {
  riskId?: string;
  number?: string;
  version?: number;
  initial: RiskValues;
  fromAi: boolean;
  options: RiskOptions;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const navigate = useNavigate();
  const toast = useToast();
  const labels = useRiskLabels();
  const axis = useAxisLabels();
  const [v, setV] = useState<RiskValues>(initial);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const create = useRiskMutation(riskApi.create);
  const update = useRiskMutation(riskApi.update);
  const set = <K extends keyof RiskValues>(key: K, value: RiskValues[K]) => setV((prev) => ({ ...prev, [key]: value }));
  const text = (key: keyof RiskValues) => (v[key] as string | null) ?? '';
  const framework = options.framework.content;
  const bands = framework.bands;
  const method = framework.scoringMethod;
  const opportunities = options.opportunitiesEnabled;

  const require = (checks: [string, boolean][]) => {
    const next: Record<string, string> = {};
    for (const [field, ok] of checks) if (!ok) next[field] = t('wizard.required');
    setErrors(next);
    return Object.keys(next).length === 0;
  };

  function onError(error: unknown) {
    if (error instanceof ApiError && error.problem.errors?.length) {
      const next: Record<string, string> = {};
      for (const e of error.problem.errors) next[e.field] = translateMessageKey(tc, e.messageKey, e.params ?? undefined);
      setErrors(next);
    }
    toast({ tone: 'error', title: errorMessage(tc, error) });
  }

  function save() {
    setErrors({});
    const values: RiskValues = {
      ...v,
      opportunity: opportunities ? v.opportunity : false,
      objectiveCodes: v.objectiveCodes ?? [],
    };
    const done = (risk: { id: string }) => void navigate({ to: '/risk/risks/$riskId', params: { riskId: risk.id } });
    if (riskId && version !== undefined) update.mutate({ id: riskId, values, version }, { onSuccess: done, onError });
    else create.mutate(values, { onSuccess: done, onError });
  }

  const lookupSelect = (key: keyof RiskValues, type: string, filter?: (code: string) => boolean) => (
    <Select
      value={(v[key] as string | null) ?? NONE}
      onValueChange={(value) => set(key, (value === NONE ? null : value) as never)}
      options={[
        { value: NONE, label: t('common.none') },
        ...labels.lookupOptions(type).filter((o) => !filter || filter(o.value)),
      ]}
    />
  );

  const rating = (l: number | null, i: number | null) =>
    previewRating(method, bands, l || null, i || null);
  const chip = (l: number | null, i: number | null, testId: string) => {
    if (!l || !i) return <span className="text-sm text-muted-foreground">—</span>;
    const r = rating(l, i);
    return r ? (
      <RatingChip band={r.band?.code} score={r.score} bands={bands} testId={testId} />
    ) : (
      <span className="text-sm text-muted-foreground" data-testid={testId}>
        {t('wizard.computedOnSave')}
      </span>
    );
  };

  const area = v.inherentImpactArea ?? framework.impactAreas[0]?.code;

  const steps: WizardStep[] = [
    {
      key: 'statement',
      title: t('wizard.steps.statement'),
      validate: () => require([['titleEn', !!(v.titleAr?.trim() || v.titleEn?.trim())]]),
      content: (
        <div className="flex flex-col gap-4" data-testid="risk-wizard-statement">
          {fromAi && <AiBadge label={t('ai.prefilled')} className="self-start" />}
          <Grid>
            <FormField label={t('fields.titleAr')} error={errors.titleAr}>
              <Input dir="rtl" value={text('titleAr')} onChange={(e) => set('titleAr', e.target.value || null)} data-testid="risk-title-ar" />
            </FormField>
            <FormField label={t('fields.titleEn')} error={errors.titleEn}>
              <Input dir="ltr" value={text('titleEn')} onChange={(e) => set('titleEn', e.target.value || null)} data-testid="risk-title-en" />
            </FormField>
          </Grid>
          <p className="text-sm text-muted-foreground">{t('wizard.statementHint')}</p>
          <FormField label={t('fields.cause')} error={errors.cause}>
            <Textarea value={text('cause')} onChange={(e) => set('cause', e.target.value || null)} data-testid="risk-cause" />
          </FormField>
          <FormField label={t('fields.eventText')} error={errors.eventText}>
            <Textarea value={text('eventText')} onChange={(e) => set('eventText', e.target.value || null)} data-testid="risk-event" />
          </FormField>
          <FormField label={t('fields.consequence')} error={errors.consequence}>
            <Textarea value={text('consequence')} onChange={(e) => set('consequence', e.target.value || null)} data-testid="risk-consequence" />
          </FormField>
          <StatementTools values={v} riskId={riskId} onRewrite={(r) => setV((prev) => ({ ...prev, ...r }))} />
        </div>
      ),
    },
    {
      key: 'classification',
      title: t('wizard.steps.classification'),
      validate: () =>
        require([
          ['unitId', !!v.unitId],
          ['categoryCodes', v.categoryCodes.length > 0],
          ['identifiedOn', !!v.identifiedOn],
        ]),
      content: (
        <div className="flex flex-col gap-4" data-testid="risk-wizard-classification">
          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium">{t('fields.categories')}</legend>
            <div className="flex flex-wrap gap-4" data-testid="risk-categories">
              {options.categories
                .filter((c) => c.active)
                .map((c) => (
                  <Checkbox
                    key={c.code}
                    label={labels.category(c.code)}
                    checked={v.categoryCodes.includes(c.code)}
                    onChange={(e) =>
                      set(
                        'categoryCodes',
                        e.target.checked ? [...v.categoryCodes, c.code] : v.categoryCodes.filter((x) => x !== c.code),
                      )
                    }
                  />
                ))}
            </div>
            {errors.categoryCodes && <p className="text-sm text-danger">{errors.categoryCodes}</p>}
          </fieldset>
          <Grid>
            <FormField label={t('fields.riskType')}>
              <Select
                value={v.riskType}
                onValueChange={(value) => set('riskType', value)}
                options={riskTypes.map((x) => ({ value: x, label: k(`riskType.${x}`) }))}
              />
            </FormField>
            <FormField label={t('fields.determinedBy')}>{lookupSelect('determinedByCode', 'risk.determined-by')}</FormField>
            <FormField label={t('fields.identifiedOn')} error={errors.identifiedOn}>
              <Input type="date" value={v.identifiedOn} onChange={(e) => set('identifiedOn', e.target.value)} />
            </FormField>
            <FormField label={t('fields.exposureStart')}>
              <Input type="date" value={text('exposureStart')} onChange={(e) => set('exposureStart', e.target.value || null)} />
            </FormField>
            <FormField label={t('fields.unit')} error={errors.unitId}>
              <UnitSelect value={v.unitId || undefined} onChange={(value) => set('unitId', value ?? '')} />
            </FormField>
            <FormField label={t('fields.owner')} error={errors.ownerEmployeeId}>
              <EmployeePicker value={v.ownerEmployeeId ?? undefined} onChange={(x) => set('ownerEmployeeId', x ?? null)} />
            </FormField>
            <FormField label={t('fields.coordinator')}>
              <EmployeePicker value={v.coordinatorEmployeeId ?? undefined} onChange={(x) => set('coordinatorEmployeeId', x ?? null)} />
            </FormField>
            <FormField label={t('fields.followUp')}>
              <EmployeePicker value={v.followUpEmployeeId ?? undefined} onChange={(x) => set('followUpEmployeeId', x ?? null)} />
            </FormField>
            <FormField label={t('fields.processes')} className="md:col-span-2">
              <Input value={text('processes')} onChange={(e) => set('processes', e.target.value || null)} />
            </FormField>
          </Grid>
          <div className="flex flex-wrap gap-6">
            <Checkbox label={t('fields.emerging')} checked={v.emerging} onChange={(e) => set('emerging', e.target.checked)} />
            {opportunities && (
              <Checkbox label={t('fields.opportunity')} checked={v.opportunity} onChange={(e) => set('opportunity', e.target.checked)} />
            )}
          </div>
          {options.objectives.length > 0 && (
            <fieldset className="flex flex-col gap-2">
              <legend className="text-sm font-medium">{t('fields.objectives')}</legend>
              <div className="flex flex-wrap gap-4">
                {options.objectives.map((o) => (
                  <Checkbox
                    key={o.code}
                    label={pickText(language, o.labelAr, o.labelEn) || o.code}
                    checked={(v.objectiveCodes ?? []).includes(o.code)}
                    onChange={(e) =>
                      set(
                        'objectiveCodes',
                        e.target.checked
                          ? [...(v.objectiveCodes ?? []), o.code]
                          : (v.objectiveCodes ?? []).filter((x) => x !== o.code),
                      )
                    }
                  />
                ))}
              </div>
            </fieldset>
          )}
        </div>
      ),
    },
    {
      key: 'assessment',
      title: t('wizard.steps.assessment'),
      validate: () =>
        require([
          ['inherentLikelihood', v.inherentLikelihood > 0 && v.inherentImpact > 0],
          ['residualLikelihood', (v.residualLikelihood == null) === (v.residualImpact == null)],
          ['targetLikelihood', (v.targetLikelihood == null) === (v.targetImpact == null)],
        ]),
      content: (
        <div className="flex flex-col gap-6" data-testid="risk-wizard-assessment">
          <section className="flex flex-col gap-3">
            <header className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="font-semibold">{t('rating.inherent')}</h3>
              {chip(v.inherentLikelihood, v.inherentImpact, 'risk-wizard-inherent-chip')}
            </header>
            <RiskMatrix
              cells={options.matrix}
              bands={bands}
              prefix="inherent"
              label={t('rating.inherent')}
              axis={axis}
              value={{ likelihood: v.inherentLikelihood, impact: v.inherentImpact }}
              onChange={(l, i) => setV((p) => ({ ...p, inherentLikelihood: l, inherentImpact: i }))}
            />
            {errors.inherentLikelihood && <p className="text-sm text-danger">{errors.inherentLikelihood}</p>}
            <FormField label={t('fields.impactArea')}>
              <Select
                value={v.inherentImpactArea ?? NONE}
                onValueChange={(value) => set('inherentImpactArea', value === NONE ? null : value)}
                options={[
                  { value: NONE, label: t('common.none') },
                  ...framework.impactAreas.map((a) => ({ value: a.code, label: pickText(language, a.labelAr, a.labelEn) || a.code })),
                ]}
              />
            </FormField>
            {area && v.inherentImpact > 0 && (
              <p className="rounded border bg-muted/40 p-3 text-sm" data-testid="risk-impact-criteria">
                {(() => {
                  const level = framework.impact.find((x) => x.value === v.inherentImpact);
                  const c = level?.criteria?.[area];
                  return `${pickText(language, level?.labelAr, level?.labelEn)}: ${pickText(language, c?.ar, c?.en) || '—'}`;
                })()}
              </p>
            )}
          </section>
          <div className="grid gap-6 xl:grid-cols-2">
            {(['residual', 'target'] as const).map((kind) => {
              const lk = kind === 'residual' ? 'residualLikelihood' : 'targetLikelihood';
              const ik = kind === 'residual' ? 'residualImpact' : 'targetImpact';
              return (
                <section key={kind} className="flex flex-col gap-3">
                  <header className="flex flex-wrap items-center justify-between gap-2">
                    <h3 className="font-semibold">
                      {t(`rating.${kind}`)} <span className="text-sm font-normal text-muted-foreground">({t('wizard.optional')})</span>
                    </h3>
                    <span className="flex items-center gap-2">
                      {chip(v[lk], v[ik], `risk-wizard-${kind}-chip`)}
                      {v[lk] != null && (
                        <Button variant="ghost" size="sm" onClick={() => setV((p) => ({ ...p, [lk]: null, [ik]: null }))}>
                          <X aria-hidden />
                          {tc('actions.clear')}
                        </Button>
                      )}
                    </span>
                  </header>
                  <RiskMatrix
                    cells={options.matrix}
                    bands={bands}
                    prefix={kind}
                    label={t(`rating.${kind}`)}
                    value={{ likelihood: v[lk], impact: v[ik] }}
                    onChange={(l, i) => setV((p) => ({ ...p, [lk]: l, [ik]: i }))}
                  />
                  {errors[lk] && <p className="text-sm text-danger">{errors[lk]}</p>}
                </section>
              );
            })}
          </div>
          <Grid>
            <FormField label={t('fields.targetDate')} error={errors.targetDate}>
              <Input type="date" value={text('targetDate')} onChange={(e) => set('targetDate', e.target.value || null)} />
            </FormField>
            <FormField label={t('fields.response')} error={errors.responseCode}>
              {lookupSelect('responseCode', 'risk.response', (code) => opportunities || (code !== 'exploit' && code !== 'enhance'))}
            </FormField>
          </Grid>
          <Checkbox label={t('fields.needsFollowUp')} checked={v.needsFollowUp} onChange={(e) => set('needsFollowUp', e.target.checked)} />
        </div>
      ),
    },
    {
      key: 'review',
      title: t('wizard.steps.review'),
      content: <Summary values={v} chip={chip} />,
    },
  ];

  return (
    <RiskPageLayout testId="risk-wizard">
      <PageHeader
        title={riskId ? t('wizard.editTitle', { number }) : t('wizard.newTitle')}
        description={t('wizard.description')}
      />
      {Object.keys(errors).length > 0 && (
        <StatusBanner tone="danger" title={t('wizard.fixErrors')} description={Object.values(errors).join(' · ')} />
      )}
      <Wizard steps={steps} onFinish={save} busy={create.isPending || update.isPending} finishLabel={tc('actions.save')} />
    </RiskPageLayout>
  );
}

function Summary({
  values: v,
  chip,
}: {
  values: RiskValues;
  chip: (l: number | null, i: number | null, testId: string) => ReactNode;
}) {
  const { t } = useTranslation('risk');
  const { k, language } = useRiskT();
  const labels = useRiskLabels();
  const names = useNames();
  const rows: [string, ReactNode][] = [
    [t('fields.title'), names.text(v.titleAr, v.titleEn)],
    [t('fields.cause'), v.cause],
    [t('fields.eventText'), v.eventText],
    [t('fields.consequence'), v.consequence],
    [t('fields.categories'), v.categoryCodes.map(labels.category).join('، ')],
    [t('fields.riskType'), k(`riskType.${v.riskType}`)],
    [t('fields.identifiedOn'), v.identifiedOn ? formatDate(v.identifiedOn, language) : '—'],
    [t('fields.response'), labels.lookup('risk.response', v.responseCode)],
    [t('rating.inherent'), chip(v.inherentLikelihood, v.inherentImpact, 'risk-summary-inherent')],
    [t('rating.residual'), chip(v.residualLikelihood, v.residualImpact, 'risk-summary-residual')],
    [t('rating.target'), chip(v.targetLikelihood, v.targetImpact, 'risk-summary-target')],
    [t('fields.objectives'), (v.objectiveCodes ?? []).map(labels.objective).join('، ')],
  ];
  return (
    <div className="flex flex-col gap-4" data-testid="risk-wizard-summary">
      <dl className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-2">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-sm text-muted-foreground">{label}</dt>
            <dd className="mt-1 whitespace-pre-line">{value || '—'}</dd>
          </div>
        ))}
      </dl>
      <StatusBanner tone="info" title={t('wizard.afterSave')} />
    </div>
  );
}

/** "Check statement" (deterministic flags + AI rewrite) and the duplicate check across units. */
function StatementTools({
  values,
  riskId,
  onRewrite,
}: {
  values: RiskValues;
  riskId?: string;
  onRewrite: (r: Pick<RiskValues, 'cause' | 'eventText' | 'consequence'>) => void;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const names = useNames();
  const toast = useToast();
  const canCheck = useCan('risk.ai.quality');
  const canDuplicates = useCan('risk.ai.duplicates');
  const [check, setCheck] = useState<StatementCheck>();
  const [duplicates, setDuplicates] = useState<DuplicateCheck>();
  const [busy, setBusy] = useState<'check' | 'duplicates'>();
  if (!canCheck && !canDuplicates) return null;
  async function runCheck() {
    setBusy('check');
    try {
      setCheck(
        await riskApi.statementCheck({ cause: values.cause, eventText: values.eventText, consequence: values.consequence }),
      );
    } catch (e) {
      toast({ tone: 'error', title: errorMessage(tc, e) });
    } finally {
      setBusy(undefined);
    }
  }
  async function runDuplicates() {
    setBusy('duplicates');
    try {
      const text = [values.titleEn, values.titleAr, values.eventText].filter(Boolean).join(' ').slice(0, 2000);
      setDuplicates(await riskApi.duplicates({ text, excludeId: riskId ?? null }));
    } catch (e) {
      toast({ tone: 'error', title: errorMessage(tc, e) });
    } finally {
      setBusy(undefined);
    }
  }
  const hasRewrite = check && (check.cause || check.eventText || check.consequence);
  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-muted/30 p-4">
      <div className="flex flex-wrap gap-2">
        {canCheck && (
          <Button variant="outline" size="sm" onClick={() => void runCheck()} loading={busy === 'check'} data-testid="risk-statement-check">
            <Sparkles aria-hidden />
            {t('ai.statement.action')}
          </Button>
        )}
        {canDuplicates && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => void runDuplicates()}
            loading={busy === 'duplicates'}
            disabled={!values.titleAr && !values.titleEn && !values.eventText}
            data-testid="risk-duplicate-check"
          >
            <Copy aria-hidden />
            {t('ai.duplicates.action')}
          </Button>
        )}
      </div>
      {check && (
        <div className="flex flex-col gap-2 text-sm" data-testid="risk-statement-result">
          {check.flags.length === 0 ? (
            <p className="text-success">{t('ai.statement.noFlags')}</p>
          ) : (
            <ul className="list-disc ps-5">
              {check.flags.map((f) => (
                <li key={f}>{k(`statementFlags.${f}`)}</li>
              ))}
            </ul>
          )}
          {check.issues.length > 0 && (
            <ul className="list-disc ps-5 text-muted-foreground">
              {check.issues.map((issue, index) => (
                <li key={index}>{issue}</li>
              ))}
            </ul>
          )}
          {hasRewrite && (
            <div className="flex flex-col gap-1 rounded border bg-card p-3">
              <AiBadge label={t('ai.generatedDraft')} className="self-start" />
              <p>
                <span className="text-muted-foreground">{t('fields.cause')}:</span> {check.cause}
              </p>
              <p>
                <span className="text-muted-foreground">{t('fields.eventText')}:</span> {check.eventText}
              </p>
              <p>
                <span className="text-muted-foreground">{t('fields.consequence')}:</span> {check.consequence}
              </p>
              <Button
                size="sm"
                className="self-start"
                data-testid="risk-statement-use"
                onClick={() =>
                  onRewrite({
                    cause: check.cause ?? values.cause,
                    eventText: check.eventText ?? values.eventText,
                    consequence: check.consequence ?? values.consequence,
                  })
                }
              >
                {t('ai.statement.use')}
              </Button>
            </div>
          )}
        </div>
      )}
      {duplicates && (
        <div className="flex flex-col gap-1 text-sm" data-testid="risk-duplicates-result">
          {duplicates.visible.length === 0 && duplicates.otherUnits === 0 && <p>{t('ai.duplicates.none')}</p>}
          {duplicates.visible.length > 0 && (
            <ul className="flex flex-col gap-1">
              {duplicates.visible.map((d) => (
                <li key={d.id}>
                  <a href={`/risk/risks/${d.id}`} target="_blank" rel="noreferrer" className="text-primary hover:underline">
                    <bdi>{d.number}</bdi> · {names.text(d.titleAr, d.titleEn)}
                  </a>{' '}
                  <span className="text-muted-foreground">({Math.round(d.score * 100)}%)</span>
                </li>
              ))}
            </ul>
          )}
          {duplicates.otherUnits > 0 && (
            <p className="text-muted-foreground">{t('ai.duplicates.otherUnits', { count: duplicates.otherUnits })}</p>
          )}
        </div>
      )}
    </div>
  );
}
