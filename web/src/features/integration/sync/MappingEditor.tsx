import { Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  ScrollRegion,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  FIELD_KINDS,
  TRANSFORMS,
  useObjectMapping,
  useSourceDescribe,
  useSyncMutation,
  useSyncTargets,
  type FieldMapping,
  type MappingValidation,
  type ObjectMapping,
  type ObjectMappingInput,
  type SourceDescribe,
  type SourceField,
  type SyncTarget,
} from './api';
import { Code, isPlainObject, parseJson, severityTone, useMessage, useSyncT } from './shared';

interface EditorProps {
  connectionId: string;
  mappingId: string;
  canManage: boolean;
  onClose: () => void;
}

/** Object mapping editor (ADR 0203): live describe on one side, field mappings with value maps on the other; Save then Validate. */
export function MappingEditor({ connectionId, mappingId, canManage, onClose }: EditorProps) {
  const { t } = useSyncT();
  const { t: tc } = useTranslation();
  const mapping = useObjectMapping(mappingId);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className="max-w-[min(100rem,calc(100%-2rem))]"
        title={mapping.data ? t('sync.editor.title', { object: mapping.data.key }) : t('sync.editor.loading')}
        description={t('sync.editor.description')}
        footer={
          <DialogClose asChild>
            <Button variant="outline">{tc('actions.close')}</Button>
          </DialogClose>
        }
      >
        {mapping.data ? (
          <EditorBody
            key={mapping.data.version}
            connectionId={connectionId}
            mapping={mapping.data}
            canManage={canManage}
          />
        ) : (
          <Skeleton className="h-96 rounded-lg" />
        )}
      </DialogContent>
    </Dialog>
  );
}

interface Draft {
  targetType: string;
  loadOrder: string;
  where: string;
  watermarkField: string;
  externalIdTemplate: string;
  useBulk: boolean;
  fields: FieldMapping[];
  defaults: string;
  ignore: string;
  options: string;
  notesAr: string;
  notesEn: string;
}

function toDraft(m: ObjectMapping): Draft {
  return {
    targetType: m.targetType ?? '',
    loadOrder: String(m.loadOrder),
    where: m.where ?? '',
    watermarkField: m.watermarkField,
    externalIdTemplate: m.externalIdTemplate,
    useBulk: m.useBulk,
    fields: m.fields.map((f) => ({ ...f, values: f.values ? { ...f.values } : null })),
    defaults: JSON.stringify(m.defaults, null, 2),
    ignore: m.ignore.join('\n'),
    options: m.options === null || m.options === undefined ? '' : JSON.stringify(m.options, null, 2),
    notesAr: m.notesAr ?? '',
    notesEn: m.notesEn ?? '',
  };
}

