import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError, type Problem } from './client';

/**
 * Server message keys are `<namespace>.<key>` (e.g. `errors.notFound`, `validation.notEmpty`,
 * `sample.errors.inactive`). The server never sends sentences; the client translates.
 */
export function translateMessageKey(
  t: TFunction,
  messageKey: string,
  params?: Record<string, unknown> | null,
): string {
  // Keys from the server are only known at runtime, so they go through an untyped view of `t`.
  const translate = t as unknown as DynamicT;
  const dot = messageKey.indexOf('.');
  const key = dot > 0 ? `${messageKey.slice(0, dot)}:${messageKey.slice(dot + 1)}` : messageKey;
  return (
    translate(key, { ...(params ?? {}), defaultValue: '' }) ||
    translate('errors:unexpected', { reference: '' })
  );
}

type DynamicT = (key: string, options?: Record<string, unknown>) => string;

/** A user-facing sentence for any error thrown by a query or mutation. */
export function errorMessage(t: TFunction, error: unknown): string {
  if (error instanceof ApiError) {
    return translateMessageKey(t, error.problem.messageKey, {
      reference: error.problem.correlationId ?? error.problem.traceId ?? '',
      ...(error.problem.params ?? {}),
    });
  }
  return (t as unknown as DynamicT)('errors:network');
}

/**
 * Puts server validation errors on the matching form fields. Returns true when at least one field took an error,
 * so the caller can skip a generic toast.
 */
export function applyFieldErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly string[],
): boolean {
  if (!(error instanceof ApiError) || !error.problem.errors?.length) return false;
  let applied = false;
  for (const fieldError of error.problem.errors) {
    if (fields.includes(fieldError.field)) {
      setError(fieldError.field as Path<T>, { type: 'server', message: encodeFieldMessage(fieldError) });
      applied = true;
    }
  }
  return applied;
}

/** Field messages travel through react-hook-form as `key|{json params}` and are translated when rendered. */
export function encodeFieldMessage(
  error: Pick<NonNullable<Problem['errors']>[number], 'messageKey' | 'params'>,
): string {
  return error.params ? `${error.messageKey}|${JSON.stringify(error.params)}` : error.messageKey;
}

export function translateFieldMessage(t: TFunction, message: string | undefined): string | undefined {
  if (!message) return undefined;
  const [key = '', params] = message.split('|', 2);
  return translateMessageKey(t, key, params ? (JSON.parse(params) as Record<string, unknown>) : null);
}
