import { describe, expect, it } from 'vitest';
import { formatDate, formatNumber, pickText } from './format';

describe('format', () => {
  it('uses Latin digits by default and Arabic-Indic digits when enabled', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
    expect(formatNumber(1234, 'ar')).toMatch(/1.?234/);
    expect(formatNumber(12, 'ar', { arabicIndicDigits: true })).toBe('١٢');
  });

  it('formats calendar dates per language and optionally in Hijri', () => {
    expect(formatDate('2026-10-22T08:00:00Z', 'en')).toBe('22 Oct 2026');
    expect(formatDate('2026-10-22T08:00:00Z', 'ar', { hijri: true })).toMatch(/1448/);
  });

  it('picks the user language and falls back to the other one', () => {
    expect(pickText('ar', 'اسم', 'Name')).toBe('اسم');
    expect(pickText('en', 'اسم', 'Name')).toBe('Name');
    expect(pickText('en', 'اسم', '  ')).toBe('اسم');
    expect(pickText('ar', null, undefined)).toBe('');
  });
});