function EditorBody({
  connectionId,
  mapping,
  canManage,
}: {
  connectionId: string;
  mapping: ObjectMapping;
  canManage: boolean;
}) {
  const { t, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [draft, setDraft] = useState<Draft>(() => toDraft(mapping));
  const [dirty, setDirty] = useState(false);
  const [jsonError, setJsonError] = useState<string | null>(null);
  const [validation, setValidation] = useState<MappingValidation | null>(null);
  const describe = useSourceDescribe(connectionId, mapping.sourceObject);
  const targets = useSyncTargets();
  const target = targets.data?.find((x) => x.code === draft.targetType);
  const update = (patch: Partial<Draft>) => {
    setDraft((d) => ({ ...d, ...patch }));
    setDirty(true);
  };
  const setField = (index: number, patch: Partial<FieldMapping>) =>
    update({ fields: draft.fields.map((f, i) => (i === index ? { ...f, ...patch } : f)) });
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const save = useSyncMutation(async (body: ObjectMappingInput) =>
    unwrap(
      await api.PUT('/api/v1/integration/sync/mappings/{mappingId}', {
        params: { path: { mappingId: mapping.id } },
        body,
      }),
    ),
  );
  const validate = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/mappings/{mappingId}/validate', {
        params: { path: { id: connectionId, mappingId: mapping.id } },
      }),
    ),
  );
  const submit = () => {
    const defaults = parseJson(draft.defaults.trim() || '{}', isPlainObject) as
      Record<string, never> | undefined;
    const options = draft.options.trim() ? parseJson(draft.options, () => true) : null;
    if (!defaults || options === undefined) {
      setJsonError(t('sync.editor.invalidJson'));
      return;
    }
    setJsonError(null);
    save.mutate(
      {
        targetType: draft.targetType || null,
        loadOrder: Number(draft.loadOrder) || 0,
        where: draft.where.trim() || null,
        watermarkField: draft.watermarkField.trim() || null,
        externalIdTemplate: draft.externalIdTemplate.trim() || null,
        useBulk: draft.useBulk,
        fields: draft.fields.map(cleanField),
        defaults,
        ignore: draft.ignore
          .split(/[\n,]/)
          .map((s) => s.trim())
          .filter(Boolean),
        options,
        notesAr: draft.notesAr.trim() || null,
        notesEn: draft.notesEn.trim() || null,
        version: mapping.version,
      },
      {
        onSuccess: () => {
          setDirty(false);
          setValidation(null);
          toast({ tone: 'success', title: t('sync.editor.saved') });
        },
        onError,
      },
    );
  };
  return (
    <div className="flex flex-col gap-4">
      <StatusBanner
        tone={mapping.confirmedAt ? 'success' : 'warning'}
        title={
          mapping.confirmedAt
            ? t('sync.editor.confirmedOn', { date: formatDateTime(mapping.confirmedAt, language) })
            : t('sync.objects.notConfirmed')
        }
        description={t('sync.editor.saveClears')}
      />
      {pickText(language, mapping.notesAr, mapping.notesEn) && (
        <p className="text-sm text-muted-foreground">
          {pickText(language, mapping.notesAr, mapping.notesEn)}
        </p>
      )}
      <div className="grid gap-4 lg:grid-cols-[minmax(16rem,1fr)_3fr]">
        <DescribePanel
          describe={describe.data}
          loading={describe.isPending}
          failed={describe.isError}
          error={describe.error}
        />
        <div className="flex min-w-0 flex-col gap-4">
          <div className="grid gap-3 md:grid-cols-3">
            <FormField label={t('sync.objects.target')}>
              <Select
                value={draft.targetType || '-'}
                disabled={!canManage}
                onValueChange={(value) => update({ targetType: value === '-' ? '' : value })}
                options={[
                  { value: '-', label: t('sync.editor.noTarget') },
                  ...(targets.data ?? []).map((x) => ({
                    value: x.code,
                    label: `${pickText(language, x.nameAr, x.nameEn)} (${x.code})`,
                  })),
                ]}
              />
            </FormField>
            <FormField label={t('sync.mappings.order')}>
              <Input
                type="number"
                name="loadOrder"
                disabled={!canManage}
                value={draft.loadOrder}
                onChange={(e) => update({ loadOrder: e.target.value })}
              />
            </FormField>
            <FormField label={t('sync.editor.watermarkField')}>
              <Input
                dir="ltr"
                name="watermarkField"
                disabled={!canManage}
                value={draft.watermarkField}
                onChange={(e) => update({ watermarkField: e.target.value })}
              />
            </FormField>
            <FormField label={t('sync.editor.externalId')} hint={t('sync.editor.externalIdHint')}>
              <Input
                dir="ltr"
                name="externalIdTemplate"
                disabled={!canManage}
                value={draft.externalIdTemplate}
                onChange={(e) => update({ externalIdTemplate: e.target.value })}
              />
            </FormField>
            <div className="md:col-span-2">
              <FormField label={t('sync.editor.where')} hint={t('sync.editor.whereHint')}>
                <Input
                  dir="ltr"
                  name="where"
                  disabled={!canManage}
                  value={draft.where}
                  onChange={(e) => update({ where: e.target.value })}
                />
              </FormField>
            </div>
          </div>
          <Checkbox
            label={t('sync.editor.useBulk')}
            disabled={!canManage}
            checked={draft.useBulk}
            onChange={(e) => update({ useBulk: e.target.checked })}
          />
          <section className="flex flex-col gap-2" aria-labelledby="mapping-fields-title">
            <div className="flex items-center justify-between gap-2">
              <h3 id="mapping-fields-title" className="font-medium">
                {t('sync.editor.fields')}
              </h3>
              {canManage && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    update({ fields: [...draft.fields, { source: null, target: '', kind: 'direct' }] })
                  }
                >
                  <Plus aria-hidden />
                  {t('sync.editor.addField')}
                </Button>
              )}
            </div>
            {draft.fields.length === 0 && (
              <p className="text-sm text-muted-foreground">{t('sync.editor.noFields')}</p>
            )}
            {draft.fields.map((field, index) => (
              <FieldRow
                key={index}
                index={index}
                field={field}
                describe={describe.data}
                target={target}
                targets={targets.data ?? []}
                canManage={canManage}
                onChange={(patch) => setField(index, patch)}
                onRemove={() => update({ fields: draft.fields.filter((_, i) => i !== index) })}
              />
            ))}
          </section>
          <div className="grid gap-3 md:grid-cols-2">
            <FormField label={t('sync.editor.defaults')} hint={t('sync.editor.defaultsHint')}>
              <Textarea
                dir="ltr"
                rows={4}
                name="defaults"
                className="font-mono text-xs"
                disabled={!canManage}
                value={draft.defaults}
                onChange={(e) => update({ defaults: e.target.value })}
              />
            </FormField>
            <FormField label={t('sync.editor.ignore')} hint={t('sync.editor.ignoreHint')}>
              <Textarea
                dir="ltr"
                rows={4}
                name="ignore"
                className="font-mono text-xs"
                disabled={!canManage}
                value={draft.ignore}
                onChange={(e) => update({ ignore: e.target.value })}
              />
            </FormField>
            <FormField label={t('sync.editor.options')} hint={t('sync.editor.optionsHint')}>
              <Textarea
                dir="ltr"
                rows={3}
                name="options"
                className="font-mono text-xs"
                disabled={!canManage}
                value={draft.options}
                onChange={(e) => update({ options: e.target.value })}
              />
            </FormField>
            <div className="flex flex-col gap-3">
              <FormField label={t('sync.editor.notesAr')}>
                <Textarea
                  dir="rtl"
                  rows={2}
                  name="notesAr"
                  disabled={!canManage}
                  value={draft.notesAr}
                  onChange={(e) => update({ notesAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('sync.editor.notesEn')}>
                <Textarea
                  dir="ltr"
                  rows={2}
                  name="notesEn"
                  disabled={!canManage}
                  value={draft.notesEn}
                  onChange={(e) => update({ notesEn: e.target.value })}
                />
              </FormField>
            </div>
          </div>
          {jsonError && <StatusBanner tone="danger" title={jsonError} />}
          {canManage && (
            <div className="flex flex-wrap items-center gap-2">
              <Button loading={save.isPending} disabled={!dirty} onClick={submit}>
                {tc('actions.save')}
              </Button>
              <Button
                variant="secondary"
                loading={validate.isPending}
                disabled={dirty}
                onClick={() => validate.mutate(undefined, { onSuccess: setValidation, onError })}
              >
                {t('sync.editor.validate')}
              </Button>
              {dirty && <span className="text-xs text-muted-foreground">{t('sync.editor.saveFirst')}</span>}
            </div>
          )}
          {validation && <ValidationResult validation={validation} />}
        </div>
      </div>
    </div>
  );
}

