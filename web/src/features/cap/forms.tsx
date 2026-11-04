import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { UnitSelect } from '@/features/access/pickers';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { Checkbox, DatePicker, FormField, Input, Select, Textarea } from '@/ui';
import { capApi, priorities, type CapActionValues, type CapRemarkValues, type CapSourceValues } from './api';
import { SimpleDialog, lookupTypes, useCapLabels, useCapT, useDialogMutation } from './components';

const today = () => new Date().toISOString().slice(0, 10);

/** New source of remarks (manual: workshop, audit report, complaint…). */
export function SourceDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (id: string) => void;
}) {
  const { t } = useTranslation('cap');
  const labels = useCapLabels();
  const [v, setV] = useState<CapSourceValues>({
    titleAr: '',
    titleEn: '',
    sourceTypeCode: 'workshop',
    description: '',
    receivedOn: today(),
    ownerEmployeeId: null,
    unitId: null,
    teamId: null,
  });
  const m = useDialogMutation(capApi.createSource, onClose);
  const set = (patch: Partial<CapSourceValues>) => setV((x) => ({ ...x, ...patch }));
  return (
    <SimpleDialog
      title={t('sources.new')}
      description={t('sources.newHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.create')}
      disabled={!(v.titleAr || v.titleEn)}
      testId="cap-source-save"
      onSubmit={() =>
        m.run(
          {
            ...v,
            titleAr: v.titleAr || null,
            titleEn: v.titleEn || null,
            description: v.description || null,
          },
          (s) => onCreated(s.id),
        )
      }
    >
      <FormField label={t('fields.titleAr')} required>
        <Input
          dir="rtl"
          value={v.titleAr ?? ''}
          onChange={(e) => set({ titleAr: e.target.value })}
          data-testid="cap-source-title-ar"
        />
      </FormField>
      <FormField label={t('fields.titleEn')}>
        <Input
          dir="ltr"
          value={v.titleEn ?? ''}
          onChange={(e) => set({ titleEn: e.target.value })}
          data-testid="cap-source-title-en"
        />
      </FormField>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.sourceType')} required>
          <Select
            value={v.sourceTypeCode}
            onValueChange={(sourceTypeCode) => set({ sourceTypeCode })}
            options={labels.lookupOptions(lookupTypes.sourceType)}
          />
        </FormField>
        <FormField label={t('fields.receivedOn')} required>
          <DatePicker value={v.receivedOn} onChange={(d) => d && set({ receivedOn: d })} />
        </FormField>
        <FormField label={t('fields.unit')}>
          <UnitSelect
            allowEmpty
            value={v.unitId ?? undefined}
            onChange={(u) => set({ unitId: u && u !== '-' ? u : null })}
          />
        </FormField>
        <FormField label={t('fields.sourceOwner')}>
          <EmployeePicker
            value={v.ownerEmployeeId ?? undefined}
            onChange={(e) => set({ ownerEmployeeId: e ?? null })}
          />
        </FormField>
      </div>
      <FormField label={t('fields.description')}>
        <Textarea value={v.description ?? ''} onChange={(e) => set({ description: e.target.value })} />
      </FormField>
    </SimpleDialog>
  );
}

export function emptyRemark(): CapRemarkValues {
  return {
    textAr: '',
    textEn: '',
    criterionCode: null,
    typeCode: 'observation',
    priority: 'medium',
    departmentUnitId: '',
    sectionUnitId: null,
    ownerEmployeeId: null,
  };
}

