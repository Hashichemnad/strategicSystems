// Pure helpers of the PM-standards screens (docs/design/portfolio.md §33–§37), unit-tested in standardsLogic.test.ts.

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

/** RAG value (green / amber / red / grey) as a chip tone; the chip always carries the text too, never colour alone. */
export function ragTone(value: string | null | undefined): Tone {
  switch (value) {
    case 'green':
      return 'success';
    case 'amber':
      return 'warning';
    case 'red':
      return 'danger';
    default:
      return 'neutral';
  }
}

/** The RAG values the server knows; anything else reads as grey. */
export function ragCode(value: string | null | undefined): 'green' | 'amber' | 'red' | 'grey' {
  return value === 'green' || value === 'amber' || value === 'red' ? value : 'grey';
}

/** Days late (positive) or early (negative) against the baseline: early or on time is fine, up to two weeks late is a warning. */
export function dayVarianceTone(days: number | null | undefined): Tone {
  if (days === null || days === undefined || days === 0) return 'neutral';
  if (days < 0) return 'success';
  return days <= 14 ? 'warning' : 'danger';
}

/** A weight / cost delta: more than the baseline is a warning, less is information. */
export function deltaTone(delta: number | null | undefined): Tone {
  if (delta === null || delta === undefined || Math.abs(delta) < 0.00005) return 'neutral';
  return delta > 0 ? 'warning' : 'info';
}

/** "+3" / "−2" / "0" for a signed difference (the minus sign is the typographic one). */
export function signed(value: number, format: (n: number) => string): string {
  if (value > 0) return `+${format(value)}`;
  if (value < 0) return `−${format(Math.abs(value))}`;
  return format(0);
}

export type RiskBand = 'low' | 'medium' | 'high' | 'critical';

/** Default project risk band of a probability × impact score (server DefaultRiskScoring: 1–4 · 5–9 · 10–16 · 17–25). */
export function riskBand(score: number): RiskBand {
  if (score > 16) return 'critical';
  if (score > 9) return 'high';
  if (score > 4) return 'medium';
  return 'low';
}

export const riskBandTone: Record<RiskBand, Tone> = {
  low: 'success',
  medium: 'info',
  high: 'warning',
  critical: 'danger',
};

export interface MatrixCellInput {
  probability: number;
  impact: number;
  count: number;
  riskIds: string[];
}

export interface MatrixCell extends MatrixCellInput {
  band: RiskBand;
}

/**
 * The 5 × 5 grid: probability rows from 5 (top) to 1, impact columns from 1 to 5 in reading order (a CSS grid mirrors them in RTL).
 * Cells the server did not send count 0.
 */
export function matrixGrid(
  cells: readonly MatrixCellInput[],
): { probability: number; cells: MatrixCell[] }[] {
  const rows = [];
  for (let probability = 5; probability >= 1; probability--) {
    const row: MatrixCell[] = [];
    for (let impact = 1; impact <= 5; impact++) {
      const found = cells.find((c) => c.probability === probability && c.impact === impact);
      row.push({
        probability,
        impact,
        count: found?.count ?? 0,
        riskIds: found?.riskIds ?? [],
        band: riskBand(probability * impact),
      });
    }
    rows.push({ probability, cells: row });
  }
  return rows;
}

/** SPI / CPI below 1 means behind schedule / over cost. */
export function isBelowOne(ratio: number | null | undefined): boolean {
  return ratio !== null && ratio !== undefined && ratio < 1;
}

/** Areas of a RACI matrix that do not have exactly one Accountable person. */
export function raciAreasWithoutSingleA(entries: readonly { area: string; role: string }[]): {
  missing: string[];
  multiple: string[];
} {
  const counts = new Map<string, number>();
  for (const e of entries) counts.set(e.area, (counts.get(e.area) ?? 0) + (e.role === 'A' ? 1 : 0));
  const missing: string[] = [];
  const multiple: string[] = [];
  for (const [area, count] of counts) {
    if (count === 0) missing.push(area);
    if (count > 1) multiple.push(area);
  }
  return { missing: missing.sort(), multiple: multiple.sort() };
}

/** A communication is overdue when its next due date has passed (ISO dates compare as text). */
export function isOverdue(nextDue: string | null | undefined, today: string): boolean {
  return Boolean(nextDue) && (nextDue ?? '') < today;
}

export const benefitTone: Record<string, Tone> = {
  planned: 'neutral',
  realised: 'success',
  partly: 'warning',
  'not-realised': 'danger',
};

export interface SprintRollup {
  number: number;
  name: string | null;
  startDate: string | null;
  endDate: string | null;
  committedPoints: number;
  completedPoints: number;
}

/** The sprints a monthly roll-up carried (MonthlyUpdateDto.sprints JSON); bad JSON reads as none. */
export function parseSprints(json: string | null | undefined): SprintRollup[] {
  if (!json) return [];
  try {
    const value = JSON.parse(json) as unknown;
    if (!Array.isArray(value)) return [];
    return value
      .filter((s): s is Record<string, unknown> => typeof s === 'object' && s !== null)
      .map((s) => ({
        number: Number(s.number ?? s.Number ?? 0),
        name: (s.name ?? s.Name ?? null) as string | null,
        startDate: (s.startDate ?? s.StartDate ?? null) as string | null,
        endDate: (s.endDate ?? s.EndDate ?? null) as string | null,
        committedPoints: Number(s.committedPoints ?? s.CommittedPoints ?? 0),
        completedPoints: Number(s.completedPoints ?? s.CompletedPoints ?? 0),
      }));
  } catch {
    return [];
  }
}

/** Overall supplier rating: the average of the three 1–5 scores (null until all are given). */
export function averageRating(scores: readonly (number | null | undefined)[]): number | null {
  if (scores.length === 0 || scores.some((s) => s === null || s === undefined)) return null;
  const sum = scores.reduce<number>((a, s) => a + (s ?? 0), 0);
  return Math.round((sum / scores.length) * 100) / 100;
}

/** Quality sign-off: the owner or anyone who manages the project (the server checks again and answers 403 otherwise). */
export function canSignOff(
  canManage: boolean,
  ownerEmployeeId: string | null | undefined,
  myEmployeeId: string | null | undefined,
): boolean {
  return canManage || (Boolean(ownerEmployeeId) && ownerEmployeeId === myEmployeeId);
}

/** A number typed by the user, or null for empty / not a number. */
export function parseNumber(text: string): number | null {
  const trimmed = text.trim().replace(',', '.');
  if (trimmed === '' || !/^-?\d+(\.\d+)?$/u.test(trimmed)) return null;
  return Number(trimmed);
}

/** Quality sign-off status of a deliverable (pending until the owner or PM Office decides). */
export function qualityTone(status: string | null | undefined): Tone {
  if (status === 'accepted') return 'success';
  if (status === 'rejected') return 'danger';
  return 'warning';
}
