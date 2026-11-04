import { ApiError } from '@/api/client';
import { pickText } from '@/lib/format';
import type { FieldCondition, FormFieldDef, FormSchema, FormValues } from './api';

/** A server field error, kept as message key + params and translated when rendered. */
export interface FieldMessage {
  messageKey: string;
  params?: Record<string, unknown> | null;
}

/** Field errors by path relative to the form: `code`, `table[2].column`. */
export type FieldErrors = Record<string, FieldMessage>;

export const fieldTypes = [
  'text',
  'textarea',
  'richtext',
  'number',
  'currency',
  'date',
  'checkbox',
  'picklist',
  'multipicklist',
  'employee',
  'unit',
  'file',
  'record',
  'table',
  'secret',
] as const;

/** Types allowed as table columns (as the server's FieldTypes.Columns). */
export const columnTypes = [
  'text',
  'textarea',
  'number',
  'currency',
  'date',
  'checkbox',
  'picklist',
  'employee',
  'unit',
  'secret',
] as const;

export const conditionOps = ['eq', 'ne', 'in', 'filled', 'empty'] as const;

/** Same emptiness rule as the server's FormDataValidator.IsEmpty. */
export function isEmpty(value: unknown): boolean {
  if (value === null || value === undefined) return true;
  if (typeof value === 'string') return value.trim() === '';
  if (Array.isArray(value)) return value.length === 0;
  return false;
}

function numberOf(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '' && /^-?\d+(\.\d+)?$/u.test(value.trim()))
    return Number(value.trim());
  return null;
}

function scalar(value: unknown): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  return JSON.stringify(value);
}

function same(a: unknown, b: unknown): boolean {
  if (isEmpty(a) || isEmpty(b)) return isEmpty(a) && isEmpty(b);
  const x = numberOf(a);
  const y = numberOf(b);
  if (x !== null && y !== null) return x === y;
  return scalar(a) === scalar(b);
}

/** Does the condition hold on these values? Mirrors FormDataValidator.Holds so the screen hides exactly what the server drops. */
export function holds(condition: FieldCondition, values: FormValues): boolean {
  const value = values[condition.field];
  switch (condition.op) {
    case 'filled':
      return !isEmpty(value);
    case 'empty':
      return isEmpty(value);
    case 'eq':
      return same(value, condition.value);
    case 'ne':
      return !same(value, condition.value);
    case 'in':
      return Array.isArray(condition.value) && condition.value.some((option) => same(value, option));
    default:
      return false;
  }
}

export function isVisible(field: FormFieldDef, values: FormValues): boolean {
  return !field.visibleWhen || holds(field.visibleWhen, values);
}

export function isRequired(field: FormFieldDef, values: FormValues): boolean {
  return field.required === true || (field.requiredWhen ? holds(field.requiredWhen, values) : false);
}

export function fieldLabel(field: FormFieldDef, language: string): string {
  return pickText(language, field.labelAr, field.labelEn) || (field.code ?? '');
}

export function allFields(schema: FormSchema | null | undefined): FormFieldDef[] {
  return (schema?.sections ?? []).flatMap((section) => section.fields ?? []);
}

export function isEmptySchema(schema: FormSchema | null | undefined): boolean {
  return allFields(schema).length === 0;
}

function normaliseValue(field: FormFieldDef, value: unknown): unknown {
  if (isEmpty(value)) return undefined;
  switch (field.type) {
    case 'number':
    case 'currency': {
      const n = numberOf(value);
      return n ?? value;
    }
    case 'table':
      return Array.isArray(value)
        ? value.map((row) =>
            normalise({ sections: [{ code: 'row', fields: field.columns ?? [] }] }, row as FormValues),
          )
        : value;
    default:
      return value;
  }
}

/**
 * Values to send: only fields of the schema, typed numbers, empty values and hidden fields left out (the server drops hidden values
 * anyway and refuses unknown keys).
 */
export function normalise(schema: FormSchema, values: FormValues): FormValues {
  const output: FormValues = {};
  for (const field of allFields(schema)) {
    const code = field.code ?? '';
    if (!isVisible(field, values)) continue;
    const value = normaliseValue(field, values[code]);
    if (value !== undefined) output[code] = value;
  }
  return output;
}

/** Initial values of an empty form: tables start with their minimum rows, checkboxes unticked. */
export function initialValues(schema: FormSchema, prefill: FormValues = {}): FormValues {
  const values: FormValues = {};
  for (const field of allFields(schema)) {
    const code = field.code ?? '';
    if (code in prefill) values[code] = prefill[code];
    else if (field.type === 'table')
      values[code] = Array.from({ length: Math.max(0, field.minRows ?? 0) }, () => ({}));
  }
  return values;
}

/** Field errors of a failed call under a prefix (`fields` for the request form, `form` for mini-forms), relative to the form. */
export function fieldErrorsOf(error: unknown, prefix: 'fields' | 'form'): FieldErrors {
  if (!(error instanceof ApiError) || !error.problem.errors?.length) return {};
  const result: FieldErrors = {};
  for (const e of error.problem.errors) {
    if (e.field.startsWith(`${prefix}.`)) {
      result[e.field.slice(prefix.length + 1)] = {
        messageKey: e.messageKey,
        params: e.params ?? null,
      };
    }
  }
  return result;
}

/** Errors of a call that do not belong to a form field (shown in a banner). */
export function otherErrors(error: unknown, prefixes: string[]): FieldMessage[] {
  if (!(error instanceof ApiError) || !error.problem.errors?.length) return [];
  return error.problem.errors
    .filter((e) => !prefixes.some((p) => e.field.startsWith(`${p}.`)))
    .map((e) => ({ messageKey: e.messageKey, params: e.params ?? null }));
}

/** The first section holding an error (the wizard jumps there). */
export function sectionOfError(schema: FormSchema, errors: FieldErrors): number {
  const codes = Object.keys(errors).map((path) => path.split(/[.[]/u)[0]);
  const sections = schema.sections ?? [];
  return sections.findIndex((section) => (section.fields ?? []).some((f) => codes.includes(f.code ?? '')));
}

/** Parses the wizard's `values` search parameter (JSON object); anything else is ignored. */
export function parsePrefill(raw: string | undefined): FormValues {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as FormValues) : {};
  } catch {
    return {};
  }
}

/** Secret values come back masked (e.g. `784-****-*******-9`); sending the mask back keeps the stored value. */
export function isMasked(value: unknown): boolean {
  return typeof value === 'string' && value.includes('*');
}
