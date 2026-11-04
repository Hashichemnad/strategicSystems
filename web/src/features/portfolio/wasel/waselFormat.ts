/** API ratios (1.0 = 100 %) as a localised percentage. */
export function percent(ratio: number | null | undefined, language: string): string {
  if (ratio === null || ratio === undefined) return '—';
  return new Intl.NumberFormat(language === 'ar' ? 'ar' : 'en', {
    style: 'percent',
    maximumFractionDigits: 2,
  }).format(ratio);
}
