import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import ar from '@/i18n/ar/vendors.json';
import en from '@/i18n/en/vendors.json';
import { renderWithProviders } from '@/test/render';
import { visibleTabs } from '../projectApi';
import { DiffList } from './DiffList';
import { compareRows, isEdited, type PmSubmissionItem } from './waselApi';
import { percent } from './waselFormat';

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

const item: PmSubmissionItem = {
  id: 'i1',
  milestoneId: 'm1',
  code: 'P-1.1',
  titleAr: 'التصميم',
  titleEn: 'Design',
  weight: 0.6,
  plannedStart: '2026-01-01',
  plannedEnd: '2026-03-31',
  previous: { approved: 0.1, actualStart: '2026-01-05', actualEnd: null },
  vendor: {
    achieve: 0.3,
    actualStart: '2026-01-05',
    actualEnd: null,
    progressNote: 'Half done',
    causality: null,
    correctiveActions: null,
  },
  final: null,
  reviewStatus: 'pending',
  diff: [],
  pmComment: null,
  evidence: [],
  flags: [],
  version: 3,
};

describe('Wasel PM review (side by side)', () => {
  it('compares last approved, vendor and PM values per field', () => {
    const same = compareRows(item, { ...item.vendor });
    expect(same.map((r) => r.field)).toEqual([
      'achieve',
      'actualStart',
      'actualEnd',
      'causality',
      'correctiveActions',
    ]);
    expect(same.find((r) => r.field === 'achieve')).toMatchObject({
      previous: 0.1,
      vendor: 0.3,
      pm: 0.3,
      changed: false,
    });
    expect(isEdited(item, { ...item.vendor })).toBe(false);

    const edited = compareRows(item, { ...item.vendor, achieve: 0.25, causality: '  ' });
    expect(edited.find((r) => r.field === 'achieve')?.changed).toBe(true);
    expect(edited.find((r) => r.field === 'causality')?.changed).toBe(false); // blank equals empty
    expect(isEdited(item, { ...item.vendor, achieve: 0.25 })).toBe(true);
  });

  it.each(['en', 'ar'] as const)('renders the vendor → PM diff in %s', async (language) => {
    await renderWithProviders(
      <DiffList
        entries={[
          { field: 'achieve', vendor: '0.3', pm: '0.25' },
          { key: 'm3', field: 'added', vendor: null, pm: 'Test' },
          { key: 'm2', field: 'removed', vendor: 'Build', pm: null },
        ]}
      />,
      { language },
    );
    const list = screen.getByTestId('wasel-diff');
    const [first, second, third, ...rest] = within(list).getAllByRole('listitem');
    if (!first || !second || !third) throw new Error('three rows expected');
    expect(rest).toHaveLength(0);
    expect(first).toHaveTextContent(language === 'en' ? 'Achievement' : 'الإنجاز');
    expect(within(first).getByText('0.3').tagName).toBe('DEL');
    expect(within(first).getByText('0.25').tagName).toBe('INS');
    expect(within(second).getByText('Test').tagName).toBe('INS');
    expect(within(third).getByText('Build').tagName).toBe('DEL');
  });

  it('formats ratios as percentages in both languages', () => {
    expect(percent(0.25, 'en')).toBe('25%');
    expect(percent(null, 'en')).toBe('—');
    expect(percent(1, 'ar')).toContain('100');
  });

  it('shows the Vendors tab on every project', () => {
    expect(
      visibleTabs({ charter: true, lpo: true, teamFormation: true, closure: true, impact: true }),
    ).toContain('vendors');
    expect(
      visibleTabs({ charter: false, lpo: false, teamFormation: false, closure: false, impact: false }),
    ).toContain('vendors');
  });
});

describe('vendors translations', () => {
  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });

  it('names every status, role, mode and flag', () => {
    for (const key of ['active', 'suspended', 'blacklisted']) expect(en.status).toHaveProperty(key);
    for (const key of ['invited', 'active', 'disabled', 'dormant']) expect(en.userStatus).toHaveProperty(key);
    for (const key of ['vendor-lead', 'vendor-contributor']) expect(en.roles).toHaveProperty(key);
    for (const key of ['pm-only', 'vendor-then-pm']) expect(en.modes).toHaveProperty(key);
    for (const key of ['draft', 'submitted', 'returned', 'accepted', 'included'])
      expect(en.submission.status).toHaveProperty(key);
    for (const key of [
      'completeWithoutEvidence',
      'progressJump',
      'dateOutsideContract',
      'endBeforeStart',
      'startBeforePlan',
      'delayedWithoutCausality',
      'endInFuture',
    ])
      expect(en.flags).toHaveProperty(key);
  });
});