/** Drops empty optional properties so the stored JSON stays small. */
function cleanField(f: FieldMapping): FieldMapping {
  const values = f.values && Object.keys(f.values).length > 0 ? f.values : null;
  const usesValues = f.kind === 'picklist' || f.kind === 'recordType';
  return {
    source: f.kind === 'constant' ? null : f.source || null,
    target: f.target ?? '',
    kind: f.kind ?? 'direct',
    values: usesValues ? values : null,
    transform: f.transform || null,
    referenceType: f.kind === 'reference' ? f.referenceType || null : null,
    value: f.kind === 'constant' ? (f.value ?? '') : null,
    required: f.required ?? false,
  };
}

function DescribePanel({
  describe,
  loading,
  failed,
  error,
}: {
  describe: SourceDescribe | undefined;
  loading: boolean;
  failed: boolean;
  error: unknown;
}) {
  const { t } = useSyncT();
  const { t: tc } = useTranslation();
  const [filter, setFilter] = useState('');
  if (loading) return <Skeleton className="h-96 rounded-lg" />;
  if (failed || !describe)
    return (
      <StatusBanner
        tone="warning"
        title={t('sync.editor.describeFailed')}
        description={errorMessage(tc, error)}
      />
    );
  const needle = filter.trim().toLowerCase();
  const fields = describe.fields.filter(
    (f) => !needle || f.name.toLowerCase().includes(needle) || f.label.toLowerCase().includes(needle),
  );
  return (
    <section
      className="flex min-w-0 flex-col gap-2 rounded-lg border p-3"
      aria-labelledby="describe-title"
      data-testid="describe-panel"
    >
      <h3 id="describe-title" className="font-medium">
        {t('sync.editor.describe', { object: describe.label })}
      </h3>
      <Input
        name="describeFilter"
        aria-label={t('sync.editor.filterFields')}
        placeholder={t('sync.editor.filterFields')}
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
      />
      <ScrollRegion
        className="max-h-[32rem] overflow-auto"
        label={t('sync.editor.describe', { object: describe.label })}
      >
        <ul className="flex flex-col divide-y text-sm">
          {fields.map((f) => (
            <li key={f.name} className="flex flex-col gap-0.5 py-1.5">
              <span className="flex flex-wrap items-center gap-2">
                <Code>{f.name}</Code>
                <StatusChip tone="neutral">{f.type}</StatusChip>
              </span>
              <span className="text-xs text-muted-foreground">{f.label}</span>
              {f.picklistValues.length > 0 && (
                <span className="text-xs text-muted-foreground" dir="ltr">
                  {f.picklistValues
                    .filter((v) => v.active)
                    .map((v) => v.value)
                    .join(' · ')}
                </span>
              )}
            </li>
          ))}
        </ul>
      </ScrollRegion>
    </section>
  );
}

