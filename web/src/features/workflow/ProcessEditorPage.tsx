import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowDown, ArrowLeft, ArrowUp, CircleCheck, Plus, Trash2 } from 'lucide-react';
import { useEffect, useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { useServerErrors } from '@/features/settings/common';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
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
  type Tone,
} from '@/ui';
import {
  useConditionCheck,
  usePreview,
  useProcess,
  useSaveProcess,
  type ApproverRule,
  type Preview,
  type ProcessDetail,
  type StepDefinition,
} from './api';

const route = getRouteApi('/app/settings/approvals/$processKey');

const ruleKinds = [
  'sectionManager',
  'departmentDirector',
  'headOfUnit',
  'submitterManager',
  'role',
  'employee',
  'owningUnit',
  'recordRole',
  'teamRole',
] as const;

type ServerErrors = ReturnType<typeof useServerErrors>;

function newStep(index: number): StepDefinition {
  return {
    code: `step-${index + 1}`,
    nameAr: null,
    nameEn: null,
    rule: { kind: 'departmentDirector', of: 'record' },
    condition: null,
    actions: ['approve', 'reject', 'return'],
    customActions: [],
    commentOnApprove: false,
    commentOnReject: true,
    commentOnReturn: true,
    slaHours: null,
    escalation: null,
    onNoApprover: 'block',
    excludeSubmitter: true,
    notificationTemplate: null,
    slaMode: 'working',
    businessHoursSource: 'default',
    businessHoursId: null,
  };
}

/** Editor of one approver rule (step approver or escalation target). */
function RuleEditor({
  rule,
  onChange,
  detail,
  field,
  errors,
  allowNone,
  disabled,
}: {
  rule: ApproverRule | null | undefined;
  onChange: (rule: ApproverRule | null) => void;
  detail: ProcessDetail;
  field: string;
  errors: ServerErrors;
  allowNone?: boolean;
  disabled: boolean;
}) {
  const { t, i18n } = useTranslation('workflow');
  const kindOptions = [
    ...(allowNone ? [{ value: 'none', label: t('editor.escalationNone') }] : []),
    ...ruleKinds.map((kind) => ({ value: kind, label: t(`kinds.${kind}`) })),
  ];
  const ofOptions = [
    { value: 'record', label: t('of.record') },
    { value: 'submitter', label: t('of.submitter') },
  ];
  const set = (patch: Partial<ApproverRule>) => rule && onChange({ ...rule, ...patch });
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <FormField
        label={allowNone ? t('editor.escalation') : t('editor.kind')}
        error={errors.errorFor(`${field}.kind`)}
      >
        <Select
          value={rule?.kind ?? 'none'}
          disabled={disabled}
          onValueChange={(kind) =>
            onChange(
              kind === 'none'
                ? null
                : { kind, of: ['sectionManager', 'departmentDirector'].includes(kind) ? 'record' : null },
            )
          }
          options={kindOptions}
        />
      </FormField>
      {rule && (rule.kind === 'sectionManager' || rule.kind === 'departmentDirector') && (
        <FormField label={t('editor.of')}>
          <Select
            value={rule.of ?? 'record'}
            disabled={disabled}
            onValueChange={(of) => set({ of })}
            options={ofOptions}
          />
        </FormField>
      )}
      {rule?.kind === 'role' && (
        <FormField label={t('editor.roleKey')} error={errors.errorFor(`${field}.roleKey`)}>
          <Select
            value={rule.roleKey ?? undefined}
            disabled={disabled}
            onValueChange={(roleKey) => set({ roleKey })}
            options={detail.roles.map((role) => ({
              value: role.code,
              label: pickText(i18n.language, role.labelAr, role.labelEn) || role.code,
            }))}
          />
        </FormField>
      )}
      {rule?.kind === 'headOfUnit' && (
        <FormField
          label={t('editor.unit')}
          hint={t('editor.unitFromRecord')}
          error={errors.errorFor(`${field}.unitId`)}
        >
          <UnitSelect
            value={rule.unitId ?? undefined}
            allowEmpty
            onChange={(unitId) => set({ unitId: unitId ?? null, of: unitId ? null : 'record' })}
          />
        </FormField>
      )}
      {rule?.kind === 'employee' && (
        <FormField label={t('editor.employee')} error={errors.errorFor(`${field}.employeeId`)}>
          <EmployeePicker
            value={rule.employeeId ?? undefined}
            onChange={(employeeId) => set({ employeeId: employeeId ?? null })}
          />
        </FormField>
      )}
      {rule?.kind === 'recordRole' && (
        <FormField label={t('editor.recordRole')} error={errors.errorFor(`${field}.roleCode`)}>
          <Select
            value={rule.roleCode ?? undefined}
            disabled={disabled}
            onValueChange={(roleCode) => set({ roleCode })}
            options={detail.recordRoles.map((role) => ({
              value: role.code,
              label: pickText(i18n.language, role.labelAr, role.labelEn) || role.code,
            }))}
          />
        </FormField>
      )}
      {rule?.kind === 'teamRole' && (
        <FormField label={t('editor.teamRole')} error={errors.errorFor(`${field}.roleCode`)}>
          <Input
            dir="ltr"
            value={rule.roleCode ?? ''}
            disabled={disabled}
            onChange={(e) => set({ roleCode: e.target.value })}
          />
        </FormField>
      )}
      {rule?.kind === 'owningUnit' && (
        <FormField label={t('editor.systemCode')} hint={t('editor.systemDefault')}>
          <Input
            dir="ltr"
            value={rule.systemCode ?? ''}
            disabled={disabled}
            placeholder={detail.systemCode}
            onChange={(e) => set({ systemCode: e.target.value || null })}
          />
        </FormField>
      )}
    </div>
  );
}

