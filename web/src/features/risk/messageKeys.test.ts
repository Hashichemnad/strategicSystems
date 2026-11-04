import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/risk.json';
import en from '@/i18n/en/risk.json';
import {
  actionStatuses,
  assessmentKinds,
  controlStatuses,
  kriBases,
  kriDirections,
  kriLevels,
  projectResidualRules,
  recalculationModes,
  registerViews,
  reportKinds,
  residualStatuses,
  reviewStatuses,
  riskStatuses,
  riskTypes,
  scoringMethods,
  statementFlags,
} from './api';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Risk Management can return to the SPA (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(risk\.(?:errors|currentStatus|actions|myWork)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Risk/Strategy.Modules.Risk');
  return [...keys].sort();
}

/** Key paths without plural suffixes (Arabic has more plural forms than English). */
function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null)
    return [prefix.replace(/_(zero|one|two|few|many|other)$/, '')];
  return [
    ...new Set(
      Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key)),
    ),
  ];
}

describe('Risk message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('risk.errors.invalidTransition');
    expect(keys).toContain('risk.errors.formulaCell');
    expect(keys).toContain('risk.currentStatus.reassessment');
    expect(keys).toContain('risk.actions.startReview');
    expect(keys).toContain('risk.myWork.actionOverdue');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, {
          date: '2026-04-15',
          reason: 'x',
          likelihood: 1,
          impact: 1,
          c: 0,
        }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect([...new Set(paths(ar))].sort()).toEqual([...new Set(paths(en))].sort());
  });

  it.each(['en', 'ar'] as const)('names every code shown in the UI in %s', async (language) => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['status', riskStatuses],
      ['residualStatus', residualStatuses],
      ['actionStatus', actionStatuses],
      ['controlStatus', controlStatuses],
      ['reviewStatus', reviewStatuses],
      ['kriLevel', kriLevels],
      ['kriBasis', kriBases],
      ['kriDirection', kriDirections],
      ['assessmentKind', assessmentKinds],
      ['scoringMethod', scoringMethods],
      ['scoringMethodHint', scoringMethods],
      ['projectResidualRule', projectResidualRules],
      ['projectResidualRuleHint', projectResidualRules],
      ['recalculation', recalculationModes],
      ['recalculationShort', recalculationModes],
      ['riskType', riskTypes],
      ['views', registerViews],
      ['statementFlags', statementFlags],
      ['reports.kinds', reportKinds],
      ['reports.hints', reportKinds],
      ['escalationReason', ['critical', 'outside-appetite', 'manual']],
      [
        'tabs',
        [
          'overview',
          'assessment',
          'controls',
          'actions',
          'kris',
          'events',
          'reviews',
          'documents',
          'activity',
        ],
      ],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`risk:${group}.${code}`)).not.toContain(`${group}.`);
  });
});
