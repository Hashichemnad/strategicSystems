import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/kpi.json';
import en from '@/i18n/en/kpi.json';
import { anomalyCodes, periodCodes, ragCodes, resultStatuses, smartCodes } from './api';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Performance Management can return to the SPA (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(kpi\.(?:errors|currentStatus|actions|myWork|ai\.targets)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Kpi/Strategy.Modules.Kpi');
  return [...keys].sort();
}

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

describe('KPI message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('kpi.errors.checklistRequired');
    expect(keys).toContain('kpi.currentStatus.open');
    expect(keys).toContain('kpi.myWork.overdue');
    expect(keys).toContain('kpi.ai.targets.trend');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, {
          date: '2026-04-15',
          status: 'open',
          action: 'submit',
          value: 1,
          year: 2026,
        }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });

  it.each(['en', 'ar'] as const)('names every code shown in the UI in %s', async (language) => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['resultStatus', resultStatuses],
      ['period', periodCodes],
      ['rag', ragCodes],
      ['anomaly', anomalyCodes],
      ['ai.smart.checks', smartCodes],
      ['measuringPeriod', ['quarterly', 'half_yearly', 'annual', 'two_year']],
      ['direction', ['increase', 'decrease', 'matching']],
      ['targetType', ['single', 'range']],
      ['nature', ['quantitative', 'qualitative']],
      ['level', ['organisation', 'department', 'section']],
      ['elementKind', ['organisation', 'vision', 'perspective', 'objective', 'kpi', 'unaligned']],
      ['planStatus', ['draft', 'confirmed']],
      ['changeStatus', ['draft', 'pending', 'approved', 'rejected', 'returned', 'withdrawn']],
      ['aggregation', ['sum', 'average']],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`kpi:${group}.${code}`)).not.toContain(`${group}.`);
  });
});