/** Add or edit a remark: text, type, criterion, priority (sets the due date in working time), department / section, owner. */
export function RemarkDialog({
  sourceId,
  initial,
  remarkId,
  version,
  onClose,
}: {
  sourceId?: string;
  initial?: CapRemarkValues;
  remarkId?: string;
  version?: number;
  onClose: () => void;
}) {
  const { t } = useTranslation('cap');
  const { k } = useCapT();
  const labels = useCapLabels();
  const [v, setV] = useState<CapRemarkValues>(initial ?? emptyRemark());
  const create = useDialogMutation(capApi.createRemark, onClose);
  const update = useDialogMutation(capApi.updateRemark, onClose);
  const set = (patch: Partial<CapRemarkValues>) => setV((x) => ({ ...x, ...patch }));
  const values = { ...v, textAr: v.textAr || null, textEn: v.textEn || null };
  return (
    <SimpleDialog
      title={remarkId ? t('remark.edit') : t('remark.add')}
      description={t('remark.addHint')}
      onClose={onClose}
      busy={create.busy || update.busy}
      submitLabel={t('common.save')}
      disabled={!(v.textAr || v.textEn) || !v.departmentUnitId}
      testId="cap-remark-save"
      wide
      onSubmit={() =>
        remarkId
          ? update.run({ id: remarkId, values, version: version ?? 0 })
          : sourceId && create.run({ sourceId, values })
      }
    >
      <FormField label={t('fields.textAr')} required>
        <Textarea
          dir="rtl"
          value={v.textAr ?? ''}
          onChange={(e) => set({ textAr: e.target.value })}
          data-testid="cap-remark-text-ar"
        />
      </FormField>
      <FormField label={t('fields.textEn')}>
        <Textarea
          dir="ltr"
          value={v.textEn ?? ''}
          onChange={(e) => set({ textEn: e.target.value })}
          data-testid="cap-remark-text-en"
        />
      </FormField>
      <div className="grid gap-3 md:grid-cols-3">
        <FormField label={t('fields.remarkType')} required>
          <Select
            value={v.typeCode}
            onValueChange={(typeCode) => set({ typeCode })}
            options={labels.lookupOptions(lookupTypes.remarkType)}
          />
        </FormField>
        <FormField label={t('fields.criterion')}>
          <Select
            value={v.criterionCode ?? '-'}
            onValueChange={(c) => set({ criterionCode: c === '-' ? null : c })}
            options={[{ value: '-', label: '—' }, ...labels.lookupOptions(lookupTypes.criterion)]}
          />
        </FormField>
        <FormField label={t('fields.priority')} required hint={t('remark.priorityHint')}>
          <Select
            value={v.priority}
            onValueChange={(priority) => set({ priority })}
            options={priorities.map((p) => ({ value: p, label: k(`priority.${p}`) }))}
            aria-label={t('fields.priority')}
          />
        </FormField>
        <FormField label={t('fields.department')} required>
          <UnitSelect
            value={v.departmentUnitId || undefined}
            onChange={(u) => set({ departmentUnitId: u ?? '' })}
          />
        </FormField>
        <FormField label={t('fields.section')}>
          <UnitSelect
            allowEmpty
            value={v.sectionUnitId ?? undefined}
            onChange={(u) => set({ sectionUnitId: u && u !== '-' ? u : null })}
          />
        </FormField>
        <FormField label={t('fields.owner')} hint={t('remark.ownerHint')}>
          <EmployeePicker
            value={v.ownerEmployeeId ?? undefined}
            onChange={(e) => set({ ownerEmployeeId: e ?? null })}
          />
        </FormField>
      </div>
    </SimpleDialog>
  );
}

export function emptyAction(): CapActionValues {
  return {
    detailsAr: '',
    detailsEn: '',
    typeCode: null,
    budgetRequired: false,
    budgetAvailable: null,
    estimatedCost: null,
    relatedProcess: '',
    assignedEmployeeId: null,
    plannedStart: null,
    plannedEnd: null,
  };
}