interface FieldRowProps {
  index: number;
  field: FieldMapping;
  describe: SourceDescribe | undefined;
  target: SyncTarget | undefined;
  targets: SyncTarget[];
  canManage: boolean;
  onChange: (patch: Partial<FieldMapping>) => void;
  onRemove: () => void;
}

function FieldRow({ index, field, describe, target, targets, canManage, onChange, onRemove }: FieldRowProps) {
  const { t, tx } = useSyncT();
  const kind = field.kind ?? 'direct';
  const source = describe?.fields.find((f) => f.name.toLowerCase() === (field.source ?? '').toLowerCase());
  const sourceOptions = [
    { value: '-', label: t('sync.editor.chooseField') },
    ...(describe?.fields ?? []).map((f) => ({
      value: f.name,
      label: f.label && f.label !== f.name ? `${f.name} — ${f.label}` : f.name,
    })),
  ];
  if (field.source && !source) sourceOptions.push({ value: field.source, label: field.source });
  const targetOptions = [
    { value: '-', label: t('sync.editor.chooseField') },
    ...(target?.fields ?? []).map((f) => ({
      value: f.code,
      label: `${f.code} (${tx(`sync.targetKinds.${f.kind}`, { defaultValue: f.kind })}${f.required ? ' *' : ''})`,
    })),
  ];
  if (field.target && !target?.fields.some((f) => f.code === field.target))
    targetOptions.push({ value: field.target, label: field.target });
  const [transform = '', truncateAt = ''] = (field.transform ?? '').split(':');
  const label = (name: string) => `${tx(`sync.editor.${name}`)} ${index + 1}`;
  return (
    <div className="flex flex-col gap-3 rounded-md border p-3" data-testid={`field-row-${index}`}>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <FormField label={t('sync.editor.kind')}>
          <Select
            aria-label={label('kind')}
            value={kind}
            disabled={!canManage}
            onValueChange={(value) => onChange({ kind: value })}
            options={FIELD_KINDS.map((k) => ({ value: k, label: tx(`sync.fieldKinds.${k}`) }))}
          />
        </FormField>
        {kind === 'constant' ? (
          <FormField label={t('sync.editor.value')}>
            <Input
              dir="ltr"
              name={`fields.${index}.value`}
              disabled={!canManage}
              value={field.value ?? ''}
              onChange={(e) => onChange({ value: e.target.value })}
            />
          </FormField>
        ) : (
          <FormField label={t('sync.editor.source')}>
            <Select
              aria-label={label('source')}
              value={field.source || '-'}
              disabled={!canManage}
              onValueChange={(value) => onChange({ source: value === '-' ? null : value })}
              options={sourceOptions}
            />
          </FormField>
        )}
        <FormField label={t('sync.editor.target')}>
          <Select
            aria-label={label('target')}
            value={field.target || '-'}
            disabled={!canManage}
            onValueChange={(value) => onChange({ target: value === '-' ? '' : value })}
            options={targetOptions}
          />
        </FormField>
        <FormField label={t('sync.editor.transform')}>
          <Select
            aria-label={label('transform')}
            value={transform || '-'}
            disabled={!canManage}
            onValueChange={(value) =>
              onChange({ transform: value === '-' ? null : value === 'truncate' ? 'truncate:255' : value })
            }
            options={[
              { value: '-', label: t('sync.editor.noTransform') },
              ...TRANSFORMS.map((x) => ({ value: x, label: tx(`sync.transforms.${x}`) })),
            ]}
          />
        </FormField>
        {transform === 'truncate' && (
          <FormField label={t('sync.editor.truncateAt')}>
            <Input
              type="number"
              min={1}
              name={`fields.${index}.truncate`}
              disabled={!canManage}
              value={truncateAt}
              onChange={(e) => onChange({ transform: `truncate:${e.target.value}` })}
            />
          </FormField>
        )}
        {kind === 'reference' && (
          <FormField label={t('sync.editor.referenceType')}>
            <Select
              aria-label={label('referenceType')}
              value={field.referenceType || '-'}
              disabled={!canManage}
              onValueChange={(value) => onChange({ referenceType: value === '-' ? null : value })}
              options={[
                { value: '-', label: t('sync.editor.chooseField') },
                ...targets.map((x) => ({ value: x.code, label: x.code })),
              ]}
            />
          </FormField>
        )}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Checkbox
          label={t('sync.editor.required')}
          disabled={!canManage}
          checked={field.required ?? false}
          onChange={(e) => onChange({ required: e.target.checked })}
        />
        {canManage && (
          <Button variant="ghost" size="sm" onClick={onRemove}>
            <Trash2 aria-hidden />
            {t('sync.editor.removeField')}
          </Button>
        )}
      </div>
      {(kind === 'picklist' || kind === 'recordType') && (
        <ValueMapEditor
          values={field.values ?? {}}
          source={source}
          canManage={canManage}
          onChange={(values) => onChange({ values })}
        />
      )}
    </div>
  );
}

