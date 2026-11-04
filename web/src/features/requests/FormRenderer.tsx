import { Download, Plus, Trash2, Upload, X } from 'lucide-react';
import { useEffect, useId, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { contentUrl, useAttachments } from '@/features/files/api';
import { LinkBadge } from '@/features/links/LinkBadge';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { useEmployee, useUnitTree } from '@/features/org/api';
import { useLookupOptions, useLookupValues } from '@/features/settings/lookups';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  DatePicker,
  FormField,
  Input,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import {
  requestApi,
  requestResource,
  useDocumentSearch,
  useLinkView,
  type FormFieldDef,
  type FormSchema,
  type FormValues,
} from './api';
import { fieldLabel, isRequired, isVisible, type FieldErrors } from './formLogic';

export interface FieldDecoration {
  className?: string;
  after?: ReactNode;
}

export interface FormRendererProps {
  schema: FormSchema;
  values: FormValues;
  onChange?: (values: FormValues) => void;
  /** Server field errors by path relative to the form (`code`, `table[1].column`). */
  errors?: FieldErrors;
  /** Only these sections (wizard steps); all when omitted. */
  sectionCodes?: string[];
  /** Section titles as headings (off in mini-form dialogs with a single section). */
  showSectionTitles?: boolean;
  /** Labels in this language instead of the user's (builder preview). */
  language?: string;
  readOnly?: boolean;
  /** Disables inputs while showing them as editable (builder preview). */
  disabled?: boolean;
  /** The request files belong to; `ensureRequest` creates the draft first when there is none yet. */
  requestId?: string;
  ensureRequest?: () => Promise<string>;
  /** Extra styling / content per field (AI drafts to review). */
  decorate?: (field: FormFieldDef) => FieldDecoration | undefined;
}

/**
 * Renders an admin-defined form (docs/design/requests.md §2.2): every field type, `visibleWhen` / `requiredWhen` evaluated live like the
 * server does, AR / EN labels and help, required markers and the server's field errors. Reused by the wizard, mini-form dialogs and the
 * builder preview.
 */
export function FormRenderer(props: FormRendererProps) {
  const { schema, values, sectionCodes, showSectionTitles = true } = props;
  const { i18n } = useTranslation();
  const language = props.language ?? i18n.language;
  if (props.readOnly) return <FormValuesView schema={schema} values={values} language={language} />;
  const sections = (schema.sections ?? []).filter(
    (section) => !sectionCodes || sectionCodes.includes(section.code ?? ''),
  );
  return (
    <div
      className="flex flex-col gap-6"
      dir={props.language ? (language === 'ar' ? 'rtl' : 'ltr') : undefined}
    >
      {sections.map((section) => (
        <section key={section.code} className="flex flex-col gap-4">
          {showSectionTitles && sections.length > 1 && (
            <h3 className="font-semibold">{pickText(language, section.titleAr, section.titleEn)}</h3>
          )}
          <div className="grid gap-4 md:grid-cols-2">
            {(section.fields ?? [])
              .filter((field) => isVisible(field, values))
              .map((field) => (
                <FieldInput
                  key={field.code}
                  {...props}
                  language={language}
                  field={field}
                  path={field.code ?? ''}
                  rowValues={values}
                  value={values[field.code ?? '']}
                  onValue={(value) => props.onChange?.({ ...values, [field.code ?? '']: value })}
                />
              ))}
          </div>
        </section>
      ))}
    </div>
  );
}

const wide = new Set(['textarea', 'richtext', 'table', 'multipicklist', 'file']);

interface FieldInputProps extends FormRendererProps {
  language: string;
  field: FormFieldDef;
  path: string;
  /** Values the field's conditions read (the form, or the table row). */
  rowValues: FormValues;
  value: unknown;
  onValue: (value: unknown) => void;
}

function FieldInput(props: FieldInputProps) {
  const { field, language, path, errors } = props;
  const { t: tc } = useTranslation();
  const required = isRequired(field, props.rowValues);
  const error = errors?.[path];
  const decoration = props.decorate?.(field);
  const help = pickText(language, field.helpAr, field.helpEn);
  const label = fieldLabel(field, language);
  const errorText = error ? translateMessageKey(tc, error.messageKey, error.params) : undefined;

  if (field.type === 'checkbox')
    return (
      <div className={cn('flex flex-col gap-1', decoration?.className, 'rounded-md')}>
        <Checkbox
          label={
            <>
              {label}
              {required && (
                <span className="ms-0.5 text-danger" aria-hidden>
                  *
                </span>
              )}
            </>
          }
          description={help || undefined}
          checked={props.value === true}
          disabled={props.disabled}
          aria-invalid={error ? true : undefined}
          onChange={(e) => props.onValue(e.target.checked)}
          data-testid={`field-${path}`}
        />
        {errorText && (
          <p role="alert" className="text-xs font-medium text-danger">
            {errorText}
          </p>
        )}
        {decoration?.after}
      </div>
    );

  if (field.type === 'table' || field.type === 'multipicklist' || field.type === 'file')
    return (
      <fieldset className={cn('flex flex-col gap-2 md:col-span-2', decoration?.className)}>
        <legend className="mb-1.5 text-sm font-medium">
          {label}
          {required && (
            <span className="ms-0.5 text-danger" aria-hidden>
              *
            </span>
          )}
        </legend>
        {field.type === 'table' && <TableInput {...props} />}
        {field.type === 'multipicklist' && <MultiPicklistInput {...props} />}
        {field.type === 'file' && <FileInput {...props} />}
        {help && <p className="text-xs text-muted-foreground">{help}</p>}
        {errorText && (
          <p role="alert" className="text-xs font-medium text-danger">
            {errorText}
          </p>
        )}
        {decoration?.after}
      </fieldset>
    );

  return (
    <div className={cn('flex flex-col gap-1', wide.has(field.type ?? '') && 'md:col-span-2')}>
      <FormField label={label} required={required} hint={help || undefined} error={errorText}>
        <ScalarInput {...props} className={decoration?.className} />
      </FormField>
      {decoration?.after}
    </div>
  );
}

function text(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return JSON.stringify(value);
}

/** One-control fields; receives the id / ARIA props from FormField. */
function ScalarInput(
  props: FieldInputProps & {
    className?: string | undefined;
    id?: string;
    'aria-invalid'?: boolean;
    'aria-describedby'?: string;
  },
) {
  const { field, value, onValue, disabled, language, className } = props;
  const { t } = useTranslation('requests', { lng: language });
  const aria = {
    id: props.id,
    'aria-invalid': props['aria-invalid'],
    'aria-describedby': props['aria-describedby'],
  };
  const testId = `field-${props.path}`;
  switch (field.type) {
    case 'textarea':
    case 'richtext':
      return (
        <Textarea
          {...aria}
          dir="auto"
          rows={field.type === 'richtext' ? 6 : 4}
          maxLength={field.maxLength ?? undefined}
          value={text(value)}
          disabled={disabled}
          className={className}
          data-testid={testId}
          onChange={(e) => onValue(e.target.value)}
        />
      );
    case 'number':
    case 'currency':
      return (
        <Input
          {...aria}
          dir="ltr"
          inputMode="decimal"
          value={text(value)}
          disabled={disabled}
          className={className}
          data-testid={testId}
          onChange={(e) => onValue(e.target.value)}
        />
      );
    case 'date':
      return (
        <DatePicker
          {...aria}
          value={typeof value === 'string' && value ? value : undefined}
          disabled={disabled}
          onChange={(date) => onValue(date ?? null)}
        />
      );
    case 'picklist':
      return <PicklistInput {...props} {...aria} />;
    case 'employee':
      return disabled ? (
        <Input {...aria} disabled value="" />
      ) : (
        <EmployeePicker
          {...aria}
          value={typeof value === 'string' && value ? value : undefined}
          onChange={(id) => onValue(id ?? null)}
        />
      );
    case 'unit':
      return disabled ? (
        <Input {...aria} disabled value="" />
      ) : (
        <UnitSelect
          {...aria}
          allowEmpty
          value={typeof value === 'string' && value ? value : undefined}
          onChange={(id) => onValue(id ?? null)}
        />
      );
    case 'record':
      return <RecordInput {...props} {...aria} />;
    case 'secret':
      return (
        <Input
          {...aria}
          dir="ltr"
          autoComplete="off"
          spellCheck={false}
          placeholder={field.format === 'emiratesId' ? t('form.emiratesIdPlaceholder') : undefined}
          value={text(value)}
          disabled={disabled}
          className={className}
          data-testid={testId}
          onChange={(e) => onValue(e.target.value)}
        />
      );
    default:
      return (
        <Input
          {...aria}
          dir="auto"
          maxLength={field.maxLength ?? undefined}
          value={text(value)}
          disabled={disabled}
          className={className}
          data-testid={testId}
          onChange={(e) => onValue(e.target.value)}
        />
      );
  }
}

function PicklistInput(
  props: FieldInputProps & { id?: string; 'aria-invalid'?: boolean; 'aria-describedby'?: string },
) {
  const { t } = useTranslation('requests', { lng: props.language });
  const current = typeof props.value === 'string' ? props.value : undefined;
  const options = useLookupOptions(props.field.lookupType ?? '', current, t('form.none'));
  return (
    <Select
      id={props.id}
      aria-invalid={props['aria-invalid']}
      aria-describedby={props['aria-describedby']}
      disabled={props.disabled}
      value={current ?? '-'}
      onValueChange={(value) => props.onValue(value === '-' ? null : value)}
      options={options}
      className={props.decorate?.(props.field)?.className}
    />
  );
}

function MultiPicklistInput(props: FieldInputProps) {
  const values = useLookupValues(props.field.lookupType ?? '');
  const selected = Array.isArray(props.value) ? (props.value as string[]) : [];
  const options = (values.data ?? []).filter((v) => v.active || selected.includes(v.code));
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {options.map((option) => (
        <Checkbox
          key={option.code}
          label={pickText(props.language, option.labelAr, option.labelEn) || option.code}
          checked={selected.includes(option.code)}
          disabled={props.disabled}
          onChange={(e) =>
            props.onValue(
              e.target.checked ? [...selected, option.code] : selected.filter((code) => code !== option.code),
            )
          }
        />
      ))}
    </div>
  );
}

