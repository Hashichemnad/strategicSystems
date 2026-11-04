import { describe, expect, it } from 'vitest';
import {
  addYears,
  daysUntil,
  isEmiratesIdInputValid,
  sharesAreValid,
  sharesTotal,
  stageOf,
  proposeExpiry,
} from './logic';

describe('IP rules', () => {
  it.each([
    ['', true],
    ['784-****-*******-9', true],
    ['784-1990-1234567-1', true],
    ['784199012345671', false],
    ['785-1990-1234567-1', false],
    ['784-1990-123456-1', false],
  ])('Emirates ID input %s is valid: %s', (value, valid) => {
    expect(isEmiratesIdInputValid(value)).toBe(valid);
  });

  it('totals shares to 100', () => {
    expect(sharesTotal([33.33, 33.33, 33.34])).toBe(100);
    expect(sharesAreValid([50, 50])).toBe(true);
    expect(sharesAreValid([50, 40])).toBe(false);
    expect(sharesAreValid([])).toBe(false);
  });

  it.each([
    ['submitted', 'review'],
    ['under_review', 'review'],
    ['returned', 'review'],
    ['approved_to_file', 'approved'],
    ['filed', 'filed'],
    ['rejected_by_moet', 'filed'],
    ['registered', 'registered'],
    ['expired', 'registered'],
    ['renewed', 'renewal'],
  ])('status %s is at stage %s', (status, stage) => {
    expect(stageOf(status)).toBe(stage);
  });

  it('proposes registration date + term', () => {
    // Copyright: 50 years from the first publication; patents and utility models from the registration date.
    expect(proposeExpiry(50, 'publication', '2026-03-01', '2025-06-15')).toBe('2075-06-15');
    expect(proposeExpiry(50, 'publication', '2026-03-01', undefined)).toBeUndefined();
    expect(proposeExpiry(10, 'registration', '2026-02-01', '2025-06-15')).toBe('2036-02-01');
    expect(proposeExpiry(null, 'registration', '2026-02-01', undefined)).toBeUndefined();
    expect(addYears('2026-03-15', 20)).toBe('2046-03-15');
    expect(addYears('2024-02-29', 1)).toBe('2025-02-28');
    expect(daysUntil('2026-11-14', '2026-11-04')).toBe(10);
  });
});