/** Source value → code. The describe's active picklist values are listed even when not mapped yet; `*` is the fallback. */
function ValueMapEditor({
  values,
  source,
  canManage,
  onChange,
}: {
  values: Record<string, string>;
  source: SourceField | undefined;
  canManage: boolean;
  onChange: (values: Record<string, string>) => void;
}) {
  const { t } = useSyncT();
  const [extra, setExtra] = useState('');
  const known = (source?.picklistValues ?? []).filter((v) => v.active).map((v) => v.value);
  const keys = [...new Set([...known, ...Object.keys(values).filter((k) => k !== '*'), '*'])];
  const set = (key: string, code: string) => {
    const trimmed = code.trim();
    onChange(
      Object.fromEntries([
        ...Object.entries(values).filter(([k]) => k !== key),
        ...(trimmed ? [[key, trimmed] as const] : []),
      ]),
    );
  };
  return (
    <fieldset className="flex flex-col gap-2 rounded-md bg-muted/40 p-3">
      <legend className="text-sm font-medium">{t('sync.editor.valueMap')}</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {keys.map((key) => (
          <label key={key} className="flex items-center gap-2 text-sm">
            <span className="w-1/2 truncate" dir="ltr" title={key}>
              {key === '*' ? t('sync.editor.fallback') : key}
              {!known.includes(key) && key !== '*' && source && (
                <span className="ms-1 text-xs text-warning">({t('sync.editor.notInSource')})</span>
              )}
            </span>
            <Input
              dir="ltr"
              className="w-1/2"
              aria-label={t('sync.editor.codeFor', { value: key })}
              disabled={!canManage}
              value={values[key] ?? ''}
              onChange={(e) => set(key, e.target.value)}
            />
          </label>
        ))}
      </div>
      {canManage && (
        <div className="flex items-center gap-2">
          <Input
            dir="ltr"
            className="max-w-xs"
            aria-label={t('sync.editor.newValue')}
            placeholder={t('sync.editor.newValue')}
            value={extra}
            onChange={(e) => setExtra(e.target.value)}
          />
          <Button
            variant="outline"
            size="sm"
            disabled={!extra.trim() || keys.includes(extra.trim())}
            onClick={() => {
              onChange({ ...values, [extra.trim()]: '' });
              setExtra('');
            }}
          >
            {t('sync.editor.addValue')}
          </Button>
        </div>
      )}
    </fieldset>
  );
}

function ValidationResult({ validation }: { validation: MappingValidation }) {
  const { t, tx } = useSyncT();
  const message = useMessage();
  return (
    <section className="flex flex-col gap-2" aria-live="polite" data-testid="validation-result">
      <StatusBanner
        tone={validation.ok ? 'success' : 'danger'}
        title={validation.ok ? t('sync.editor.valid') : t('sync.editor.invalid')}
        description={validation.ok ? t('sync.editor.validHint') : undefined}
      />
      {validation.issues.length > 0 && (
        <ul className="flex flex-col gap-1 text-sm">
          {validation.issues.map((issue, i) => (
            <li key={i} className="flex flex-wrap items-start gap-2">
              <StatusChip tone={severityTone[issue.severity] ?? 'neutral'}>
                {tx(`sync.severity.${issue.severity}`, { defaultValue: issue.severity })}
              </StatusChip>
              {issue.field && <Code>{issue.field}</Code>}
              <span className="min-w-0 flex-1">{message(issue.messageKey, issue.params)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
