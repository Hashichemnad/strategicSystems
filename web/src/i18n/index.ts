import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import arCommon from './ar/common.json';
import arErrors from './ar/errors.json';
import arSample from './ar/sample.json';
import arShell from './ar/shell.json';
import arUiKit from './ar/uiKit.json';
import arValidation from './ar/validation.json';
import enCommon from './en/common.json';
import enErrors from './en/errors.json';
import enSample from './en/sample.json';
import enShell from './en/shell.json';
import enUiKit from './en/uiKit.json';
import enValidation from './en/validation.json';
import { readPreference } from '@/lib/preferences';

export const languages = ['ar', 'en'] as const;
export type Language = (typeof languages)[number];

export const resources = {
  ar: {
    common: arCommon,
    shell: arShell,
    errors: arErrors,
    validation: arValidation,
    sample: arSample,
    uiKit: arUiKit,
  },
  en: {
    common: enCommon,
    shell: enShell,
    errors: enErrors,
    validation: enValidation,
    sample: enSample,
    uiKit: enUiKit,
  },
} as const;

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (languages as readonly string[]).includes(value);
}

/** Stored choice, else the browser's language, else Arabic. Phase 1 moves the choice to the user's profile. */
export function initialLanguage(): Language {
  const stored = readPreference('language');
  if (isLanguage(stored)) return stored;
  return navigator.language.toLowerCase().startsWith('en') ? 'en' : 'ar';
}

/** Mirrors the active language onto <html lang dir> so the whole layout flips instantly. */
export function applyDocumentLanguage(language: string): void {
  document.documentElement.lang = language;
  document.documentElement.dir = i18n.dir(language);
}

void i18n.use(initReactI18next).init({
  resources,
  lng: initialLanguage(),
  fallbackLng: { ar: ['en'], default: ['en'] },
  supportedLngs: languages,
  defaultNS: 'common',
  ns: Object.keys(resources.en),
  interpolation: { escapeValue: false },
  returnNull: false,
});

applyDocumentLanguage(i18n.language);
i18n.on('languageChanged', applyDocumentLanguage);

export default i18n;
