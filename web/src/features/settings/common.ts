import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { useToast } from '@/ui';

type FieldErrors = components['schemas']['Problem']['errors'];

/**
 * Server field errors for a settings form: `fail(error)` keeps field errors (or toasts the problem), `errorFor(field)`
 * returns the translated message for a field.
 */
export function useServerErrors() {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [errors, setErrors] = useState<FieldErrors>();
  return {
    clear: () => setErrors(undefined),
    fail: (failure: unknown) => {
      if (failure instanceof ApiError && failure.problem.errors?.length) setErrors(failure.problem.errors);
      else toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
    errorFor: (field: string) => {
      const error = errors?.find((e) => e.field === field || e.field.startsWith(`${field}[`));
      return error ? translateMessageKey(tc, error.messageKey, error.params ?? undefined) : undefined;
    },
  };
}
