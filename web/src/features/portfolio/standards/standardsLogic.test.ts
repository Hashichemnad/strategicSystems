import { describe, expect, it } from 'vitest';
import { suggestionPayload } from './CharterAssistantDialog';
import {
  averageRating,
  canSignOff,
  dayVarianceTone,
  deltaTone,
  isBelowOne,
  isOverdue,
  matrixGrid,
  parseNumber,
  parseSprints,
  qualityTone,
  raciAreasWithoutSingleA,
  ragCode,
  ragTone,
  riskBand,
  signed,
} from './standardsLogic';

describe('RAG', () => {
  it.each([
    ['green', 'success'],
    ['amber', 'warning'],
    ['red', 'danger'],
    ['grey', 'neutral'],
    [null, 'neutral'],
    ['purple', 'neutral'],
  ])('%s → %s', (value, tone) => {
    expect(ragTone(value)).toBe(tone);
  });

  it('reads unknown values as grey', () => {
    expect(ragCode('amber')).toBe('amber');
    expect(ragCode('none')).toBe('grey');
    expect(ragCode(undefined)).toBe('grey');
  });
});

describe('variance colouring', () => {
  it.each([
    [null, 'neutral'],
    [0, 'neutral'],
    [-3, 'success'],
    [1, 'warning'],
    [14, 'warning'],
    [15, 'danger'],
  ])('%s days → %s', (days, tone) => {
    expect(dayVarianceTone(days)).toBe(tone);
  });

  it('colours weight and cost deltas', () => {
    expect(deltaTone(0)).toBe('neutral');
    expect(deltaTone(0.00001)).toBe('neutral');
    expect(deltaTone(0.05)).toBe('warning');
    expect(deltaTone(-100)).toBe('info');
    expect(deltaTone(null)).toBe('neutral');
  });

  it('signs differences with a typographic minus', () => {
    const f = (n: number) => String(n);
    expect(signed(3, f)).toBe('+3');
    expect(signed(-2, f)).toBe('−2');
    expect(signed(0, f)).toBe('0');
  });
});

describe('risk matrix', () => {
  it.each([
    [1, 'low'],
    [4, 'low'],
    [5, 'medium'],
    [9, 'medium'],
    [10, 'high'],
    [16, 'high'],
    [17, 'critical'],
    [25, 'critical'],
  ])('score %s is %s (server DefaultRiskScoring bands)', (score, band) => {
    expect(riskBand(score)).toBe(band);
  });

  it('builds 5 × 5 with probability 5 on top and impact 1 first', () => {
    const grid = matrixGrid([{ probability: 4, impact: 2, count: 3, riskIds: ['a', 'b', 'c'] }]);
    expect(grid.map((r) => r.probability)).toEqual([5, 4, 3, 2, 1]);
    expect(grid[0]?.cells.map((c) => c.impact)).toEqual([1, 2, 3, 4, 5]);
    const cell = grid[1]?.cells[1];
    expect(cell).toMatchObject({ probability: 4, impact: 2, count: 3, band: 'medium' });
    expect(grid[0]?.cells[4]).toMatchObject({ count: 0, band: 'critical', riskIds: [] });
    expect(grid[4]?.cells[0]?.band).toBe('low');
  });
});

describe('earned value flags', () => {
  it('highlights SPI / CPI below 1 only', () => {
    expect(isBelowOne(0.75)).toBe(true);
    expect(isBelowOne(1)).toBe(false);
    expect(isBelowOne(1.2)).toBe(false);
    expect(isBelowOne(null)).toBe(false);
  });
});

describe('RACI', () => {
  it('finds areas without exactly one Accountable', () => {
    const result = raciAreasWithoutSingleA([
      { area: 'Design', role: 'A' },
      { area: 'Design', role: 'R' },
      { area: 'Build', role: 'R' },
      { area: 'Test', role: 'A' },
      { area: 'Test', role: 'A' },
    ]);
    expect(result).toEqual({ missing: ['Build'], multiple: ['Test'] });
  });

  it('is clean for an empty matrix', () => {
    expect(raciAreasWithoutSingleA([])).toEqual({ missing: [], multiple: [] });
  });
});