/** Condition editor: validated against the process's variables as the admin types (debounced), with the variables listed. */
function ConditionEditor({
  value,
  onChange,
  detail,
  error,
  disabled,
}: {
  value: string | null | undefined;
  onChange: (value: string | null) => void;
  detail: ProcessDetail;
  error: string | undefined;
  disabled: boolean;
}) {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const check = useConditionCheck(detail.key);
  const [result, setResult] = useState<{ valid: boolean; message?: string }>();
  const text = value ?? '';
  useEffect(() => {
    if (!text.trim()) {
      setResult(undefined);
      return;
    }
    const timer = window.setTimeout(() => {
      check.mutate(text, {
        onSuccess: (outcome) => {
          const first = outcome.errors[0];
          setResult({
            valid: outcome.valid,
            message: first ? translateMessageKey(tc, first.messageKey, first.params ?? undefined) : undefined,
          });
        },
      });
    }, 400);
    return () => window.clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-check only when the text changes
  }, [text]);
  const id = useId();
  return (
    <div className="flex flex-col gap-2">
      <FormField
        label={t('editor.condition')}
        hint={t('editor.conditionHint')}
        error={error ?? (result?.valid === false ? result.message : undefined)}
      >
        <Textarea
          dir="ltr"
          rows={2}
          className="min-h-16 font-mono text-sm"
          placeholder={t('editor.conditionPlaceholder')}
          value={text}
          disabled={disabled}
          aria-describedby={`${id}-variables`}
          onChange={(e) => onChange(e.target.value || null)}
        />
      </FormField>
      {result?.valid && (
        <p className="flex items-center gap-1 text-sm text-success" role="status">
          <CircleCheck className="size-4" aria-hidden /> {t('editor.conditionValid')}
        </p>
      )}
      <details id={`${id}-variables`} className="text-sm">
        <summary className="cursor-pointer text-muted-foreground">
          {t('editor.variables')} · {t('editor.functions')}
        </summary>
        <ul className="mt-2 flex flex-wrap gap-2">
          {detail.variables.map((variable) => (
            <li key={variable.name}>
              <StatusChip tone={variable.builtIn ? 'neutral' : 'info'}>
                <code dir="ltr">{variable.name}</code> ·{' '}
                {pickText(i18n.language, variable.labelAr, variable.labelEn)}
              </StatusChip>
            </li>
          ))}
        </ul>
        <p className="mt-2 text-xs text-muted-foreground" dir="ltr">
          {detail.functions.join(', ')} · and or not in == != &lt; &lt;= &gt; &gt;= + - * / %
        </p>
      </details>
    </div>
  );
}

