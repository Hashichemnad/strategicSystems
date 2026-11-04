/** Pure rules of the IP screens (unit-tested in logic.test.ts). The server re-checks every one of them. */

export const EMIRATES_ID_PATTERN = /^784-\d{4}-\d{7}-\d$/;

/** Empty, the stored mask (kept as is) or a full Emirates ID in the 784-YYYY-NNNNNNN-N format. */
export function isEmiratesIdInputValid(value: string | null | undefined): boolean {
  const v = (value ?? '').trim();
  return v === '' || v.includes('*') || EMIRATES_ID_PATTERN.test(v);
}

/** Shares are percentages and must total 100 (the server allows ±0.01). */
export function sharesTotal(shares: readonly number[]): number {
  return Math.round(shares.reduce((sum, share) => sum + (Number.isFinite(share) ? share : 0), 0) * 100) / 100;
}

export function sharesAreValid(shares: readonly number[]): boolean {
  return shares.length > 0 && Math.abs(sharesTotal(shares) - 100) <= 0.01;
}

/** Stage of the IP lifecycle stepper for a status. */
export const ipStageKeys = ['review', 'approved', 'filed', 'registered', 'renewal'] as const;
export type IpStage = (typeof ipStageKeys)[number];

export function stageOf(status: string): IpStage {
  switch (status) {
    case 'approved_to_file':
      return 'approved';
    case 'filed':
    case 'rejected_by_moet':
      return 'filed';
    case 'registered':
    case 'expired':
      return 'registered';
    case 'renewed':
      return 'renewal';
    default:
      return 'review';
  }
}

/** Calendar date `YYYY-MM-DD` of today in the browser. */
export function todayIso(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** Adds whole years to a `YYYY-MM-DD` date (29 February falls back to 28 February). */
export function addYears(date: string, years: number): string {
  const [y = 0, m = 1, d = 1] = date.split('-').map(Number);
  const target = y + years;
  const lastDay = new Date(Date.UTC(target, m, 0)).getUTCDate();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${String(target).padStart(4, '0')}-${pad(m)}-${pad(Math.min(d, lastDay))}`;
}

/**
 * The expiry a protection rule proposes (ADR 0087, the server's `IpRules.ProposedExpiry`): `termYears` from the registration date, or from the
 * first publication for publication-based terms (copyright). Undefined when there is no expiry or the needed date is missing.
 */
export function proposeExpiry(
  termYears: number | null | undefined,
  termFrom: string,
  registrationDate: string | undefined,
  firstPublicationDate: string | undefined,
): string | undefined {
  if (!termYears) return undefined;
  const start = termFrom === 'publication' ? firstPublicationDate : registrationDate;
  return start ? addYears(start, termYears) : undefined;
}

/** Days from `today` to `date` (both `YYYY-MM-DD`). */
export function daysUntil(date: string, today: string): number {
  return Math.round((Date.parse(`${date}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000);
}