function TableInput(props: FieldInputProps) {
  const { field, language, path } = props;
  const { t } = useTranslation('requests', { lng: language });
  const rows = Array.isArray(props.value) ? (props.value as FormValues[]) : [];
  const columns = field.columns ?? [];
  const setRow = (index: number, row: FormValues) =>
    props.onValue(rows.map((r, i) => (i === index ? row : r)));
  const canAdd =
    !props.disabled && (field.maxRows === null || field.maxRows === undefined || rows.length < field.maxRows);
  return (
    <div className="flex flex-col gap-3">
      {rows.length === 0 && <p className="text-sm text-muted-foreground">{t('form.noRows')}</p>}
      {rows.map((row, index) => (
        <div
          key={index}
          className="flex flex-col gap-3 rounded-md border bg-muted/30 p-3"
          data-testid={`row-${path}-${index}`}
        >
          <div className="flex items-center justify-between gap-2">
            <span className="text-sm font-medium">{t('form.row', { number: index + 1 })}</span>
            <Button
              type="button"
              size="icon"
              variant="ghost"
              className="size-8"
              disabled={props.disabled}
              aria-label={t('form.removeRow', { number: index + 1 })}
              onClick={() => props.onValue(rows.filter((_, i) => i !== index))}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            {columns
              .filter((column) => isVisible(column, row))
              .map((column) => (
                <FieldInput
                  key={column.code}
                  {...props}
                  field={column}
                  path={`${path}[${index}].${column.code ?? ''}`}
                  rowValues={row}
                  value={row[column.code ?? '']}
                  onValue={(value) => setRow(index, { ...row, [column.code ?? '']: value })}
                />
              ))}
          </div>
        </div>
      ))}
      <div>
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={!canAdd}
          onClick={() => props.onValue([...rows, {}])}
          data-testid={`add-row-${path}`}
        >
          <Plus aria-hidden />
          {t('form.addRow')}
        </Button>
      </div>
    </div>
  );
}