function StepCard({
  step,
  index,
  count,
  detail,
  errors,
  disabled,
  onChange,
  onMove,
  onRemove,
}: {
  step: StepDefinition;
  index: number;
  count: number;
  detail: ProcessDetail;
  errors: ServerErrors;
  disabled: boolean;
  onChange: (step: StepDefinition) => void;
  onMove: (delta: number) => void;
  onRemove: () => void;
}) {
  const { t, i18n } = useTranslation('workflow');
  const at = `steps[${index}]`;
  const actions = step.actions ?? ['approve', 'reject', 'return'];
  const set = (patch: Partial<StepDefinition>) => onChange({ ...step, ...patch });
  const title = pickText(i18n.language, step.nameAr, step.nameEn);
  return (
    <section
      className="flex flex-col gap-4 rounded-lg border bg-card p-5"
      aria-label={t('editor.step', { number: index + 1 })}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">
          {t('editor.step', { number: index + 1 })}
          {title && <span className="text-muted-foreground"> · {title}</span>}
        </h3>
        {!disabled && (
          <div className="flex gap-1">
            <Button
              size="icon"
              variant="ghost"
              className="size-8"
              disabled={index === 0}
              onClick={() => onMove(-1)}
              aria-label={t('editor.moveUp')}
            >
              <ArrowUp aria-hidden />
            </Button>
            <Button
              size="icon"
              variant="ghost"
              className="size-8"
              disabled={index === count - 1}
              onClick={() => onMove(1)}
              aria-label={t('editor.moveDown')}
            >
              <ArrowDown aria-hidden />
            </Button>
            <Button
              size="icon"
              variant="ghost"
              className="size-8 text-danger"
              disabled={count === 1}
              onClick={onRemove}
              aria-label={t('editor.removeStep')}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        )}
      </header>
      <div className="grid gap-3 sm:grid-cols-3">
        <FormField label={t('editor.code')} hint={t('editor.codeHint')} error={errors.errorFor(`${at}.code`)}>
          <Input
            dir="ltr"
            value={step.code}
            disabled={disabled}
            onChange={(e) => set({ code: e.target.value })}
          />
        </FormField>
        <FormField label={t('editor.nameAr')} error={errors.errorFor(`${at}.nameAr`)}>
          <Input
            dir="rtl"
            lang="ar"
            value={step.nameAr ?? ''}
            disabled={disabled}
            onChange={(e) => set({ nameAr: e.target.value || null })}
          />
        </FormField>
        <FormField label={t('editor.nameEn')} error={errors.errorFor(`${at}.nameEn`)}>
          <Input
            dir="ltr"
            lang="en"
            value={step.nameEn ?? ''}
            disabled={disabled}
            onChange={(e) => set({ nameEn: e.target.value || null })}
          />
        </FormField>
      </div>
      <fieldset className="flex flex-col gap-3">
        <legend className="mb-2 text-sm font-semibold">{t('editor.approver')}</legend>
        <RuleEditor
          rule={step.rule}
          detail={detail}
          field={`${at}.rule`}
          errors={errors}
          disabled={disabled}
          onChange={(rule) => rule && set({ rule })}
        />
        <Switch
          checked={step.excludeSubmitter}
          disabled={disabled}
          onCheckedChange={(excludeSubmitter) => set({ excludeSubmitter })}
          label={t('editor.excludeSubmitter')}
        />
      </fieldset>
      <ConditionEditor
        value={step.condition}
        detail={detail}
        disabled={disabled}
        error={errors.errorFor(`${at}.condition`)}
        onChange={(condition) => set({ condition })}
      />
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-semibold">{t('editor.decisions')}</legend>
        <div className="flex flex-wrap gap-4">
          {(['approve', 'reject', 'return'] as const).map((action) => (
            <Checkbox
              key={action}
              label={t(`actions.${action}`)}
              checked={actions.includes(action)}
              disabled={disabled || action === 'approve'}
              onChange={(e) =>
                set({
                  actions: e.target.checked ? [...actions, action] : actions.filter((a) => a !== action),
                })
              }
            />
          ))}
        </div>
        {errors.errorFor(`${at}.actions`) && (
          <p className="text-sm text-danger">{errors.errorFor(`${at}.actions`)}</p>
        )}
        <div className="flex flex-wrap gap-4">
          {(['approve', 'reject', 'return'] as const)
            .filter((action) => actions.includes(action))
            .map((action) => {
              const key =
                action === 'approve'
                  ? 'commentOnApprove'
                  : action === 'reject'
                    ? 'commentOnReject'
                    : 'commentOnReturn';
              return (
                <Checkbox
                  key={key}
                  label={t(`editor.commentOn.${action}`)}
                  checked={step[key]}
                  disabled={disabled}
                  onChange={(e) => set({ [key]: e.target.checked })}
                />
              );
            })}
        </div>
        <div className="flex flex-col gap-2">
          <p className="text-sm font-medium">{t('editor.customActions')}</p>
          <p className="text-xs text-muted-foreground">{t('editor.customActionsHint')}</p>
          {(step.customActions ?? []).map((custom, c) => {
            const update = (patch: Partial<typeof custom>) =>
              set({
                customActions: (step.customActions ?? []).map((x, i) => (i === c ? { ...x, ...patch } : x)),
              });
            return (
              <div key={c} className="grid items-end gap-2 sm:grid-cols-[8rem_1fr_1fr_10rem_auto]">
                <FormField
                  label={t('editor.customCode')}
                  error={errors.errorFor(`${at}.customActions[${c}].code`)}
                >
                  <Input
                    dir="ltr"
                    value={custom.code}
                    disabled={disabled}
                    onChange={(e) => update({ code: e.target.value })}
                  />
                </FormField>
                <FormField
                  label={t('editor.customLabelAr')}
                  error={errors.errorFor(`${at}.customActions[${c}].labelEn`)}
                >
                  <Input
                    dir="rtl"
                    value={custom.labelAr}
                    disabled={disabled}
                    onChange={(e) => update({ labelAr: e.target.value })}
                  />
                </FormField>
                <FormField label={t('editor.customLabelEn')}>
                  <Input
                    dir="ltr"
                    value={custom.labelEn}
                    disabled={disabled}
                    onChange={(e) => update({ labelEn: e.target.value })}
                  />
                </FormField>
                <FormField label={t('editor.outcome')}>
                  <Select
                    value={custom.outcome}
                    disabled={disabled}
                    onValueChange={(outcome) => update({ outcome })}
                    options={actions.map((a) => ({ value: a, label: t(`actions.${a as 'approve'}`) }))}
                  />
                </FormField>
                <Button
                  size="icon"
                  variant="ghost"
                  disabled={disabled}
                  aria-label={t('editor.removeCustomAction')}
                  onClick={() => set({ customActions: (step.customActions ?? []).filter((_, i) => i !== c) })}
                >
                  <Trash2 aria-hidden />
                </Button>
              </div>
            );
          })}
          {!disabled && (
            <div>
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  set({
                    customActions: [
                      ...(step.customActions ?? []),
                      { code: '', labelAr: '', labelEn: '', outcome: 'approve' },
                    ],
                  })
                }
              >
                <Plus aria-hidden />
                {t('editor.addCustomAction')}
              </Button>
            </div>
          )}
        </div>
      </fieldset>
      <fieldset className="flex flex-col gap-3">
        <legend className="mb-2 text-sm font-semibold">{t('editor.deadline')}</legend>
        <div className="grid gap-3 sm:grid-cols-[12rem_1fr]">
          <FormField
            label={t('editor.sla')}
            hint={t('editor.slaHint')}
            error={errors.errorFor(`${at}.slaHours`)}
          >
            <Input
              type="number"
              min={1}
              max={8760}
              value={step.slaHours ?? ''}
              disabled={disabled}
              onChange={(e) => set({ slaHours: e.target.value ? Number(e.target.value) : null })}
            />
          </FormField>
          <RuleEditor
            rule={step.escalation}
            allowNone
            detail={detail}
            field={`${at}.escalation`}
            errors={errors}
            disabled={disabled || !step.slaHours}
            onChange={(escalation) => set({ escalation })}
          />
        </div>
        <div className="grid gap-3 sm:grid-cols-3">
          <FormField label={t('editor.slaMode')} hint={t('editor.slaModeHint')}>
            <Select
              value={step.slaMode}
              disabled={disabled || !step.slaHours}
              onValueChange={(slaMode) => set({ slaMode })}
              options={[
                { value: 'working', label: t('editor.slaModes.working') },
                { value: 'calendar', label: t('editor.slaModes.calendar') },
              ]}
            />
          </FormField>
          {step.slaMode === 'working' && (
            <FormField
              label={t('editor.businessHoursSource')}
              error={errors.errorFor(`${at}.businessHoursSource`)}
            >
              <Select
                value={step.businessHoursSource}
                disabled={disabled || !step.slaHours}
                onValueChange={(businessHoursSource) =>
                  set({
                    businessHoursSource,
                    businessHoursId: businessHoursSource === 'specific' ? step.businessHoursId : null,
                  })
                }
                options={[
                  { value: 'default', label: t('editor.businessHoursSources.default') },
                  { value: 'unit', label: t('editor.businessHoursSources.unit') },
                  { value: 'specific', label: t('editor.businessHoursSources.specific') },
                ]}
              />
            </FormField>
          )}
          {step.slaMode === 'working' && step.businessHoursSource === 'specific' && (
            <FormField label={t('editor.businessHours')} error={errors.errorFor(`${at}.businessHoursId`)}>
              <Select
                value={step.businessHoursId ?? undefined}
                disabled={disabled || !step.slaHours}
                onValueChange={(businessHoursId) => set({ businessHoursId })}
                options={detail.businessHours.map((record) => ({
                  value: record.code,
                  label: pickText(i18n.language, record.labelAr, record.labelEn) || record.code,
                }))}
              />
            </FormField>
          )}
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <FormField label={t('editor.onNoApprover')}>
            <Select
              value={step.onNoApprover}
              disabled={disabled}
              onValueChange={(onNoApprover) => set({ onNoApprover })}
              options={[
                { value: 'block', label: t('editor.noApprover.block') },
                { value: 'skip', label: t('editor.noApprover.skip') },
              ]}
            />
          </FormField>
          <FormField label={t('editor.template')} error={errors.errorFor(`${at}.notificationTemplate`)}>
            <Select
              value={step.notificationTemplate ?? '-'}
              disabled={disabled}
              onValueChange={(template) => set({ notificationTemplate: template === '-' ? null : template })}
              options={[
                { value: '-', label: t('editor.templateDefault') },
                ...detail.templates.map((template) => ({
                  value: template.code,
                  label: pickText(i18n.language, template.labelAr, template.labelEn) || template.code,
                })),
              ]}
            />
          </FormField>
        </div>
      </fieldset>
    </section>
  );
}

