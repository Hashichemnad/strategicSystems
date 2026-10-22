/**
 * Locale-aware formatting through Intl only (CLAUDE.md §10). Latin digits by default; Arabic-Indic digits and
 * Hijri display become Settings toggles in Phase 2 (`numberingSystem` / `calendar` below).
 */
export interface FormatOptions {
  arabicIndicDigits?: boolean;
  hijri?: boolean;
}

function locale(language: string, options: FormatOptions = {}): string {
  const base = language === 'ar' ? 'ar-AE' : 'en-GB';
  const extensions = [
    options.hijri ? 'ca-islamic-umalqura' : 'ca-gregory',
    `nu-${language === 'ar' && options.arabicIndicDigits ? 'arab' : 'latn'}`,
  ];
  return `${base}-u-${extensions.join('-')}`;
}

export function formatNumber(value: number, language: string, options?: FormatOptions): string {
  return new Intl.NumberFormat(locale(language, options)).format(value);
}

export function formatDateTime(value: string | Date, language: string, options?: FormatOptions): string {
  return new Intl.DateTimeFormat(locale(language, options), {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function formatDate(value: string | Date, language: string, options?: FormatOptions): string {
  return new Intl.DateTimeFormat(locale(language, options), { dateStyle: 'medium' }).format(new Date(value));
}

/** Shows the user's language, falling back to the other one (bilingual `*_ar` / `*_en` fields). */
export function pickText(
  language: string,
  ar: string | null | undefined,
  en: string | null | undefined,
): string {
  const [first, second] = language === 'ar' ? [ar, en] : [en, ar];
  return first?.trim() || second?.trim() || '';
}
