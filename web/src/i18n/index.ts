import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import arAdmin from './ar/admin.json';
import enAdmin from './en/admin.json';
import arAuth from './ar/auth.json';
import enAuth from './en/auth.json';
import arIdentity from './ar/identity.json';
import enIdentity from './en/identity.json';
import arImport from './ar/import.json';
import enImport from './en/import.json';
import arOrg from './ar/org.json';
import enOrg from './en/org.json';
import arProfile from './ar/profile.json';
import enProfile from './en/profile.json';
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
    auth: arAuth,
    profile: arProfile,
    org: arOrg,
    import: arImport,
    identity: arIdentity,
    admin: arAdmin,
  },
  en: {
    common: enCommon,
    shell: enShell,
    errors: enErrors,
    validation: enValidation,
    sample: enSample,
    uiKit: enUiKit,
    auth: enAuth,
    profile: enProfile,
    org: enOrg,
    import: enImport,
    identity: enIdentity,
    admin: enAdmin,
  },
} as const;

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (languages as readonly string[]).includes(value);
}

/** Stored choice, else the browser's language, else Arabic. Signed in, the profile's language wins (AppShell). */
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