function FileInput(props: FieldInputProps) {
  const { field, language } = props;
  const { t } = useTranslation('requests', { lng: language });
  const { t: tc } = useTranslation();
  const toast = useToast();
  const inputId = useId();
  const [busy, setBusy] = useState(false);
  const ids = Array.isArray(props.value)
    ? (props.value as string[])
    : typeof props.value === 'string' && props.value
      ? [props.value]
      : [];
  const accept = field.accept?.length ? field.accept.map((kind) => `.${kind}`).join(',') : undefined;
  const upload = async (files: FileList | null) => {
    if (!files?.length) return;
    setBusy(true);
    try {
      const requestId = props.requestId ?? (await props.ensureRequest?.());
      if (!requestId) return;
      const uploaded: string[] = [];
      for (const file of Array.from(field.multiple ? files : [files[0] as File])) {
        const stored = await requestApi.upload({ requestId, file });
        uploaded.push(stored.id);
      }
      props.onValue(field.multiple ? [...ids, ...uploaded] : (uploaded[0] ?? null));
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  };
  const remove = (id: string) => props.onValue(field.multiple ? ids.filter((x) => x !== id) : null);
  const canUpload = !props.disabled && (props.requestId !== undefined || props.ensureRequest !== undefined);
  return (
    <div className="flex flex-col gap-2">
      <FileList requestId={props.requestId} ids={ids} onRemove={props.disabled ? undefined : remove} />
      {(field.multiple || ids.length === 0) && (
        <div>
          <label
            htmlFor={inputId}
            className={cn(
              'inline-flex h-8 cursor-pointer items-center gap-2 rounded-md border border-input bg-card px-3 text-sm font-medium hover:bg-muted',
              (!canUpload || busy) && 'pointer-events-none opacity-50',
            )}
          >
            <Upload className="size-4" aria-hidden />
            {busy ? t('form.uploading') : t('form.upload')}
          </label>
          <input
            id={inputId}
            type="file"
            className="sr-only"
            accept={accept}
            multiple={field.multiple === true}
            disabled={!canUpload || busy}
            data-testid={`field-${props.path}`}
            onChange={(e) => {
              void upload(e.target.files);
              e.target.value = '';
            }}
          />
        </div>
      )}
    </div>
  );
}

function FileList({
  requestId,
  ids,
  onRemove,
}: {
  requestId: string | undefined;
  ids: string[];
  onRemove?: ((id: string) => void) | undefined;
}) {
  const { t } = useTranslation('requests');
  const files = useAttachments(requestResource, requestId ?? '');
  if (ids.length === 0) return null;
  const byId = new Map((requestId ? (files.data ?? []) : []).map((f) => [f.id, f]));
  return (
    <ul className="flex flex-col gap-1">
      {ids.map((id) => {
        const file = byId.get(id);
        return (
          <li
            key={id}
            className="flex items-center justify-between gap-2 rounded-md border px-3 py-1.5 text-sm"
          >
            <bdi className="truncate">{file?.name ?? t('form.file')}</bdi>
            <span className="flex shrink-0 gap-1">
              <Button size="icon" variant="ghost" className="size-7" asChild>
                <a
                  href={contentUrl(id, undefined, true)}
                  aria-label={t('form.download', { name: file?.name ?? '' })}
                >
                  <Download aria-hidden />
                </a>
              </Button>
              {onRemove && (
                <Button
                  type="button"
                  size="icon"
                  variant="ghost"
                  className="size-7"
                  aria-label={t('form.remove')}
                  onClick={() => onRemove(id)}
                >
                  <X aria-hidden />
                </Button>
              )}
            </span>
          </li>
        );
      })}
    </ul>
  );
}

/** A record of another system (Links registry): library search for documents, otherwise its id. */
function RecordInput(
  props: FieldInputProps & { id?: string; 'aria-invalid'?: boolean; 'aria-describedby'?: string },
) {
  const { field, language } = props;
  const { t } = useTranslation('requests', { lng: language });
  const value = typeof props.value === 'string' && props.value ? props.value : undefined;
  const aria = {
    id: props.id,
    'aria-invalid': props['aria-invalid'],
    'aria-describedby': props['aria-describedby'],
  };
  if (value)
    return (
      <div
        {...aria}
        className="flex min-h-10 items-center justify-between gap-2 rounded-md border border-input bg-card px-3 py-1.5"
      >
        <RecordBadge type={field.targetType} id={value} />
        {!props.disabled && (
          <Button
            type="button"
            size="icon"
            variant="ghost"
            className="size-7"
            aria-label={t('form.clearRecord')}
            onClick={() => props.onValue(null)}
          >
            <X aria-hidden />
          </Button>
        )}
      </div>
    );
  if (field.targetType === 'docreg.document')
    return <DocumentSearch {...aria} disabled={props.disabled} onPick={(id) => props.onValue(id)} />;
  return (
    <Input
      {...aria}
      dir="ltr"
      placeholder={t('form.recordId')}
      disabled={props.disabled}
      onBlur={(e) => {
        const id = e.target.value.trim();
        if (/^[0-9a-f-]{36}$/iu.test(id)) props.onValue(id);
      }}
    />
  );
}

function DocumentSearch({
  onPick,
  disabled,
  ...aria
}: {
  onPick: (id: string) => void;
  disabled?: boolean | undefined;
  id?: string | undefined;
  'aria-invalid'?: boolean | undefined;
  'aria-describedby'?: string | undefined;
}) {
  const { t, i18n } = useTranslation('requests');
  const listId = useId();
  const [term, setTerm] = useState('');
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(term.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [term]);
  const results = useDocumentSearch(debounced, debounced.length > 1);
  const items = results.data?.page.items ?? [];
  return (
    <div className="relative">
      <Input
        {...aria}
        role="combobox"
        aria-expanded={items.length > 0}
        aria-controls={listId}
        aria-autocomplete="list"
        placeholder={t('form.recordSearch')}
        value={term}
        disabled={disabled}
        onChange={(e) => setTerm(e.target.value)}
      />
      {debounced.length > 1 && (
        <ul
          id={listId}
          role="listbox"
          className="absolute inset-x-0 top-full z-20 mt-1 max-h-60 overflow-y-auto rounded-md border bg-card shadow-lg"
        >
          {items.length === 0 ? (
            <li className="px-3 py-2 text-sm text-muted-foreground">{t('form.recordNone')}</li>
          ) : (
            items.map((doc) => (
              <li key={doc.id} role="option" aria-selected={false}>
                <button
                  type="button"
                  className="w-full px-3 py-2 text-start text-sm hover:bg-muted"
                  onClick={() => {
                    onPick(doc.id);
                    setTerm('');
                  }}
                >
                  {doc.documentNo && <bdi className="me-2 text-muted-foreground">{doc.documentNo}</bdi>}
                  <span dir="auto">{pickText(i18n.language, doc.nameAr, doc.nameEn)}</span>
                </button>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}

export function RecordBadge({ type, id }: { type: string | null | undefined; id: string }) {
  const view = useLinkView(type, id);
  if (view.data) return <LinkBadge view={view.data} />;
  return (
    <code dir="ltr" className="text-xs break-all">
      {id}
    </code>
  );
}

/** Read-only display of form values by field type (overview tab, review step, process data). */
export function FormValuesView({
  schema,
  values,
  language,
  requestId,
}: {
  schema: FormSchema;
  values: FormValues;
  language?: string;
  requestId?: string | undefined;
}) {
  const { i18n } = useTranslation();
  const lang = language ?? i18n.language;
  const sections = (schema.sections ?? []).filter((section) =>
    (section.fields ?? []).some((f) => isVisible(f, values)),
  );
  return (
    <div className="flex flex-col gap-6">
      {sections.map((section) => (
        <section key={section.code} className="flex flex-col gap-3">
          {sections.length > 1 && (
            <h3 className="font-semibold">{pickText(lang, section.titleAr, section.titleEn)}</h3>
          )}
          <dl className="grid gap-4 md:grid-cols-2">
            {(section.fields ?? [])
              .filter((f) => isVisible(f, values))
              .map((field) => (
                <div key={field.code} className={cn(wide.has(field.type ?? '') && 'md:col-span-2')}>
                  <dt className="text-sm text-muted-foreground">{fieldLabel(field, lang)}</dt>
                  <dd className="mt-1" data-testid={`value-${field.code ?? ''}`}>
                    <ValueView
                      field={field}
                      value={values[field.code ?? '']}
                      language={lang}
                      requestId={requestId}
                    />
                  </dd>
                </div>
              ))}
          </dl>
        </section>
      ))}
    </div>
  );
}

function LookupLabel({ type, code, language }: { type: string; code: string; language: string }) {
  const values = useLookupValues(type);
  const value = values.data?.find((v) => v.code === code);
  return <>{value ? pickText(language, value.labelAr, value.labelEn) || code : code}</>;
}

function EmployeeName({ id, language }: { id: string; language: string }) {
  const employee = useEmployee(id);
  return <>{employee.data ? pickText(language, employee.data.nameAr, employee.data.nameEn) : '…'}</>;
}

export function ValueView({
  field,
  value,
  language,
  requestId,
}: {
  field: FormFieldDef;
  value: unknown;
  language: string;
  requestId?: string | undefined;
}) {
  const { t } = useTranslation('requests', { lng: language });
  if (value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0))
    return <span className="text-muted-foreground">—</span>;
  switch (field.type) {
    case 'checkbox':
      return <>{value === true ? t('form.yes') : t('form.no')}</>;
    case 'number':
    case 'currency': {
      const n = typeof value === 'number' ? value : Number(value);
      return (
        <span className="tabular-nums">{Number.isFinite(n) ? formatNumber(n, language) : text(value)}</span>
      );
    }
    case 'date':
      return <>{typeof value === 'string' ? formatDate(value, language) : text(value)}</>;
    case 'picklist':
      return <LookupLabel type={field.lookupType ?? ''} code={text(value)} language={language} />;
    case 'multipicklist':
      return (
        <span className="flex flex-wrap gap-1">
          {(value as string[]).map((code, i) => (
            <span key={code} className="rounded bg-muted px-2 py-0.5 text-sm">
              <LookupLabel type={field.lookupType ?? ''} code={code} language={language} />
              {i < (value as string[]).length - 1 ? '' : ''}
            </span>
          ))}
        </span>
      );
    case 'employee':
      return <EmployeeName id={text(value)} language={language} />;
    case 'unit':
      return <UnitName id={text(value)} language={language} />;
    case 'record':
      return <RecordBadge type={field.targetType} id={text(value)} />;
    case 'file':
      return (
        <FileList requestId={requestId} ids={Array.isArray(value) ? (value as string[]) : [text(value)]} />
      );
    case 'secret':
      return (
        <bdi dir="ltr" className="font-mono">
          {text(value)}
        </bdi>
      );
    case 'richtext':
      return (
        <p dir="auto" className="whitespace-pre-line">
          {plainText(text(value))}
        </p>
      );
    case 'textarea':
      return (
        <p dir="auto" className="whitespace-pre-line">
          {text(value)}
        </p>
      );
    case 'table': {
      const rows = Array.isArray(value) ? (value as FormValues[]) : [];
      const columns = field.columns ?? [];
      return (
        <div className="overflow-x-auto rounded-md border">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>#</TableHeaderCell>
                {columns.map((c) => (
                  <TableHeaderCell key={c.code}>{fieldLabel(c, language)}</TableHeaderCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row, i) => (
                <TableRow key={i}>
                  <TableCell className="tabular-nums">{i + 1}</TableCell>
                  {columns.map((c) => (
                    <TableCell key={c.code}>
                      <ValueView field={c} value={row[c.code ?? '']} language={language} />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      );
    }
    default:
      return <span dir="auto">{text(value)}</span>;
  }
}

function UnitName({ id, language }: { id: string; language: string }) {
  const unit = useOrgUnitName(id);
  return <>{unit ? pickText(language, unit.nameAr, unit.nameEn) : '…'}</>;
}

function useOrgUnitName(id: string) {
  const tree = useUnitTree();
  return tree.data?.find((node) => node.id === id);
}

/**
 * Rich text shown as its text only: the HTML is parsed into an inert document (no scripts run, nothing is inserted into the page), so
 * nothing from the value can execute.
 */
export function plainText(html: string): string {
  if (!/[<&]/u.test(html)) return html;
  const doc = new DOMParser().parseFromString(
    html.replace(/<(br|\/p|\/li|\/h\d)\s*\/?>/giu, '$&\n'),
    'text/html',
  );
  return doc.body.textContent.trim();
}
