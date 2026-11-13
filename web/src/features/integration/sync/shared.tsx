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
    const text = translateMessageKey(t, key, localiseParams(t, params));
    // What Salesforce answered is shown by <SalesforceErrorDetails> (ADR 0211); older runs only have a raw `detail`.
    if (salesforceErrorOf(params)) return text;
    const detail = params?.['detail'];
    return typeof detail === 'string' && detail ? `${text} (${detail})` : text;
  };
}

/** Code-valued params of validation messages shown as words: where a queried field is used, the kind of a target field. */
function localiseParams(t: TFunction, params: Record<string, unknown> | null | undefined) {
  if (!params) return null;
  const result: Record<string, unknown> = { ...params };
  if (typeof params['usage'] === 'string')
    result['usage'] = t(`integration:sync.queryUsage.${params['usage']}`, { defaultValue: params['usage'] });
  if (typeof params['targetKind'] === 'string')
    result['targetKind'] = t(`integration:sync.targetKinds.${params['targetKind']}`, {
      defaultValue: params['targetKind'],
    });
  return result;
}

/** What Salesforce answered to a failed call (ADR 0211): HTTP status, the call, Salesforce's error code and message. */
export interface SalesforceErrorInfo {
  status?: string | number | null;
  call?: string | null;
  errorCode?: string | null;
  message?: string | null;
}

/** The Salesforce answer inside message params (run log, run, validation issue, warning), or null. */
export function salesforceErrorOf(
  params: Record<string, unknown> | null | undefined,
): SalesforceErrorInfo | null {
  if (!params || typeof params['call'] !== 'string') return null;
  const text = (key: string) => (typeof params[key] === 'string' && params[key] ? params[key] : null);
  return {
    status: text('status'),
    call: text('call'),
    errorCode: text('errorCode'),
    message: text('message'),
  };
}

/** The technical details of a Salesforce answer, labelled in the user's language; the values themselves stay as Salesforce sent them (LTR). */
export function SalesforceErrorDetails({ error }: { error: SalesforceErrorInfo }) {
  const { t } = useTranslation('integration');
  const rows = (
    [
      ['status', error.status],
      ['call', error.call],
      ['errorCode', error.errorCode],
      ['message', error.message],
    ] as const
  ).filter(([, value]) => value !== null && value !== undefined && value !== '');
  if (rows.length === 0) return null;
  return (
    <dl
      className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 rounded-md border bg-muted/40 p-2 text-xs"
      data-testid="salesforce-error"
      aria-label={t('sync.errorDetails.title')}
    >
      {rows.map(([key, value]) => (
        <div key={key} className="contents">
          <dt className="text-muted-foreground">{t(`sync.errorDetails.${key}`)}</dt>
          <dd className="min-w-0 [overflow-wrap:anywhere]">
            <bdi dir="ltr" className="font-mono">
              {String(value)}
            </bdi>
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** A server message with, when it carries one, what Salesforce answered. */
export function ServerMessage({
  messageKey,
  params,
}: {
  messageKey: string | null | undefined;
  params?: Record<string, unknown> | null;
}) {
  const message = useMessage();
  const error = salesforceErrorOf(params);
  return (
    <span className="flex min-w-0 flex-col gap-1">
      <span>{message(messageKey, params)}</span>
      {error && <SalesforceErrorDetails error={error} />}
    </span>
  );
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