/** "Who would approve this sample record": runs the unsaved steps for a sample submitter, unit and values (nothing stored). */
function PreviewPanel({ detail, steps }: { detail: ProcessDetail; steps: StepDefinition[] }) {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const preview = usePreview(detail.key);
  const [submitter, setSubmitter] = useState<string>();
  const [unit, setUnit] = useState<string>();
  const [values, setValues] = useState<Record<string, string>>({});
  const moduleVariables = detail.variables.filter((v) => !v.builtIn);
  const result: Preview | undefined = preview.data;
  const outcomeTone: Record<string, Tone | undefined> = {
    included: 'success',
    skipped: 'neutral',
    noApprover: 'warning',
    blocked: 'danger',
  };
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-5" aria-label={t('preview.title')}>
      <div>
        <h2 className="font-semibold">{t('preview.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('preview.description')}</p>
      </div>
      <FormField label={t('preview.submitter')}>
        <EmployeePicker value={submitter} onChange={setSubmitter} />
      </FormField>
      <FormField label={t('preview.unit')} hint={t('preview.unitDefault')}>
        <UnitSelect value={unit} allowEmpty onChange={setUnit} />
      </FormField>
      {moduleVariables.map((variable) => (
        <FormField
          key={variable.name}
          label={pickText(i18n.language, variable.labelAr, variable.labelEn) || variable.name}
        >
          {variable.type === 'boolean' ? (
            <Select
              value={values[variable.name] ?? '-'}
              onValueChange={(value) => setValues({ ...values, [variable.name]: value })}
              options={[
                { value: '-', label: t('preview.none') },
                { value: 'true', label: t('preview.yes') },
                { value: 'false', label: t('preview.no') },
              ]}
            />
          ) : (
            <Input
              dir="ltr"
              type={variable.type === 'number' ? 'number' : variable.type === 'date' ? 'date' : 'text'}
              value={values[variable.name] ?? ''}
              onChange={(e) => setValues({ ...values, [variable.name]: e.target.value })}
            />
          )}
        </FormField>
      ))}
      <div>
        <Button
          variant="secondary"
          loading={preview.isPending}
          onClick={() =>
            preview.mutate({
              steps,
              submitterEmployeeId: submitter ?? null,
              unitId: unit ?? null,
              variables: Object.fromEntries(
                moduleVariables
                  .filter(
                    (v) => values[v.name] !== undefined && values[v.name] !== '' && values[v.name] !== '-',
                  )
                  .map((v) => [
                    v.name,
                    v.type === 'number'
                      ? Number(values[v.name])
                      : v.type === 'boolean'
                        ? values[v.name] === 'true'
                        : values[v.name],
                  ]),
              ),
            })
          }
        >
          {t('preview.run')}
        </Button>
      </div>
      {preview.isError && <StatusBanner tone="danger" title={errorMessage(tc, preview.error)} />}
      {result && (
        <div className="flex flex-col gap-3" aria-live="polite">
          <StatusBanner
            tone={
              result.result === 'blocked'
                ? 'danger'
                : result.result === 'autoApproved'
                  ? 'warning'
                  : 'success'
            }
            title={t(`preview.result.${result.result as 'included'}`)}
          />
          <ol className="flex flex-col gap-2">
            {result.steps.map((step) => (
              <li key={step.index} className="flex flex-col gap-1 rounded-md border p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium">
                    {step.index + 1}. {pickText(i18n.language, step.nameAr, step.nameEn) || step.code}
                  </span>
                  <StatusChip tone={outcomeTone[step.outcome] ?? 'neutral'}>
                    {t(`preview.outcomes.${step.outcome as 'included'}`)}
                  </StatusChip>
                  {step.conditionResult === true && (
                    <StatusChip tone="info">{t('preview.conditionTrue')}</StatusChip>
                  )}
                </div>
                {step.outcome === 'included' && (
                  <p className="text-sm">
                    {step.approvers.map((a) => pickText(i18n.language, a.nameAr, a.nameEn)).join('، ') ||
                      t('preview.nobody')}
                  </p>
                )}
                {step.conditionError && <p className="text-sm text-warning">{t('preview.conditionError')}</p>}
                {step.notes.map((note) => (
                  <p key={note} className="text-xs text-muted-foreground">
                    {translateMessageKey(tc, note)}
                  </p>
                ))}
              </li>
            ))}
          </ol>
          <details className="text-sm">
            <summary className="cursor-pointer text-muted-foreground">{t('preview.facts')}</summary>
            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              {result.facts.map((fact) => (
                <div key={fact.name} className="contents">
                  <dt className="text-muted-foreground">
                    {t(`preview.factNames.${fact.name as 'submitter'}`)}
                  </dt>
                  <dd>
                    {pickText(i18n.language, fact.displayAr, fact.displayEn) ||
                      (fact.value === 'true'
                        ? t('preview.yes')
                        : fact.value === 'false'
                          ? t('preview.no')
                          : (fact.value ?? t('preview.none')))}
                  </dd>
                </div>
              ))}
            </dl>
          </details>
        </div>
      )}
    </section>
  );
}

function VersionsTable({ detail, current }: { detail: ProcessDetail; current: number }) {
  const { t, i18n } = useTranslation('workflow');
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('editor.versions.version')}</TableHeaderCell>
          <TableHeaderCell>{t('editor.versions.note')}</TableHeaderCell>
          <TableHeaderCell>{t('editor.versions.by')}</TableHeaderCell>
          <TableHeaderCell>{t('editor.versions.at')}</TableHeaderCell>
          <TableHeaderCell>{t('editor.versions.steps')}</TableHeaderCell>
          <TableHeaderCell>
            <span className="sr-only">{t('editor.versions.view')}</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {detail.versions.map((version) => (
          <TableRow key={version.versionNo}>
            <TableCell>
              {version.versionNo}
              {version.versionNo === current && (
                <StatusChip tone="success" className="ms-2">
                  {t('editor.versions.current')}
                </StatusChip>
              )}
            </TableCell>
            <TableCell>{version.note ?? '—'}</TableCell>
            <TableCell>{version.createdByName ?? '—'}</TableCell>
            <TableCell>{formatDateTime(version.createdAt, i18n.language)}</TableCell>
            <TableCell>{version.stepCount}</TableCell>
            <TableCell>
              <Link
                to="/settings/approvals/$processKey"
                params={{ processKey: detail.key }}
                search={{ version: version.versionNo === current ? undefined : version.versionNo }}
                className="text-sm text-primary hover:underline"
              >
                {t('editor.versions.view')}
              </Link>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** Process editor of the Approval Settings centre: ordered steps, live preview, versions (ADR 0024). */
export function ProcessEditorPage() {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const { processKey } = route.useParams();
  const { version } = route.useSearch();
  const toast = useToast();
  const canManage = useCan('workflow.process.manage');
  const latest = useProcess(processKey);
  const shown = useProcess(processKey, version);
  const save = useSaveProcess(processKey);
  const errors = useServerErrors();
  const [steps, setSteps] = useState<StepDefinition[]>();
  const [note, setNote] = useState('');
  const [tab, setTab] = useState('steps');
  useEffect(() => {
    if (shown.data) setSteps(shown.data.steps.map((s) => ({ ...s, customActions: s.customActions ?? [] })));
  }, [shown.data]);
  const dirty = useMemo(
    () =>
      JSON.stringify(steps) !==
      JSON.stringify(shown.data?.steps.map((s) => ({ ...s, customActions: s.customActions ?? [] }))),
    [steps, shown.data],
  );

  if (shown.isError) return <StatusBanner tone="danger" title={errorMessage(tc, shown.error)} />;
  if (!shown.data || !latest.data || !steps) return <Skeleton className="h-96 rounded-lg" />;
  const detail = shown.data;
  const currentVersion = latest.data.versionNo;

  return (
    <div className="flex flex-col gap-6">
      <Link to="/settings/approvals" className="flex items-center gap-1 text-sm text-primary hover:underline">
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('editor.back')}
      </Link>
      <PageHeader
        title={pickText(i18n.language, detail.nameAr, detail.nameEn) || detail.key}
        description={pickText(i18n.language, detail.descriptionAr, detail.descriptionEn) || undefined}
        meta={
          <StatusChip tone="info">
            {detail.versionNo ? t('editor.version', { version: detail.versionNo }) : t('editor.noVersion')}
          </StatusChip>
        }
      />
      {version && version !== currentVersion && (
        <StatusBanner
          tone="warning"
          title={t('editor.versions.viewing', { version })}
          actions={
            <Button asChild size="sm" variant="outline">
              <Link to="/settings/approvals/$processKey" params={{ processKey }} search={{}}>
                {t('editor.versions.backToCurrent')}
              </Link>
            </Button>
          }
        />
      )}
      <p className="text-sm text-muted-foreground">{t('editor.inFlight')}</p>
      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="steps">{t('editor.tabs.steps')}</TabsTrigger>
          <TabsTrigger value="versions">{t('editor.tabs.versions')}</TabsTrigger>
        </TabsList>
        <TabsContent value="steps">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <div className="flex flex-col gap-4">
              {errors.errorFor('steps') && <StatusBanner tone="danger" title={errors.errorFor('steps')} />}
              {steps.map((step, index) => (
                <StepCard
                  key={index}
                  step={step}
                  index={index}
                  count={steps.length}
                  detail={detail}
                  errors={errors}
                  disabled={!canManage}
                  onChange={(changed) => setSteps(steps.map((s, i) => (i === index ? changed : s)))}
                  onMove={(delta) => {
                    const next = [...steps];
                    next.splice(index + delta, 0, ...next.splice(index, 1));
                    setSteps(next);
                  }}
                  onRemove={() => setSteps(steps.filter((_, i) => i !== index))}
                />
              ))}
              {canManage && (
                <>
                  <div>
                    <Button variant="outline" onClick={() => setSteps([...steps, newStep(steps.length)])}>
                      <Plus aria-hidden />
                      {t('editor.addStep')}
                    </Button>
                  </div>
                  <form
                    className="flex flex-col gap-3 rounded-lg border bg-card p-5"
                    onSubmit={(event) => {
                      event.preventDefault();
                      save.mutate(
                        { steps, note: note.trim() || null, baseVersionNo: currentVersion },
                        {
                          onSuccess: (saved) => {
                            errors.clear();
                            setNote('');
                            toast({ title: t('editor.saved', { version: saved.versionNo }) });
                          },
                          onError: errors.fail,
                        },
                      );
                    }}
                  >
                    {dirty && <p className="text-sm text-warning">{t('editor.unsaved')}</p>}
                    <FormField label={t('editor.note')} error={errors.errorFor('note')}>
                      <Input
                        value={note}
                        maxLength={1000}
                        placeholder={t('editor.notePlaceholder')}
                        onChange={(e) => setNote(e.target.value)}
                      />
                    </FormField>
                    <div className="flex flex-wrap gap-2">
                      <Button type="submit" loading={save.isPending}>
                        {t('editor.save')}
                      </Button>
                      {dirty && (
                        <Button
                          type="button"
                          variant="outline"
                          onClick={() => {
                            errors.clear();
                            setSteps(
                              detail.steps.map((s) => ({ ...s, customActions: s.customActions ?? [] })),
                            );
                          }}
                        >
                          {t('editor.reset')}
                        </Button>
                      )}
                    </div>
                  </form>
                </>
              )}
            </div>
            <div className="xl:sticky xl:top-4 xl:self-start">
              <PreviewPanel detail={detail} steps={steps} />
            </div>
          </div>
        </TabsContent>
        <TabsContent value="versions">
          <VersionsTable detail={latest.data} current={currentVersion} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
