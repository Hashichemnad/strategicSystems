/**
 * Locale-aware formatting through Intl only (CLAUDE.md §10). Latin digits and the Gregorian calendar by default;
 * Arabic-Indic digits, Hijri display and the time zone come from Settings → Regional ({@link setFormatDefaults}).
 */
export interface FormatOptions {
  arabicIndicDigits?: boolean;
  hijri?: boolean;
  timeZone?: string;
}

let defaults: FormatOptions = {};

/** Installation-wide regional settings, applied to every call that does not pass its own options. */
export function setFormatDefaults(options: FormatOptions): void {
  defaults = { ...options };
}

function locale(language: string, options: FormatOptions = defaults): string {
  const base = language === 'ar' ? 'ar-AE' : 'en-GB';
  const extensions = [
    options.hijri ? 'ca-islamic-umalqura' : 'ca-gregory',
    `nu-${language === 'ar' && options.arabicIndicDigits ? 'arab' : 'latn'}`,
  ];
  return `${base}-u-${extensions.join('-')}`;
}

export function formatNumber(value: number, language: string, options?: FormatOptions): string {
  return new Intl.NumberFormat(locale(language, options ?? defaults)).format(value);
}

export function formatDateTime(value: string | Date, language: string, options?: FormatOptions): string {
  const effective = options ?? defaults;
  return new Intl.DateTimeFormat(locale(language, effective), {
    dateStyle: 'medium',
    timeStyle: 'short',
    ...(effective.timeZone ? { timeZone: effective.timeZone } : {}),
  }).format(new Date(value));
}

export function formatDate(value: string | Date, language: string, options?: FormatOptions): string {
  return new Intl.DateTimeFormat(locale(language, options ?? defaults), { dateStyle: 'medium' }).format(
    new Date(value),
  );
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
