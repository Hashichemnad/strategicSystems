import type { TFunction } from 'i18next';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import { formatNumber } from '@/lib/format';
import type { Tone } from '@/ui';

/** Translation with a key only known at run time (codes from the server), inside the `integration` namespace. */
export type DynamicT = (key: string, options?: Record<string, unknown>) => string;

export function useSyncT(): { tx: DynamicT; t: TFunction<'integration'>; language: string } {
  const { t, i18n } = useTranslation('integration');
  return { tx: t as unknown as DynamicT, t, language: i18n.language };
}

/** A server message key (`integration.sync.log.objectDone`, `health.…`, `auth.errors.…`) with its params. */
export function useMessage(): (
  key: string | null | undefined,
  params?: Record<string, unknown> | null,
) => string {
  const { t } = useTranslation();
  return (key, params) => {
    if (!key) return '';
    const text = translateMessageKey(t, key, params ?? null);
    // Connector failures carry Salesforce's own (untranslated, technical) detail: shown after the sentence.
    const detail = params?.['detail'];
    return typeof detail === 'string' && detail ? `${text} (${detail})` : text;
  };
}

export const modeTone: Record<string, Tone> = {
  disabled: 'neutral',
  standby: 'success',
  primary: 'danger',
  reconciling: 'warning',
};

export const runTone: Record<string, Tone> = {
  running: 'info',
  succeeded: 'success',
  partial: 'warning',
  failed: 'danger',
  cancelled: 'neutral',
};

export const exceptionTone: Record<string, Tone> = {
  open: 'warning',
  resolved: 'success',
  ignored: 'neutral',
};

export const severityTone: Record<string, Tone> = { error: 'danger', warning: 'warning', info: 'info' };

export function Fact({ label, children }: { label: ReactNode; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

/** An API name, ID or URL: always left-to-right, start-aligned in both directions. */
export function Code({ children }: { children: ReactNode }) {
  return (
    <span dir="ltr" className="font-mono text-xs [overflow-wrap:anywhere]">
      {children}
    </span>
  );
}

export function formatDuration(seconds: number | null | undefined, language: string, tx: DynamicT): string {
  if (seconds === null || seconds === undefined) return '—';
  if (seconds < 60) return tx('sync.units.seconds', { value: formatNumber(Math.round(seconds), language) });
  return tx('sync.units.minutes', { value: formatNumber(Math.round(seconds / 6) / 10, language) });
}

/** Parses JSON typed in an editor; undefined when it is not valid JSON of the expected shape. */
export function parseJson(text: string, check: (value: unknown) => boolean): unknown {
  try {
    const value: unknown = JSON.parse(text);
    return check(value) ? value : undefined;
  } catch {
    return undefined;
  }
}

export const isPlainObject = (value: unknown) =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
