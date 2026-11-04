import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { ArrowDown, ArrowLeft, ArrowUp, Eye, Lock, Plus, Save, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { pickText } from '@/lib/format';
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import {
  previewTypePdf,
  useRequestType,
  useSaveType,
  useTypeOptions,
  type FieldCondition,
  type FormFieldDef,
  type FormSchema,
  type FormSection,
  type FormValues,
  type PdfTemplate,
  type SimpleStages,
  type StageDefinition,
  type SubmitterRule,
  type TypeDetail,
  type TypeInput,
  type TypeOptions,
} from './api';
import { typeIcons, useDynamicT } from './components';
import { FormRenderer } from './FormRenderer';
import { columnTypes, conditionOps, type FieldMessage } from './formLogic';

const route = getRouteApi('/app/settings/request-types/$code');

const builderTabs = ['general', 'form', 'preview', 'stages', 'templates'] as const;

interface BuilderState {
  code: string;
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  category: string | null;
  icon: string;
  numberPrefix: string;
  owningUnitId: string | null;
  submitters: SubmitterRule;
  active: boolean;
  showInCatalogue: boolean;
  embeddable: boolean;
  sort: number;
  form: FormSchema;
  simple: SimpleStages;
  builtInStages: StageDefinition[];
  templates: PdfTemplate[];
}

/** Schemas travel without the server's computed members (allFields, isEmpty). */
function cleanSchema(schema: FormSchema | null | undefined): FormSchema {
  return {
    sections: (schema?.sections ?? []).map((section) => ({
      code: section.code ?? '',
      titleAr: section.titleAr ?? null,
      titleEn: section.titleEn ?? null,
      system: section.system ?? false,
      fields: (section.fields ?? []).map(cleanField),
    })),
  };
}

function cleanField(field: FormFieldDef): FormFieldDef {
  return { ...field, columns: field.columns ? field.columns.map(cleanField) : field.columns };
}

const defaultSimple: SimpleStages = {
  approval: true,
  approvalNameAr: 'المراجعة',
  approvalNameEn: 'Review',
  processing: false,
  processingNameAr: 'المعالجة',
  processingNameEn: 'Processing',
  processorRoleKeys: [],
  slaHours: 24,
  completionForm: { sections: [] },
  allowReturn: true,
  allowReject: true,
};

function stateOf(detail: TypeDetail | undefined): BuilderState {
  if (!detail)
    return {
      code: '',
      nameAr: '',
      nameEn: '',
      descriptionAr: '',
      descriptionEn: '',
      category: null,
      icon: 'file-text',
      numberPrefix: '',
      owningUnitId: null,
      submitters: { everyone: true, roleKeys: [], unitIds: [] },
      active: true,
      showInCatalogue: true,
      embeddable: false,
      sort: 100,
      form: { sections: [{ code: 'main', titleAr: 'البيانات', titleEn: 'Details', fields: [] }] },
      simple: defaultSimple,
      builtInStages: [],
      templates: [],
    };
  return {
    code: detail.code,
    nameAr: detail.nameAr ?? '',
    nameEn: detail.nameEn ?? '',
    descriptionAr: detail.descriptionAr ?? '',
    descriptionEn: detail.descriptionEn ?? '',
    category: detail.category,
    icon: detail.icon,
    numberPrefix: detail.numberPrefix,
    owningUnitId: detail.owningUnitId,
    submitters: detail.submitters,
    active: detail.active,
    showInCatalogue: detail.showInCatalogue,
    embeddable: detail.embeddable,
    sort: detail.sort,
    form: cleanSchema(detail.form),
    simple: detail.simple
      ? { ...detail.simple, completionForm: cleanSchema(detail.simple.completionForm) }
      : defaultSimple,
    builtInStages: detail.stages,
    templates: detail.templates,
  };
}

function toInput(state: BuilderState, builtIn: boolean, creating: boolean, version: number): TypeInput {
  const text = (value: string) => value.trim() || null;
  return {
    code: creating ? state.code.trim().toLowerCase() : null,
    nameAr: text(state.nameAr),
    nameEn: text(state.nameEn),
    descriptionAr: text(state.descriptionAr),
    descriptionEn: text(state.descriptionEn),
    category: state.category,
    icon: state.icon,
    numberPrefix: text(state.numberPrefix),
    owningUnitId: state.owningUnitId,
    submitters: state.submitters,
    form: cleanSchema(state.form),
    stages: builtIn ? null : { ...state.simple, completionForm: cleanSchema(state.simple.completionForm) },
    builtInStages: builtIn ? state.builtInStages : null,
    templates: state.templates,
    active: state.active,
    showInCatalogue: state.showInCatalogue,
    embeddable: state.embeddable,
    sort: state.sort,
    version,
  };
}

type Errors = Record<string, FieldMessage>;

function errorsOf(error: unknown): Errors {
  if (!(error instanceof ApiError) || !error.problem.errors?.length) return {};
  return Object.fromEntries(
    error.problem.errors.map((e) => [e.field, { messageKey: e.messageKey, params: e.params ?? null }]),
  );
}

/** Messages at or under a path (`form.sections[0].fields[2]` → its code, type, label… errors). */
function messagesUnder(errors: Errors, path: string): FieldMessage[] {
  return Object.entries(errors)
    .filter(([key]) => key === path || key.startsWith(`${path}.`) || key.startsWith(`${path}[`))
    .map(([, m]) => m);
}

/** Settings → Request types → one type: the form builder (docs/design/requests.md §2.1–2.8). `new` creates a custom type. */
export function RequestTypeBuilderPage() {
  const { code } = route.useParams();
  const { t: tc } = useTranslation();
  const creating = code === 'new';
  const detail = useRequestType(creating ? undefined : code);
  const options = useTypeOptions();
  if ((!creating && detail.isPending) || options.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (!creating && detail.isError)
    return <StatusBanner tone="danger" title={errorMessage(tc, detail.error)} />;
  if (options.isError) return <StatusBanner tone="danger" title={errorMessage(tc, options.error)} />;
  return (
    <Builder
      key={`${code}:${detail.data?.version ?? 0}`}
      detail={creating ? undefined : detail.data}
      options={options.data}
    />
  );
}

function Builder({ detail, options }: { detail: TypeDetail | undefined; options: TypeOptions }) {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const save = useSaveType(detail?.code);
  const [state, setState] = useState<BuilderState>(() => stateOf(detail));
  const [errors, setErrors] = useState<Errors>({});
  const [tab, setTab] = useState<(typeof builderTabs)[number]>('general');
  const creating = !detail;
  const builtIn = detail?.kind === 'builtin';
  const set = (patch: Partial<BuilderState>) => setState((s) => ({ ...s, ...patch }));
  const lang = i18n.language;
  const err = (path: string) => {
    const m = errors[path];
    return m ? translateMessageKey(tc, m.messageKey, m.params) : undefined;
  };

  const onSave = () =>
    save.mutate(toInput(state, builtIn, creating, detail?.version ?? 0), {
      onSuccess: (saved) => {
        setErrors({});
        toast({ title: t('builder.saved') });
        if (creating) void navigate({ to: '/settings/request-types/$code', params: { code: saved.code } });
      },
      onError: (failure) => {
        setErrors(errorsOf(failure));
        toast({ tone: 'error', title: errorMessage(tc, failure) });
      },
    });

  const errorCount = Object.keys(errors).length;

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/settings/request-types"
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('types.title')}
      </Link>
      <PageHeader
        title={
          creating ? (
            t('builder.newTitle')
          ) : (
            <span dir="auto">{pickText(lang, detail.nameAr, detail.nameEn) || detail.code}</span>
          )
        }
        description={detail && <code dir="ltr">{detail.code}</code>}
        meta={
          detail && (
            <StatusChip tone={builtIn ? 'info' : 'neutral'}>
              {builtIn ? t('types.builtin') : t('types.custom')}
            </StatusChip>
          )
        }
        actions={
          <Button onClick={onSave} loading={save.isPending} data-testid="builder-save">
            <Save aria-hidden />
            {tc('actions.save')}
          </Button>
        }
      />
      {errorCount > 0 && (
        <StatusBanner
          tone="danger"
          title={t('builder.errors', { count: errorCount })}
          description={
            <ul className="list-disc ps-5" data-testid="builder-errors">
              {Object.entries(errors).map(([path, m]) => (
                <li key={path}>
                  <code dir="ltr" className="text-xs">
                    {path}
                  </code>
                  : {translateMessageKey(tc, m.messageKey, m.params)}
                </li>
              ))}
            </ul>
          }
        />
      )}
      <Tabs value={tab} onValueChange={(value) => setTab(value as (typeof builderTabs)[number])}>
        <TabsList className="flex-wrap">
          {builderTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`builder-tab-${key}`}>
              {t(`builder.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="general">
          <General state={state} set={set} options={options} creating={creating} err={err} />
        </TabsContent>
        <TabsContent value="form">
          <SchemaEditor
            schema={state.form}
            onChange={(form) => set({ form })}
            options={options}
            errors={errors}
            path="form"
          />
        </TabsContent>
        <TabsContent value="preview">
          <Preview schema={state.form} />
        </TabsContent>
        <TabsContent value="stages">
          {builtIn ? (
            <BuiltInStages
              stages={state.builtInStages}
              onChange={(builtInStages) => set({ builtInStages })}
              options={options}
            />
          ) : (
            <SimpleStagesEditor
              value={state.simple}
              onChange={(simple) => set({ simple })}
              options={options}
              errors={errors}
            />
          )}
        </TabsContent>
        <TabsContent value="templates">
          <Templates
            templates={state.templates}
            onChange={(templates) => set({ templates })}
            typeCode={detail?.code}
            errors={errors}
          />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Card({ title, children, actions }: { title?: ReactNode; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-4 sm:p-6">
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-2">
          {title && <h2 className="font-semibold">{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

function optionLabel(
  language: string,
  option: { code: string; labelAr: string | null; labelEn: string | null },
) {
  return pickText(language, option.labelAr, option.labelEn) || option.code;
}

function General({
  state,
  set,
  options,
  creating,
  err,
}: {
  state: BuilderState;
  set: (patch: Partial<BuilderState>) => void;
  options: TypeOptions;
  creating: boolean;
  err: (path: string) => string | undefined;
}) {
  const { t, i18n } = useTranslation('requests');
  const lang = i18n.language;
  const roles = state.submitters.roleKeys ?? [];
  return (
    <div className="flex flex-col gap-4">
      <Card title={t('builder.general.identity')}>
        <div className="grid gap-4 md:grid-cols-2">
          <FormField
            label={t('builder.general.code')}
            required
            hint={t('builder.general.codeHint')}
            error={err('code')}
          >
            <Input
              dir="ltr"
              value={state.code}
              disabled={!creating}
              onChange={(e) => set({ code: e.target.value })}
              data-testid="builder-code"
            />
          </FormField>
          <FormField
            label={t('builder.general.prefix')}
            hint={t('builder.general.prefixHint')}
            error={err('numberPrefix')}
          >
            <Input
              dir="ltr"
              value={state.numberPrefix}
              onChange={(e) => set({ numberPrefix: e.target.value.toUpperCase() })}
              data-testid="builder-prefix"
            />
          </FormField>
          <FormField label={t('builder.general.nameAr')} error={err('nameAr')}>
            <Input
              dir="rtl"
              value={state.nameAr}
              onChange={(e) => set({ nameAr: e.target.value })}
              data-testid="builder-name-ar"
            />
          </FormField>
          <FormField label={t('builder.general.nameEn')} error={err('nameEn')}>
            <Input
              dir="ltr"
              value={state.nameEn}
              onChange={(e) => set({ nameEn: e.target.value })}
              data-testid="builder-name-en"
            />
          </FormField>
          <FormField label={t('builder.general.descriptionAr')}>
            <Textarea
              dir="rtl"
              value={state.descriptionAr}
              onChange={(e) => set({ descriptionAr: e.target.value })}
            />
          </FormField>
          <FormField label={t('builder.general.descriptionEn')}>
            <Textarea
              dir="ltr"
              value={state.descriptionEn}
              onChange={(e) => set({ descriptionEn: e.target.value })}
            />
          </FormField>
          <FormField label={t('builder.general.category')}>
            <Select
              value={state.category ?? '-'}
              onValueChange={(value) => set({ category: value === '-' ? null : value })}
              options={[
                { value: '-', label: t('form.none') },
                ...options.categories.map((c) => ({ value: c.code, label: optionLabel(lang, c) })),
              ]}
            />
          </FormField>
          <FormField label={t('builder.general.icon')}>
            <Select
              value={state.icon in typeIcons ? state.icon : 'file-text'}
              onValueChange={(icon) => set({ icon })}
              options={Object.keys(typeIcons).map((name) => ({ value: name, label: name }))}
            />
          </FormField>
          <FormField label={t('builder.general.owningUnit')} hint={t('builder.general.owningUnitHint')}>
            <UnitSelect
              allowEmpty
              value={state.owningUnitId ?? undefined}
              onChange={(id) => set({ owningUnitId: id ?? null })}
            />
          </FormField>
          <FormField label={t('builder.general.sort')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={String(state.sort)}
              onChange={(e) => set({ sort: Number(e.target.value.replace(/\D/gu, '')) || 0 })}
            />
          </FormField>
        </div>
      </Card>
      <Card title={t('builder.general.availability')}>
        <Switch
          checked={state.active}
          onCheckedChange={(active) => set({ active })}
          label={t('builder.general.active')}
        />
        <Switch
          checked={state.showInCatalogue}
          onCheckedChange={(showInCatalogue) => set({ showInCatalogue })}
          label={t('builder.general.showInCatalogue')}
        />
        <Switch
          checked={state.embeddable}
          onCheckedChange={(embeddable) => set({ embeddable })}
          label={t('builder.general.embeddable')}
          description={t('builder.general.embeddableHint')}
        />
      </Card>
      <Card title={t('builder.general.submitters')}>
        <Switch
          checked={state.submitters.everyone}
          onCheckedChange={(everyone) => set({ submitters: { ...state.submitters, everyone } })}
          label={t('builder.general.everyone')}
        />
        {!state.submitters.everyone && (
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-medium">{t('builder.general.roles')}</legend>
            <RoleChecks
              options={options}
              selected={roles}
              onChange={(roleKeys) => set({ submitters: { ...state.submitters, roleKeys } })}
            />
          </fieldset>
        )}
      </Card>
    </div>
  );
}

function RoleChecks({
  options,
  selected,
  onChange,
}: {
  options: TypeOptions;
  selected: string[];
  onChange: (keys: string[]) => void;
}) {
  const { i18n } = useTranslation();
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {options.roles.map((role) => (
        <Checkbox
          key={role.code}
          label={optionLabel(i18n.language, role)}
          checked={selected.includes(role.code)}
          onChange={(e) =>
            onChange(e.target.checked ? [...selected, role.code] : selected.filter((k) => k !== role.code))
          }
        />
      ))}
    </div>
  );
}

function move<T>(items: T[], index: number, delta: number): T[] {
  const target = index + delta;
  if (target < 0 || target >= items.length) return items;
  const next = [...items];
  const [item] = next.splice(index, 1);
  if (item !== undefined) next.splice(target, 0, item);
  return next;
}

function uniqueCode(prefix: string, taken: string[]): string {
  let n = taken.length + 1;
  while (taken.includes(`${prefix}${n}`)) n++;
  return `${prefix}${n}`;
}

/** Sections and fields of a form (request form or completion form). */
function SchemaEditor({
  schema,
  onChange,
  options,
  errors,
  path,
  single,
}: {
  schema: FormSchema;
  onChange: (schema: FormSchema) => void;
  options: TypeOptions;
  errors: Errors;
  path: string;
  /** One section only (completion form). */
  single?: boolean;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const sections = schema.sections ?? [];
  const setSections = (next: FormSection[]) => onChange({ sections: next });
  const setSection = (index: number, section: FormSection) =>
    setSections(sections.map((s, i) => (i === index ? section : s)));
  const allCodes = sections.flatMap((s) => (s.fields ?? []).map((f) => f.code ?? ''));
  const addSection = () =>
    setSections([
      ...sections,
      {
        code: uniqueCode(
          'section',
          sections.map((s) => s.code ?? ''),
        ),
        titleAr: '',
        titleEn: '',
        fields: [],
      },
    ]);
  if (sections.length === 0 && single)
    return (
      <Button variant="outline" size="sm" onClick={() => setSections([{ code: 'main', fields: [] }])}>
        <Plus aria-hidden />
        {t('builder.form.addField')}
      </Button>
    );
  return (
    <div className="flex flex-col gap-4">
      {sections.map((section, s) => {
        const earlier = sections.slice(0, s).flatMap((x) => x.fields ?? []);
        const sectionPath = `${path}.sections[${s}]`;
        const sectionMessages = errors[`${sectionPath}.code`];
        return (
          <Card
            key={s}
            title={
              single ? undefined : (
                <span className="flex items-center gap-2">
                  {section.system && (
                    <Lock className="size-4 text-muted-foreground" aria-label={t('builder.form.system')} />
                  )}
                  {t('builder.form.section', { number: s + 1 })}
                </span>
              )
            }
            actions={
              !single && (
                <span className="flex gap-1">
                  <IconButton
                    label={t('builder.moveUp')}
                    disabled={s === 0}
                    onClick={() => setSections(move(sections, s, -1))}
                  >
                    <ArrowUp aria-hidden />
                  </IconButton>
                  <IconButton
                    label={t('builder.moveDown')}
                    disabled={s === sections.length - 1}
                    onClick={() => setSections(move(sections, s, 1))}
                  >
                    <ArrowDown aria-hidden />
                  </IconButton>
                  <IconButton
                    label={t('builder.form.removeSection')}
                    disabled={section.system === true || (section.fields ?? []).some((f) => f.system)}
                    onClick={() => setSections(sections.filter((_, i) => i !== s))}
                  >
                    <Trash2 aria-hidden />
                  </IconButton>
                </span>
              )
            }
          >
            {!single && (
              <div className="grid gap-3 md:grid-cols-3">
                <FormField
                  label={t('builder.form.sectionCode')}
                  error={
                    sectionMessages
                      ? translateMessageKey(tc, sectionMessages.messageKey, sectionMessages.params)
                      : undefined
                  }
                >
                  <Input
                    dir="ltr"
                    value={section.code ?? ''}
                    disabled={section.system === true}
                    onChange={(e) => setSection(s, { ...section, code: e.target.value })}
                  />
                </FormField>
                <FormField label={t('builder.form.titleAr')}>
                  <Input
                    dir="rtl"
                    value={section.titleAr ?? ''}
                    onChange={(e) => setSection(s, { ...section, titleAr: e.target.value })}
                  />
                </FormField>
                <FormField label={t('builder.form.titleEn')}>
                  <Input
                    dir="ltr"
                    value={section.titleEn ?? ''}
                    onChange={(e) => setSection(s, { ...section, titleEn: e.target.value })}
                  />
                </FormField>
              </div>
            )}
            <FieldList
              fields={section.fields ?? []}
              onChange={(fields) => setSection(s, { ...section, fields })}
              options={options}
              errors={errors}
              path={`${sectionPath}.fields`}
              earlier={earlier}
              takenCodes={allCodes}
              testId="builder-add-field"
            />
          </Card>
        );
      })}
      {!single && (
        <div>
          <Button variant="outline" onClick={addSection} data-testid="builder-add-section">
            <Plus aria-hidden />
            {t('builder.form.addSection')}
          </Button>
        </div>
      )}
    </div>
  );
}

function IconButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <Button
      type="button"
      size="icon"
      variant="ghost"
      className="size-8"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </Button>
  );
}

function FieldList({
  fields,
  onChange,
  options,
  errors,
  path,
  earlier,
  takenCodes,
  inTable,
  testId,
}: {
  fields: FormFieldDef[];
  onChange: (fields: FormFieldDef[]) => void;
  options: TypeOptions;
  errors: Errors;
  path: string;
  earlier: FormFieldDef[];
  takenCodes: string[];
  inTable?: boolean;
  testId: string;
}) {
  const { t } = useTranslation('requests');
  const [open, setOpen] = useState<number | null>(null);
  const add = () => {
    const code = uniqueCode(inTable ? 'column' : 'field', [
      ...takenCodes,
      ...fields.map((f) => f.code ?? ''),
    ]);
    onChange([...fields, { code, type: 'text', labelAr: '', labelEn: '', required: false }]);
    setOpen(fields.length);
  };
  return (
    <div className="flex flex-col gap-2">
      {fields.length === 0 && <p className="text-sm text-muted-foreground">{t('builder.form.noFields')}</p>}
      {fields.map((field, f) => (
        <FieldEditor
          key={f}
          field={field}
          open={open === f}
          onToggle={() => setOpen(open === f ? null : f)}
          onChange={(next) => onChange(fields.map((x, i) => (i === f ? next : x)))}
          onRemove={() => onChange(fields.filter((_, i) => i !== f))}
          onMove={(delta) => onChange(move(fields, f, delta))}
          first={f === 0}
          last={f === fields.length - 1}
          options={options}
          errors={errors}
          path={`${path}[${f}]`}
          earlier={[...earlier, ...fields.slice(0, f)]}
          inTable={inTable}
        />
      ))}
      <div>
        <Button type="button" size="sm" variant="outline" onClick={add} data-testid={testId}>
          <Plus aria-hidden />
          {inTable ? t('builder.form.addColumn') : t('builder.form.addField')}
        </Button>
      </div>
    </div>
  );
}

function numberOrNull(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}

function FieldEditor({
  field,
  open,
  onToggle,
  onChange,
  onRemove,
  onMove,
  first,
  last,
  options,
  errors,
  path,
  earlier,
  inTable,
}: {
  field: FormFieldDef;
  open: boolean;
  onToggle: () => void;
  onChange: (field: FormFieldDef) => void;
  onRemove: () => void;
  onMove: (delta: number) => void;
  first: boolean;
  last: boolean;
  options: TypeOptions;
  errors: Errors;
  path: string;
  earlier: FormFieldDef[];
  inTable?: boolean | undefined;
}) {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const dt = useDynamicT();
  const lang = i18n.language;
  const locked = field.system === true;
  const set = (patch: Partial<FormFieldDef>) => onChange({ ...field, ...patch });
  const messages = messagesUnder(errors, path);
  const types = inTable ? [...columnTypes] : options.fieldTypes;
  const err = (key: string) => {
    const m = errors[`${path}.${key}`];
    return m ? translateMessageKey(tc, m.messageKey, m.params) : undefined;
  };
  const label = pickText(lang, field.labelAr, field.labelEn) || field.code || '';
  const num = (key: 'maxLength' | 'min' | 'max' | 'decimals' | 'minRows' | 'maxRows', labelKey: string) => (
    <FormField label={dt(labelKey)}>
      <Input
        dir="ltr"
        inputMode="decimal"
        value={field[key] === null || field[key] === undefined ? '' : String(field[key])}
        onChange={(e) => set({ [key]: numberOrNull(e.target.value) })}
      />
    </FormField>
  );
  return (
    <div
      className={`rounded-md border ${messages.length > 0 ? 'border-danger' : ''}`}
      data-testid={`builder-field-${field.code ?? ''}`}
    >
      <div className="flex flex-wrap items-center gap-2 p-2">
        <button
          type="button"
          className="flex min-w-0 flex-1 items-center gap-2 text-start text-sm"
          aria-expanded={open}
          onClick={onToggle}
        >
          {locked && (
            <Lock className="size-4 shrink-0 text-muted-foreground" aria-label={t('builder.form.system')} />
          )}
          <span className="truncate font-medium" dir="auto">
            {label || t('builder.form.untitled')}
          </span>
          <StatusChip tone="neutral">{dt(`fieldTypes.${field.type ?? 'text'}`, field.type)}</StatusChip>
          {field.required && <StatusChip tone="warning">{t('builder.form.required')}</StatusChip>}
          <code dir="ltr" className="text-xs text-muted-foreground">
            {field.code}
          </code>
        </button>
        <span className="flex gap-1">
          <IconButton label={t('builder.moveUp')} disabled={first} onClick={() => onMove(-1)}>
            <ArrowUp aria-hidden />
          </IconButton>
          <IconButton label={t('builder.moveDown')} disabled={last} onClick={() => onMove(1)}>
            <ArrowDown aria-hidden />
          </IconButton>
          <IconButton
            label={t('builder.form.removeField', { name: label })}
            disabled={locked}
            onClick={onRemove}
          >
            <Trash2 aria-hidden />
          </IconButton>
        </span>
      </div>
      {messages.length > 0 && !open && (
        <p className="px-3 pb-2 text-xs font-medium text-danger">
          {messages.map((m) => translateMessageKey(tc, m.messageKey, m.params)).join(' · ')}
        </p>
      )}
      {open && (
        <div className="flex flex-col gap-4 border-t p-3">
          <div className="grid gap-3 md:grid-cols-2">
            <FormField label={t('builder.form.code')} required error={err('code')}>
              <Input
                dir="ltr"
                value={field.code ?? ''}
                disabled={locked}
                onChange={(e) => set({ code: e.target.value })}
              />
            </FormField>
            <FormField label={t('builder.form.type')} error={err('type')}>
              <Select
                value={field.type ?? 'text'}
                disabled={locked}
                onValueChange={(type) => set({ type })}
                options={types.map((type) => ({ value: type, label: dt(`fieldTypes.${type}`, type) }))}
              />
            </FormField>
            <FormField label={t('builder.form.labelAr')} error={err('labelAr')}>
              <Input
                dir="rtl"
                value={field.labelAr ?? ''}
                onChange={(e) => set({ labelAr: e.target.value })}
              />
            </FormField>
            <FormField label={t('builder.form.labelEn')} error={err('labelEn')}>
              <Input
                dir="ltr"
                value={field.labelEn ?? ''}
                onChange={(e) => set({ labelEn: e.target.value })}
              />
            </FormField>
            <FormField label={t('builder.form.helpAr')}>
              <Input dir="rtl" value={field.helpAr ?? ''} onChange={(e) => set({ helpAr: e.target.value })} />
            </FormField>
            <FormField label={t('builder.form.helpEn')}>
              <Input dir="ltr" value={field.helpEn ?? ''} onChange={(e) => set({ helpEn: e.target.value })} />
            </FormField>
          </div>
          <Checkbox
            label={t('builder.form.required')}
            checked={field.required === true}
            disabled={locked}
            onChange={(e) => set({ required: e.target.checked })}
          />
          <div className="grid gap-3 md:grid-cols-3">
            {(field.type === 'text' || field.type === 'textarea' || field.type === 'richtext') &&
              num('maxLength', 'builder.form.maxLength')}
            {(field.type === 'number' || field.type === 'currency') && (
              <>
                {num('min', 'builder.form.min')}
                {num('max', 'builder.form.max')}
                {num('decimals', 'builder.form.decimals')}
              </>
            )}
            {(field.type === 'picklist' || field.type === 'multipicklist') && (
              <FormField label={t('builder.form.lookupType')} required error={err('lookupType')}>
                <Select
                  value={field.lookupType ?? undefined}
                  onValueChange={(lookupType) => set({ lookupType })}
                  placeholder={t('builder.form.choose')}
                  options={options.lookupTypes.map((o) => ({
                    value: o.code,
                    label: `${optionLabel(lang, o)} (${o.code})`,
                  }))}
                />
              </FormField>
            )}
            {field.type === 'record' && (
              <FormField label={t('builder.form.targetType')} required error={err('targetType')}>
                <Select
                  value={field.targetType ?? undefined}
                  disabled={locked}
                  onValueChange={(targetType) => set({ targetType })}
                  placeholder={t('builder.form.choose')}
                  options={options.recordTypes.map((o) => ({ value: o.code, label: optionLabel(lang, o) }))}
                />
              </FormField>
            )}
            {field.type === 'secret' && (
              <FormField label={t('builder.form.format')} error={err('format')}>
                <Select
                  value={field.format ?? '-'}
                  onValueChange={(format) => set({ format: format === '-' ? null : format })}
                  options={[
                    { value: '-', label: t('builder.form.formatNone') },
                    { value: 'emiratesId', label: t('builder.form.formatEmiratesId') },
                  ]}
                />
              </FormField>
            )}
            {field.type === 'text' && !inTable && (
              <FormField label={t('builder.form.titleOf')} hint={t('builder.form.titleOfHint')}>
                <Select
                  value={field.titleOf ?? '-'}
                  onValueChange={(titleOf) => set({ titleOf: titleOf === '-' ? null : titleOf })}
                  options={[
                    { value: '-', label: t('form.none') },
                    { value: 'ar', label: t('builder.form.titleOfAr') },
                    { value: 'en', label: t('builder.form.titleOfEn') },
                  ]}
                />
              </FormField>
            )}
            {field.type === 'table' && (
              <>
                {num('minRows', 'builder.form.minRows')}
                {num('maxRows', 'builder.form.maxRows')}
              </>
            )}
          </div>
          {field.type === 'file' && (
            <Checkbox
              label={t('builder.form.multiple')}
              checked={field.multiple === true}
              onChange={(e) => set({ multiple: e.target.checked })}
            />
          )}
          {field.type === 'unit' && !inTable && (
            <Checkbox
              label={t('builder.form.requestUnit')}
              description={t('builder.form.requestUnitHint')}
              checked={field.requestUnit === true}
              onChange={(e) => set({ requestUnit: e.target.checked })}
            />
          )}
          {field.type === 'table' && (
            <fieldset className="flex flex-col gap-2 rounded-md border p-3">
              <legend className="px-1 text-sm font-medium">{t('builder.form.columns')}</legend>
              {err('columns') && <p className="text-xs font-medium text-danger">{err('columns')}</p>}
              <FieldList
                fields={field.columns ?? []}
                onChange={(columns) => set({ columns })}
                options={options}
                errors={errors}
                path={`${path}.columns`}
                earlier={[]}
                takenCodes={[]}
                inTable
                testId={`builder-add-column-${field.code ?? ''}`}
              />
            </fieldset>
          )}
          <ConditionEditor
            label={t('builder.form.visibleWhen')}
            value={field.visibleWhen ?? null}
            onChange={(visibleWhen) => set({ visibleWhen })}
            earlier={earlier}
            error={err('visibleWhen')}
          />
          <ConditionEditor
            label={t('builder.form.requiredWhen')}
            value={field.requiredWhen ?? null}
            onChange={(requiredWhen) => set({ requiredWhen })}
            earlier={earlier}
            error={err('requiredWhen')}
          />
        </div>
      )}
    </div>
  );
}

/** `visibleWhen` / `requiredWhen`: a field shown earlier in the form, an operator and a value. */
function ConditionEditor({
  label,
  value,
  onChange,
  earlier,
  error,
}: {
  label: string;
  value: FieldCondition | null;
  onChange: (condition: FieldCondition | null) => void;
  earlier: FormFieldDef[];
  error: string | undefined;
}) {
  const { t, i18n } = useTranslation('requests');
  const dt = useDynamicT();
  const candidates = earlier.filter((f) => f.code && f.type !== 'table' && f.type !== 'file');
  const source = candidates.find((f) => f.code === value?.field);
  const needsValue = value && value.op !== 'filled' && value.op !== 'empty';
  const valueText = Array.isArray(value?.value)
    ? (value.value as unknown[]).map(String).join(', ')
    : typeof value?.value === 'string' ||
        typeof value?.value === 'number' ||
        typeof value?.value === 'boolean'
      ? String(value.value)
      : '';
  return (
    <fieldset className="flex flex-col gap-2 rounded-md border p-3">
      <legend className="px-1 text-sm font-medium">{label}</legend>
      <Switch
        checked={value !== null}
        disabled={candidates.length === 0 && value === null}
        onCheckedChange={(on) =>
          onChange(on ? { field: candidates[0]?.code ?? '', op: 'eq', value: '' } : null)
        }
        label={
          candidates.length === 0 && value === null
            ? t('builder.condition.noEarlier')
            : t('builder.condition.enabled')
        }
      />
      {value && (
        <div className="grid gap-3 md:grid-cols-3">
          <FormField label={t('builder.condition.field')}>
            <Select
              value={value.field || undefined}
              onValueChange={(field) => onChange({ ...value, field })}
              options={candidates.map((f) => ({
                value: f.code ?? '',
                label: pickText(i18n.language, f.labelAr, f.labelEn) || (f.code ?? ''),
              }))}
            />
          </FormField>
          <FormField label={t('builder.condition.op')}>
            <Select
              value={value.op}
              onValueChange={(op) =>
                onChange({
                  ...value,
                  op,
                  value: op === 'in' ? [] : op === 'filled' || op === 'empty' ? null : '',
                })
              }
              options={conditionOps.map((op) => ({
                value: op,
                label: dt(`builder.condition.ops.${op}`, op),
              }))}
            />
          </FormField>
          {needsValue && (
            <FormField
              label={value.op === 'in' ? t('builder.condition.values') : t('builder.condition.value')}
            >
              {source?.type === 'checkbox' && value.op !== 'in' ? (
                <Select
                  value={value.value === true ? 'true' : 'false'}
                  onValueChange={(v) => onChange({ ...value, value: v === 'true' })}
                  options={[
                    { value: 'true', label: t('form.yes') },
                    { value: 'false', label: t('form.no') },
                  ]}
                />
              ) : (
                <Input
                  dir="auto"
                  value={valueText}
                  onChange={(e) =>
                    onChange({
                      ...value,
                      value:
                        value.op === 'in'
                          ? e.target.value
                              .split(',')
                              .map((x) => x.trim())
                              .filter(Boolean)
                          : e.target.value,
                    })
                  }
                />
              )}
            </FormField>
          )}
        </div>
      )}
      {error && <p className="text-xs font-medium text-danger">{error}</p>}
    </fieldset>
  );
}

/** The form as requesters will see it, in Arabic and English side by side (try the conditions here). */
function Preview({ schema }: { schema: FormSchema }) {
  const { t } = useTranslation('requests');
  const [values, setValues] = useState<FormValues>({});
  return (
    <div className="flex flex-col gap-3">
      <p className="flex items-center gap-2 text-sm text-muted-foreground">
        <Eye className="size-4" aria-hidden />
        {t('builder.preview.hint')}
      </p>
      <div className="grid gap-4 xl:grid-cols-2">
        {(['ar', 'en'] as const).map((language) => (
          <section
            key={language}
            className="flex flex-col gap-3 rounded-lg border bg-card p-4"
            data-testid={`builder-preview-${language}`}
          >
            <h2 className="text-sm font-semibold">{t(`builder.preview.${language}`)}</h2>
            <FormRenderer schema={schema} values={values} onChange={setValues} language={language} />
          </section>
        ))}
      </div>
    </div>
  );
}

function SimpleStagesEditor({
  value,
  onChange,
  options,
  errors,
}: {
  value: SimpleStages;
  onChange: (value: SimpleStages) => void;
  options: TypeOptions;
  errors: Errors;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const set = (patch: Partial<SimpleStages>) => onChange({ ...value, ...patch });
  const err = (path: string) => {
    const m = errors[path];
    return m ? translateMessageKey(tc, m.messageKey, m.params) : undefined;
  };
  return (
    <div className="flex flex-col gap-4">
      {err('stages') && <StatusBanner tone="danger" title={err('stages')} />}
      <Card title={t('builder.stages.approval')}>
        <Switch
          checked={value.approval}
          onCheckedChange={(approval) => set({ approval })}
          label={t('builder.stages.approvalOn')}
          description={t('builder.stages.approvalHint')}
        />
        {value.approval && (
          <>
            <div className="grid gap-3 md:grid-cols-2">
              <FormField label={t('builder.stages.nameAr')}>
                <Input
                  dir="rtl"
                  value={value.approvalNameAr ?? ''}
                  onChange={(e) => set({ approvalNameAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.stages.nameEn')}>
                <Input
                  dir="ltr"
                  value={value.approvalNameEn ?? ''}
                  onChange={(e) => set({ approvalNameEn: e.target.value })}
                />
              </FormField>
            </div>
            <p className="text-sm">
              {t('builder.stages.chain')}{' '}
              <Link to="/settings/approvals" className="text-primary hover:underline">
                {t('builder.stages.chainLink')}
              </Link>
            </p>
          </>
        )}
      </Card>
      <Card title={t('builder.stages.processing')}>
        <Switch
          checked={value.processing}
          onCheckedChange={(processing) => set({ processing })}
          label={t('builder.stages.processingOn')}
          description={t('builder.stages.processingHint')}
        />
        {value.processing && (
          <>
            <div className="grid gap-3 md:grid-cols-3">
              <FormField label={t('builder.stages.nameAr')}>
                <Input
                  dir="rtl"
                  value={value.processingNameAr ?? ''}
                  onChange={(e) => set({ processingNameAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.stages.nameEn')}>
                <Input
                  dir="ltr"
                  value={value.processingNameEn ?? ''}
                  onChange={(e) => set({ processingNameEn: e.target.value })}
                />
              </FormField>
              <FormField
                label={t('builder.stages.sla')}
                hint={t('builder.stages.slaHint')}
                error={err('stages.slaHours')}
              >
                <Input
                  dir="ltr"
                  inputMode="numeric"
                  value={
                    value.slaHours === null || value.slaHours === undefined ? '' : String(value.slaHours)
                  }
                  onChange={(e) => set({ slaHours: numberOrNull(e.target.value) })}
                />
              </FormField>
            </div>
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t('builder.stages.processors')}</legend>
              <p className="text-xs text-muted-foreground">{t('builder.stages.processorsHint')}</p>
              <RoleChecks
                options={options}
                selected={value.processorRoleKeys ?? []}
                onChange={(processorRoleKeys) => set({ processorRoleKeys })}
              />
            </fieldset>
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t('builder.stages.completionForm')}</legend>
              <p className="text-xs text-muted-foreground">{t('builder.stages.completionFormHint')}</p>
              <SchemaEditor
                single
                schema={value.completionForm ?? { sections: [] }}
                onChange={(completionForm) => set({ completionForm })}
                options={options}
                errors={errors}
                path="stages.completionForm"
              />
            </fieldset>
          </>
        )}
      </Card>
      <Card title={t('builder.stages.decisions')}>
        <Switch
          checked={value.allowReturn}
          onCheckedChange={(allowReturn) => set({ allowReturn })}
          label={t('builder.stages.allowReturn')}
        />
        <Switch
          checked={value.allowReject}
          onCheckedChange={(allowReject) => set({ allowReject })}
          label={t('builder.stages.allowReject')}
        />
      </Card>
    </div>
  );
}

/** Built-in stages are defined in code: only names, SLA and processor roles change here. */
function BuiltInStages({
  stages,
  onChange,
  options,
}: {
  stages: StageDefinition[];
  onChange: (stages: StageDefinition[]) => void;
  options: TypeOptions;
}) {
  const { t, i18n } = useTranslation('requests');
  const dt = useDynamicT();
  const set = (index: number, patch: Partial<StageDefinition>) =>
    onChange(stages.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  return (
    <div className="flex flex-col gap-4">
      <StatusBanner
        tone="info"
        title={t('builder.stages.builtInHint')}
        actions={
          <Button size="sm" variant="outline" asChild>
            <Link to="/settings/approvals">{t('builder.stages.chainLink')}</Link>
          </Button>
        }
      />
      {stages.map((stage, index) => (
        <Card
          key={stage.code}
          title={
            <span className="flex flex-wrap items-center gap-2">
              <Lock className="size-4 text-muted-foreground" aria-hidden />
              {pickText(i18n.language, stage.nameAr, stage.nameEn) || stage.code}
              <StatusChip tone="neutral">
                {dt(`builder.stages.kinds.${stage.kind ?? ''}`, stage.kind)}
              </StatusChip>
              <code dir="ltr" className="text-xs text-muted-foreground">
                {stage.code}
              </code>
            </span>
          }
        >
          <div className="grid gap-3 md:grid-cols-3">
            <FormField label={t('builder.stages.nameAr')}>
              <Input
                dir="rtl"
                value={stage.nameAr ?? ''}
                onChange={(e) => set(index, { nameAr: e.target.value })}
              />
            </FormField>
            <FormField label={t('builder.stages.nameEn')}>
              <Input
                dir="ltr"
                value={stage.nameEn ?? ''}
                onChange={(e) => set(index, { nameEn: e.target.value })}
              />
            </FormField>
            {stage.kind === 'processing' && (
              <FormField label={t('builder.stages.sla')} hint={t('builder.stages.slaHint')}>
                <Input
                  dir="ltr"
                  inputMode="numeric"
                  value={
                    stage.slaHours === null || stage.slaHours === undefined ? '' : String(stage.slaHours)
                  }
                  onChange={(e) => set(index, { slaHours: numberOrNull(e.target.value) })}
                />
              </FormField>
            )}
          </div>
          {stage.kind === 'processing' && (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t('builder.stages.processors')}</legend>
              <RoleChecks
                options={options}
                selected={stage.processorRoleKeys ?? []}
                onChange={(processorRoleKeys) => set(index, { processorRoleKeys })}
              />
            </fieldset>
          )}
          {(stage.actions ?? []).length > 0 && (
            <p className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-muted-foreground">{t('builder.stages.actions')}:</span>
              {(stage.actions ?? []).map((action) => (
                <StatusChip key={action.code} tone="neutral">
                  {pickText(i18n.language, action.labelAr, action.labelEn) || action.code}
                </StatusChip>
              ))}
            </p>
          )}
        </Card>
      ))}
    </div>
  );
}

const mergeFields = [
  '{{request.number}}',
  '{{request.title}}',
  '{{field.code}}',
  '{{#rows table}}…{{/rows}}',
  '{{process.stage.field}}',
  '{{org.name}}',
  '{{today}}',
];

function Templates({
  templates,
  onChange,
  typeCode,
  errors,
}: {
  templates: PdfTemplate[];
  onChange: (templates: PdfTemplate[]) => void;
  typeCode: string | undefined;
  errors: Errors;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const set = (index: number, patch: Partial<PdfTemplate>) =>
    onChange(templates.map((x, i) => (i === index ? { ...x, ...patch } : x)));
  const preview = (template: PdfTemplate, language: string) => {
    if (!typeCode) return;
    previewTypePdf(typeCode, template, language).catch((failure: unknown) =>
      toast({ tone: 'error', title: errorMessage(tc, failure) }),
    );
  };
  return (
    <div className="flex flex-col gap-4">
      <Card title={t('builder.templates.mergeFields')}>
        <p className="text-sm text-muted-foreground">{t('builder.templates.mergeHint')}</p>
        <ul className="flex flex-wrap gap-2">
          {mergeFields.map((field) => (
            <li key={field}>
              <code dir="ltr" className="rounded bg-muted px-2 py-0.5 text-xs">
                {field}
              </code>
            </li>
          ))}
        </ul>
      </Card>
      {templates.map((template, index) => {
        const m = errors[`templates[${index}]`];
        return (
          <Card
            key={index}
            title={t('builder.templates.template', { number: index + 1 })}
            actions={
              <span className="flex flex-wrap gap-1">
                {(['ar', 'en'] as const).map((language) => (
                  <Button
                    key={language}
                    size="sm"
                    variant="outline"
                    disabled={!typeCode}
                    title={typeCode ? undefined : t('builder.templates.saveFirst')}
                    onClick={() => preview(template, language)}
                    data-testid={`builder-preview-pdf-${language}`}
                  >
                    <Eye aria-hidden />
                    {t(`builder.templates.preview.${language}`)}
                  </Button>
                ))}
                <IconButton
                  label={t('builder.templates.remove')}
                  onClick={() => onChange(templates.filter((_, i) => i !== index))}
                >
                  <Trash2 aria-hidden />
                </IconButton>
              </span>
            }
          >
            {m && (
              <p className="text-xs font-medium text-danger">
                {translateMessageKey(tc, m.messageKey, m.params)}
              </p>
            )}
            <div className="grid gap-3 md:grid-cols-4">
              <FormField label={t('builder.templates.code')}>
                <Input
                  dir="ltr"
                  value={template.code ?? ''}
                  onChange={(e) => set(index, { code: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.templates.nameAr')}>
                <Input
                  dir="rtl"
                  value={template.nameAr ?? ''}
                  onChange={(e) => set(index, { nameAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.templates.nameEn')}>
                <Input
                  dir="ltr"
                  value={template.nameEn ?? ''}
                  onChange={(e) => set(index, { nameEn: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.templates.audience')}>
                <Select
                  value={template.audience ?? 'requester'}
                  onValueChange={(audience) => set(index, { audience })}
                  options={(['requester', 'processor', 'admin'] as const).map((a) => ({
                    value: a,
                    label: t(`builder.templates.audiences.${a}`),
                  }))}
                />
              </FormField>
            </div>
            <div className="grid gap-3 lg:grid-cols-2">
              <FormField label={t('builder.templates.htmlAr')}>
                <Textarea
                  dir="ltr"
                  rows={10}
                  className="font-mono text-xs"
                  value={template.htmlAr ?? ''}
                  onChange={(e) => set(index, { htmlAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('builder.templates.htmlEn')}>
                <Textarea
                  dir="ltr"
                  rows={10}
                  className="font-mono text-xs"
                  value={template.htmlEn ?? ''}
                  onChange={(e) => set(index, { htmlEn: e.target.value })}
                />
              </FormField>
            </div>
          </Card>
        );
      })}
      <div>
        <Button
          variant="outline"
          onClick={() =>
            onChange([
              ...templates,
              {
                code: uniqueCode(
                  'template',
                  templates.map((x) => x.code ?? ''),
                ),
                nameAr: '',
                nameEn: '',
                audience: 'requester',
                htmlAr: '<h1>{{request.number}}</h1>\n<p>{{request.title}}</p>',
                htmlEn: '<h1>{{request.number}}</h1>\n<p>{{request.title}}</p>',
              },
            ])
          }
          data-testid="builder-add-template"
        >
          <Plus aria-hidden />
          {t('builder.templates.add')}
        </Button>
      </div>
    </div>
  );
}
