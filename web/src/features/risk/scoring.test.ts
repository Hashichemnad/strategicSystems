import { describe, expect, it } from 'vitest';
import { mapBand, previewRating, previewScore, textOn } from './scoring';

const bands = [
  { code: 'low', min: 1, max: 4 },
  { code: 'medium', min: 5, max: 9 },
  { code: 'high', min: 10, max: 16 },
  { code: 'critical', min: 20, max: 25 },
];

describe('risk scoring preview', () => {
  it.each([
    ['multiplicative', 1, 1, 1],
    ['multiplicative', 3, 4, 12],
    ['multiplicative', 5, 5, 25],
    ['additive', 1, 1, 2],
    ['additive', 3, 4, 7],
    ['additive', 5, 5, 10],
  ])('%s L=%i I=%i → %f', (method, l, i, score) => {
    expect(previewScore(method, l, i)).toBe(score);
  });

  it('leaves a custom formula to the server', () => {
    expect(previewScore('custom', 3, 3)).toBeNull();
    expect(previewRating('custom', bands, 3, 3)).toBeNull();
  });

  it.each([
    [1, 'low'],
    [4, 'low'],
    [4.5, 'medium'],
    [9, 'medium'],
    [16, 'high'],
    [17, 'critical'],
    [19.99, 'critical'],
    [20, 'critical'],
    [0, 'low'],
    [30, 'critical'],
  ])('maps %f to %s (gaps → the higher band)', (score, code) => {
    expect(mapBand(bands, score)?.code).toBe(code);
  });

  it('maps bands given in any order', () => {
    expect(mapBand([...bands].reverse(), 12)?.code).toBe('high');
  });

  it('rates a cell with score and band', () => {
    expect(previewRating('multiplicative', bands, 4, 5)).toEqual({ score: 20, band: bands[3] });
    expect(previewRating('multiplicative', bands, null, 5)).toBeNull();
  });

  it('picks a readable text colour', () => {
    expect(textOn('#ffff00')).toBe('#111827');
    expect(textOn('#7f1d1d')).toBe('#ffffff');
    expect(textOn('red')).toBe('inherit');
    // Mid-tone orange: white gives 2.8:1, dark text 6.2:1 (WCAG 1.4.3).
    expect(textOn('#f97316')).toBe('#111827');
    expect(textOn('#16a34a')).toBe('#111827');
    expect(textOn('#1d4ed8')).toBe('#ffffff');
  });
});
