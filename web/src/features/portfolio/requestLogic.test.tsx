import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ExtractionResult } from '@/features/ai/api';
import { renderWithProviders } from '@/test/render';
import { Wizard } from '@/ui';
import { bandOf, liveClassification, liveScore, yearTotal, type RequestValues } from './api';
import { mergeExtractions } from './RequestFormPage';
import { toForm, toValues } from './RequestWizard';

const criteria = [
  { code: 'vision-alignment', weight: 1, active: true },
  { code: 'impact', weight: 1, active: true },
  { code: 'complexity', weight: 1, active: true },
  { code: 'radical-change', weight: 1, active: true },
];
const bands = [
  { code: 'low', minimum: 0 },
  { code: 'medium', minimum: 0.6 },
  { code: 'high', minimum: 0.8 },
];

describe('request wizard calculations (mirror the server; the server decides)', () => {
  it('adds the Year 1–4 costs without floating noise', () => {
    expect(yearTotal({ year1Cost: 0.1, year2Cost: 0.2, year3Cost: null, year4Cost: undefined })).toBe(0.3);
    expect(yearTotal({ year1Cost: null, year2Cost: null, year3Cost: null, year4Cost: null })).toBeNull();
    expect(yearTotal({ year1Cost: 175000, year2Cost: 25000, year3Cost: 25000, year4Cost: 25000 })).toBe(
      250000,
    );
  });

  it('computes the evaluation score and band like Q-P6', () => {
    expect(
      liveScore({ 'vision-alignment': 4, impact: 4, complexity: 4, 'radical-change': 4 }, criteria),
    ).toBe(0.8);
    expect(bandOf(0.8, bands)).toBe('high');
    expect(
      bandOf(
        liveScore({ 'vision-alignment': 3, impact: 3, complexity: 3, 'radical-change': 2 }, criteria),
        bands,
      ),
    ).toBe('low');
    expect(liveScore({ impact: 4 }, criteria)).toBeNull();
  });

  it('classifies like Q-P5', () => {
    const yes: RequestValues = {
      requiresBudget: true,
      estimatedBudget: 50000.01,
      screenCoreWork: true,
      screenOneTime: true,
      screenTimeBound: true,
      screenUniqueOutput: true,
      screenMinCost: true,
      screenOperationalCosts: true,
    };
    expect(liveClassification(yes, 50000)).toBe('project');
    expect(liveClassification({ ...yes, estimatedBudget: 50000 }, 50000)).toBe('initiative');
    expect(liveClassification({ ...yes, screenOneTime: false }, 50000)).toBe('initiative');
  });

  it('round-trips form text and server values', () => {
    const values = toValues(
      {
        nameEn: ' Smart ',
        estimatedBudget: '250,000',
        requiresBudget: 'yes',
        screenCoreWork: 'no',
        'priority:impact': '4',
        partnerShare: '12.5',
      },
      'unit-1',
    );
    expect(values).toMatchObject({
      nameEn: 'Smart',
      estimatedBudget: 250000,
      requiresBudget: true,
      screenCoreWork: false,
      priorityScores: { impact: 4 },
      partnerShare: 0.125,
      unitId: 'unit-1',
    });
    expect(toForm(values)).toMatchObject({
      nameEn: 'Smart',
      requiresBudget: 'yes',
      screenCoreWork: 'no',
      'priority:impact': '4',
      partnerShare: '12.5',
    });
  });
});

describe('start from several documents', () => {
  const result = (file: string, fields: [string, string, number][]): ExtractionResult => ({
    formKey: 'portfolio.request',
    fileName: file,
    documentKind: 'docx',
    truncated: false,
    language: 'en',
    warnings: [],
    usageId: 'u',
    scannedPages: 0,
    fields: fields.map(([name, value, confidence]) => ({
      name,
      value,
      confidence,
      source: `${name}: ${value}`,
      sourceVerified: true,
      locationKind: 'page',
      locationNumber: 1,
      fromScan: false,
    })),
  });

  it('keeps the most confident value per field, every one a draft to review', () => {
    const merged = mergeExtractions([
      result('a.docx', [
        ['nameEn', 'From A', 0.6],
        ['estimatedBudget', '100', 0.9],
        ['priority:impact', '9', 0.9],
      ]),
      result('b.docx', [
        ['nameEn', 'From B', 0.8],
        ['priority:complexity', '3.4', 0.7],
      ]),
    ]);
    expect(merged.form).toEqual({ nameEn: 'From B', estimatedBudget: '100', 'priority:complexity': '3' });
    expect(Object.values(merged.drafts).every((d) => !d.accepted)).toBe(true);
  });
});

describe('wizard', () => {
  it('cannot finish or save while AI values wait for review, but the steps stay open', async () => {
    const finish = vi.fn();
    await renderWithProviders(
      <Wizard
        steps={[{ key: 'one', title: 'One', content: <p>step</p> }]}
        onFinish={finish}
        onSaveDraft={vi.fn()}
        finishDisabled
        finishLabel="Create draft"
      />,
    );
    expect(screen.getByRole('button', { name: 'Create draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save as draft' })).toBeDisabled();
    expect(finish).not.toHaveBeenCalled();
  });
});
