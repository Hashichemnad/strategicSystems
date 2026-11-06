import { formatDateTime, formatNumber } from '@/lib/format';
import type { Tone } from '@/ui';

/** Status codes of the Health page (ADR 0151). */
export type HealthStatus = 'ok' | 'warning' | 'error' | 'notConfigured';

export const statusTone: Record<string, Tone> = {
  ok: 'success',
  warning: 'warning',
  error: 'danger',
  notConfigured: 'neutral',
};

const isoDate = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/;
const units = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/** Bytes as a short size (1 decimal) with a unit that reads the same in both languages. */
export function formatBytes(bytes: number, language: string): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const rounded = Math.round(value * 10) / 10;
  return `${formatNumber(rounded, language)} ${units[unit]}`;
}

/**
 * Server params are invariant strings (ADR 0151): ISO dates become local date-times, `*Bytes` become sizes, other numbers are formatted
 * for the language, everything else is shown as sent (codes, host names, versions).
 */
export function formatParams(
  params: Record<string, string> | null | undefined,
  language: string,
): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(params ?? {})) {
    if (key.endsWith('Bytes') && /^\d+$/.test(value)) result[key] = formatBytes(Number(value), language);
    else if (isoDate.test(value)) result[key] = formatDateTime(value, language);
    else if (/^-?\d+(\.\d+)?$/.test(value) && key !== 'port' && key !== 'version')
      result[key] = formatNumber(Number(value), language);
    else result[key] = value;
  }
  return result;
}

/** `health.database.ok` → `database.ok` inside the `health` namespace. */
export function messagePath(messageKey: string): string {
  return messageKey.startsWith('health.') ? messageKey.slice('health.'.length) : messageKey;
}
