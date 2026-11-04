/**
 * Client-side preview of the risk framework scoring (docs/design/risk.md §2.1). The server's RiskScoringEngine / BandMapper are the
 * source of truth; this mirrors them only to show a live score and band while the user picks a matrix cell.
 */

export interface BandLike {
  code: string;
  min: number;
  max: number;
}

/**
 * Score for a likelihood / impact pair: multiplicative `L × I`, additive `L + I`. A custom formula is evaluated by the server's
 * sandbox only, so it returns null here ("computed on save").
 */
export function previewScore(method: string, likelihood: number, impact: number): number | null {
  if (!Number.isFinite(likelihood) || !Number.isFinite(impact)) return null;
  if (method === 'multiplicative') return round2(likelihood * impact);
  if (method === 'additive') return round2(likelihood + impact);
  return null;
}

/**
 * The band whose [min, max] holds the score; a score in a gap between two bands maps to the higher band (ADR 0100), below the first
 * band → the first, above the last → the last.
 */
export function mapBand<T extends BandLike>(bands: readonly T[], score: number): T | undefined {
  const ordered = [...bands].sort((a, b) => a.min - b.min);
  for (const band of ordered) if (score <= band.max) return band;
  return ordered[ordered.length - 1];
}

/** Score and band for a cell, or null when the method needs the server. */
export function previewRating<T extends BandLike>(
  method: string,
  bands: readonly T[],
  likelihood: number | null | undefined,
  impact: number | null | undefined,
): { score: number; band: T | undefined } | null {
  if (likelihood == null || impact == null) return null;
  const score = previewScore(method, likelihood, impact);
  if (score == null) return null;
  return { score, band: mapBand(bands, score) };
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

/** Readable text colour (dark or light) on a band colour such as `#f59e0b`. */
export function textOn(color: string | null | undefined): string {
  const hex = (color ?? '').replace('#', '');
  if (!/^[0-9a-f]{6}$/i.test(hex)) return 'inherit';
  const r = parseInt(hex.slice(0, 2), 16);
  const g = parseInt(hex.slice(2, 4), 16);
  const b = parseInt(hex.slice(4, 6), 16);
  return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? '#111827' : '#ffffff';
}
