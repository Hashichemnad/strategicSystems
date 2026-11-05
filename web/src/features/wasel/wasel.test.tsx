import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import ar from '@/i18n/ar/wasel.json';
import en from '@/i18n/en/wasel.json';
import { renderWithProviders } from '@/test/render';
import { ValueDiff } from './WaselMonthlyTab';
import { ProposalDiff } from './WaselPlanTab';
import {
  diffValue,
  nextKey,
  parseTasks,
  percentToRatio,
  proposalDiffKind,
  proposedWeight,
  ratioToPercent,
} from './waselLogic';

function keys(node: unknown, prefix = ''): string[] {
  if (typeof node !== 'object' || node === null) return [prefix];
  return Object.entries(node).flatMap(([k, v]) => keys(v, prefix ? `${prefix}.${k}` : k));
}

describe('Wasel translations', () => {
  it('has every key in Arabic and English', () => {
    expect(keys(ar).sort()).toEqual(keys(en).sort());
    expect(keys(en).length).toBeGreaterThan(150);
  });
});

describe('Wasel logic', () => {
  it('converts project ratios to percent points and back', () => {
    expect(ratioToPercent(0.25)).toBe('25');
    expect(ratioToPercent(0.3333)).toBe('33.33');
    expect(ratioToPercent(null)).toBe('');
    expect(percentToRatio('25')).toBe(0.25);
    expect(percentToRatio('12,5')).toBe(0.125);
    expect(percentToRatio('x')).toBe(0);
  });

  it('formats diff values (ratios as percent) and classifies proposal diffs', () => {
    expect(diffValue('achieve', '0.25', 'en')).toBe('25%');
    expect(diffValue('actualStart', '2026-11-01', 'en')).toBe('2026-11-01');
    expect(diffValue('causality', null, 'en')).toBe('—');
    expect(proposalDiffKind({ key: 'm1', field: 'added', vendor: null, pm: 'X' })).toBe('added');
    expect(proposalDiffKind({ key: 'm1', field: 'removed', vendor: 'X', pm: null })).toBe('removed');
    expect(proposalDiffKind({ key: 'm1', field: 'titleEn', vendor: 'A', pm: 'B' })).toBe('changed');
  });

  it('builds proposal rows: keys, weights and tasks', () => {
    const rows = [
      {
        key: 'm1',
        type: 'milestone',
        parentKey: null,
        titleAr: null,
        titleEn: 'A',
        plannedStart: null,
        plannedEnd: null,
        weight: 0.6,
        isDeliverable: false,
      },
      {
        key: 'p1',
        type: 'phase',
        parentKey: null,
        titleAr: null,
        titleEn: 'P',
        plannedStart: null,
        plannedEnd: null,
        weight: null,
        isDeliverable: false,
      },
      {
        key: 'm2',
        type: 'milestone',
        parentKey: 'p1',
        titleAr: null,
        titleEn: 'B',
        plannedStart: null,
        plannedEnd: null,
        weight: 0.4,
        isDeliverable: false,
      },
    ];
    expect(nextKey(rows, 'm')).toBe('m3');
    expect(nextKey(rows, 'p')).toBe('p2');
    expect(proposedWeight(rows, 'waterfall')).toBeCloseTo(1);
    expect(proposedWeight(rows, 'agile')).toBe(0);
    expect(parseTasks(' Wireframes, , Sign-off ')).toEqual(['Wireframes', 'Sign-off']);
  });
});

describe('Wasel diff views', () => {
  it('shows the PM edit of a milestone value to the vendor (English)', async () => {
    await renderWithProviders(<ValueDiff diff={[{ field: 'achieve', vendor: '0.3', pm: '0.25' }]} />);
    expect(screen.getByTestId('wasel-value-diff')).toHaveTextContent('Achievement');
    expect(screen.getByTestId('wasel-diff-entry')).toHaveTextContent('30%');
    expect(screen.getByTestId('wasel-diff-entry')).toHaveTextContent('25%');
  });

  it('lists changed, added and removed proposal items (Arabic)', async () => {
    await renderWithProviders(
      <ProposalDiff
        diff={[
          { key: 'm1', field: 'titleEn', vendor: 'Design', pm: 'Design and sign-off' },
          { key: 'm3', field: 'added', vendor: null, pm: 'Test' },
          { key: 'm2', field: 'removed', vendor: 'Build', pm: null },
        ]}
      />,
      { language: 'ar' },
    );
    const entries = screen.getAllByTestId('wasel-diff-entry');
    expect(entries).toHaveLength(3);
    expect(entries[0]).toHaveTextContent('Design and sign-off');
    expect(entries[1]).toHaveTextContent('أضيف: Test');
    expect(entries[2]).toHaveTextContent('حُذف: Build');
  });
});
