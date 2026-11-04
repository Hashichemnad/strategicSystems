import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/portfolio.json';
import en from '@/i18n/en/portfolio.json';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Portfolio Management can return to the SPA: errors, current status, actions, My Work (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(portfolio\.(?:errors|currentStatus|myWork|actions|warnings)\.[A-Za-z0-9.]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Portfolio/Strategy.Modules.Portfolio');
  // Project banner keys are composed at run time ($"portfolio.currentStatus.project.{key}").
  for (const key of [
    'takeOwnership',
    'fillMinimums',
    'submitCharter',
    'charterReturned',
    'waitingApproval',
    'waitingLpo',
    'waitingTeam',
    'updatePlanning',
    'addMilestones',
    'submitPlanning',
    'planningReturned',
    'noWindow',
    'monthlyOpen',
    'monthlyReturned',
    'confirmCompletion',
    'submitClosure',
    'closureReturned',
    'enterImpact',
    'impactReturned',
    'onHold',
    'cancelled',
    'completed',
  ])
    keys.add(`portfolio.currentStatus.project.${key}`);
  return [...keys].sort();
}

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

describe('Portfolio message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('portfolio.errors.yearCostsMismatch');
    expect(keys).toContain('portfolio.currentStatus.waitingQuotations');
    expect(keys).toContain('portfolio.myWork.returned');
    expect(keys).toContain('portfolio.errors.weightsNot100');
    expect(keys).toContain('portfolio.warnings.openTasks');
    expect(keys).toContain('portfolio.myWork.monthlyDue');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, {
          total: '1.00',
          budget: '2.00',
          from: 'a',
          to: 'b',
          step: 'x',
          project: 'P',
          reason: 'r',
          weight: '20',
          approved: '10',
          min: 3,
          count: 2,
          period: '2027-03',
          process: 'Charter',
        }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });

  it.each(['en', 'ar'] as const)('names every request status and stage in %s', async (language) => {
    await i18n.changeLanguage(language);
    for (const status of ['draft', 'in-approval', 'approved', 'rejected', 'cancelled'])
      expect(dynamicT(`portfolio:status.${status}`)).not.toContain('status.');
    for (const stage of ['initiation', 'planning', 'execution', 'closure', 'impact', 'completed'])
      expect(dynamicT(`portfolio:projectStages.${stage}`)).not.toContain('projectStages.');
  });

  it('translates the planning weight gate with its total in both languages', async () => {
    await i18n.changeLanguage('en');
    expect(translateMessageKey(i18n.t, 'portfolio.errors.weightsNot100', { total: 90 })).toBe(
      'Milestone weights must total exactly 100 % (now 90 %).',
    );
    await i18n.changeLanguage('ar');
    expect(translateMessageKey(i18n.t, 'portfolio.errors.weightsNot100', { total: 90 })).toContain('100 %');
  });

  it('translates the Year 1–4 mismatch with its totals in both languages', async () => {
    await i18n.changeLanguage('en');
    expect(
      translateMessageKey(i18n.t, 'portfolio.errors.yearCostsMismatch', {
        total: '100001.00',
        budget: '100000.00',
      }),
    ).toBe('Year 1–4 costs (100001.00) must equal the estimated budget (100000.00).');
    await i18n.changeLanguage('ar');
    expect(
      translateMessageKey(i18n.t, 'portfolio.errors.yearCostsMismatch', {
        total: '100001.00',
        budget: '100000.00',
      }),
    ).toContain('الميزانية التقديرية');
  });
});
