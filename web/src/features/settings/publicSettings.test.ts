import i18n from 'i18next';
import { describe, expect, it } from 'vitest';
import '@/i18n';
import { formatNumber, setFormatDefaults } from '@/lib/format';
import { applyTranslationOverrides } from './publicSettings';

describe('translation overrides', () => {
  it('change a label in both languages and go back to the bundled text when cleared', () => {
    applyTranslationOverrides(i18n, {
      languages: { en: { shell: { 'nav.home': 'Home base' } }, ar: { shell: { 'nav.home': 'الرئيسية' } } },
      version: 1,
    });
    expect(i18n.t('shell:nav.home', { lng: 'en' })).toBe('Home base');
    expect(i18n.t('shell:nav.home', { lng: 'ar' })).toBe('الرئيسية');

    applyTranslationOverrides(i18n, { languages: {}, version: 2 });
    expect(i18n.t('shell:nav.home', { lng: 'en' })).toBe('My Work');
  });
});

describe('regional defaults', () => {
  it('apply Arabic-Indic digits to every format call without options', () => {
    setFormatDefaults({ arabicIndicDigits: true });
    expect(formatNumber(123, 'ar')).toBe('١٢٣');
    expect(formatNumber(123, 'en')).toBe('123');
    setFormatDefaults({});
    expect(formatNumber(123, 'ar')).toBe('123');
  });
});