describe('communication, benefits, procurement, quality', () => {
  it('overdue when the next due date has passed', () => {
    expect(isOverdue('2026-11-03', '2026-11-04')).toBe(true);
    expect(isOverdue('2026-11-04', '2026-11-04')).toBe(false);
    expect(isOverdue(null, '2026-11-04')).toBe(false);
  });

  it('averages a complete supplier rating', () => {
    expect(averageRating([5, 4, 3])).toBe(4);
    expect(averageRating([5, 4, 4])).toBe(4.33);
    expect(averageRating([5, null, 4])).toBeNull();
  });

  it('lets the owner or a manager sign off', () => {
    expect(canSignOff(true, null, null)).toBe(true);
    expect(canSignOff(false, 'e1', 'e1')).toBe(true);
    expect(canSignOff(false, 'e1', 'e2')).toBe(false);
    expect(canSignOff(false, null, null)).toBe(false);
  });

  it('colours quality status', () => {
    expect(qualityTone('accepted')).toBe('success');
    expect(qualityTone('rejected')).toBe('danger');
    expect(qualityTone(null)).toBe('warning');
  });

  it('parses typed numbers', () => {
    expect(parseNumber(' 12.5 ')).toBe(12.5);
    expect(parseNumber('3,5')).toBe(3.5);
    expect(parseNumber('')).toBeNull();
    expect(parseNumber('abc')).toBeNull();
  });
});

describe('monthly sprints roll-up', () => {
  it('reads camelCase and PascalCase snapshots, ignores bad JSON', () => {
    expect(
      parseSprints(
        '[{"number":3,"name":"S3","startDate":"2026-10-01","endDate":"2026-10-14","committedPoints":20,"completedPoints":18}]',
      ),
    ).toEqual([
      {
        number: 3,
        name: 'S3',
        startDate: '2026-10-01',
        endDate: '2026-10-14',
        committedPoints: 20,
        completedPoints: 18,
      },
    ]);
    expect(parseSprints('[{"Number":1,"CommittedPoints":5,"CompletedPoints":5}]')[0]).toMatchObject({
      number: 1,
      committedPoints: 5,
      completedPoints: 5,
    });
    expect(parseSprints('not json')).toEqual([]);
    expect(parseSprints('{}')).toEqual([]);
    expect(parseSprints(null)).toEqual([]);
  });
});

describe('charter assistant payloads', () => {
  const options = { stakeholderClasses: [{ code: 'internal', labelAr: 'داخلي', labelEn: 'Internal' }] };

  it('creates a new KPI like the KPIs tab', () => {
    expect(
      suggestionPayload('kpis', { nameAr: 'م', nameEn: 'K', equation: 'a/b', target: 90 }, options as never),
    ).toMatchObject({ nature: 'new', nameEn: 'K', equation: 'a/b', target: 90, direction: 'increase' });
  });

  it('clamps risk scores to 1–5 and sets the response when there is a mitigation', () => {
    expect(
      suggestionPayload(
        'risks',
        { title: 'R', probability: 7, impact: 0, mitigation: 'm' },
        options as never,
      ),
    ).toMatchObject({ title: 'R', probability: 5, impact: 1, response: 'mitigate', mitigationActions: 'm' });
  });

  it('keeps only known stakeholder classes and levels', () => {
    expect(
      suggestionPayload(
        'stakeholders',
        { name: 'S', classification: 'customer', interest: 'high', impact: 'huge' },
        options as never,
      ),
    ).toMatchObject({ name: 'S', classificationCode: null, interestLevel: 'high', impactLevel: null });
    expect(
      suggestionPayload(
        'stakeholders',
        { name: 'S', classification: 'internal', interest: 'low', impact: 'medium' },
        options as never,
      ),
    ).toMatchObject({ classificationCode: 'internal' });
  });

  it('passes planning items through', () => {
    expect(
      suggestionPayload('planningItems', { category: 'in-scope', description: 'd' }, options as never),
    ).toEqual({ category: 'in-scope', description: 'd' });
  });
});