/** Corrective action form with the budget questions (budget required → available? → cost; routed to a draft program request). */
export function ActionFields({
  value: v,
  onChange,
}: {
  value: CapActionValues;
  onChange: (v: CapActionValues) => void;
}) {
  const { t } = useTranslation('cap');
  const labels = useCapLabels();
  const set = (patch: Partial<CapActionValues>) => onChange({ ...v, ...patch });
  return (
    <>
      <FormField label={t('fields.detailsAr')} required>
        <Textarea
          dir="rtl"
          value={v.detailsAr ?? ''}
          onChange={(e) => set({ detailsAr: e.target.value })}
          data-testid="cap-action-details-ar"
        />
      </FormField>
      <FormField label={t('fields.detailsEn')}>
        <Textarea
          dir="ltr"
          value={v.detailsEn ?? ''}
          onChange={(e) => set({ detailsEn: e.target.value })}
          data-testid="cap-action-details-en"
        />
      </FormField>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.actionType')} hint={t('action.typeHint')}>
          <Select
            value={v.typeCode ?? '-'}
            onValueChange={(c) => set({ typeCode: c === '-' ? null : c })}
            options={[{ value: '-', label: '—' }, ...labels.lookupOptions(lookupTypes.actionType)]}
          />
        </FormField>
        <FormField label={t('fields.relatedProcess')}>
          <Input value={v.relatedProcess ?? ''} onChange={(e) => set({ relatedProcess: e.target.value })} />
        </FormField>
        <FormField label={t('fields.assignee')}>
          <EmployeePicker
            value={v.assignedEmployeeId ?? undefined}
            onChange={(e) => set({ assignedEmployeeId: e ?? null })}
          />
        </FormField>
        <div />
        <FormField label={t('fields.plannedStart')}>
          <DatePicker
            value={v.plannedStart ?? undefined}
            onChange={(d) => set({ plannedStart: d ?? null })}
          />
        </FormField>
        <FormField label={t('fields.plannedEnd')}>
          <DatePicker value={v.plannedEnd ?? undefined} onChange={(d) => set({ plannedEnd: d ?? null })} />
        </FormField>
      </div>
      <fieldset className="flex flex-col gap-3 rounded-md border p-3" data-testid="cap-action-budget">
        <legend className="px-1 text-sm font-medium">{t('budget.title')}</legend>
        <Checkbox
          label={t('budget.required')}
          checked={v.budgetRequired}
          onChange={(e) =>
            set({
              budgetRequired: e.target.checked,
              ...(e.target.checked ? {} : { budgetAvailable: null, estimatedCost: null }),
            })
          }
          data-testid="cap-action-budget-required"
        />
        {v.budgetRequired && (
          <>
            <FormField label={t('budget.available')} required>
              <Select
                value={v.budgetAvailable == null ? undefined : v.budgetAvailable ? 'yes' : 'no'}
                onValueChange={(x) => set({ budgetAvailable: x === 'yes' })}
                options={[
                  { value: 'yes', label: t('common.yes') },
                  { value: 'no', label: t('common.no') },
                ]}
                aria-label={t('budget.available')}
              />
            </FormField>
            <FormField label={t('budget.cost')}>
              <Input
                type="number"
                min={0}
                value={v.estimatedCost ?? ''}
                onChange={(e) =>
                  set({ estimatedCost: e.target.value === '' ? null : Number(e.target.value) })
                }
                data-testid="cap-action-cost"
              />
            </FormField>
            {v.budgetAvailable === false && (
              <p className="rounded bg-warning/10 p-2 text-sm" data-testid="cap-budget-routing-hint">
                {t('budget.routingHint')}
              </p>
            )}
          </>
        )}
      </fieldset>
    </>
  );
}

export function ActionDialog({
  remarkId,
  actionId,
  initial,
  version,
  onClose,
}: {
  remarkId?: string;
  actionId?: string;
  initial?: CapActionValues;
  version?: number;
  onClose: () => void;
}) {
  const { t } = useTranslation('cap');
  const [v, setV] = useState<CapActionValues>(initial ?? emptyAction());
  const create = useDialogMutation(capApi.createAction, onClose);
  const update = useDialogMutation(capApi.updateAction, onClose);
  const values: CapActionValues = {
    ...v,
    detailsAr: v.detailsAr || null,
    detailsEn: v.detailsEn || null,
    relatedProcess: v.relatedProcess || null,
  };
  return (
    <SimpleDialog
      title={actionId ? t('action.edit') : t('action.new')}
      onClose={onClose}
      busy={create.busy || update.busy}
      submitLabel={t('common.save')}
      disabled={!(v.detailsAr || v.detailsEn) || (v.budgetRequired && v.budgetAvailable == null)}
      testId="cap-action-save"
      wide
      onSubmit={() =>
        actionId
          ? update.run({ id: actionId, values, version: version ?? 0 })
          : remarkId && create.run({ remarkId, values })
      }
    >
      <ActionFields value={v} onChange={setV} />
    </SimpleDialog>
  );
}

/** Reason dialog shared by reject / cancel. */
export function ReasonDialog({
  title,
  description,
  label,
  submitLabel,
  testId,
  onClose,
  onSubmit,
  busy,
  required = true,
}: {
  title: string;
  description?: string;
  label: string;
  submitLabel: string;
  testId: string;
  onClose: () => void;
  onSubmit: (reason: string) => void;
  busy: boolean;
  required?: boolean;
}) {
  const [reason, setReason] = useState('');
  return (
    <SimpleDialog
      title={title}
      description={description}
      onClose={onClose}
      busy={busy}
      submitLabel={submitLabel}
      disabled={required && !reason.trim()}
      testId={testId}
      onSubmit={() => onSubmit(reason.trim())}
    >
      <FormField label={label} required={required}>
        <Textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          data-testid={`${testId}-reason`}
        />
      </FormField>
    </SimpleDialog>
  );
}
