import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/auditmgmt.json';
import en from '@/i18n/en/auditmgmt.json';
import {
  engagementActions,
  engagementAllStages,
  evidenceSupport,
  findingStatuses,
  findingViews,
  logKinds,
  planStatuses,
  recommendationStatuses,
  reportStatuses,
  reviewStatuses,
  workPaperStatuses,
} from './api';
import { findingTabs } from './FindingPage';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Audit Management can return to the SPA (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const pattern = /"(auditmgmt\.(?:errors|currentStatus|actions|myWork)\.[A-Za-z]+)"/g;
  const walk = (folder: string) => {
    for (const name of readdirSync(folder)) {
      const path = join(folder, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(pattern)) if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/AuditManagement/Strategy.Modules.AuditManagement');
  return [...keys].sort();
}

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

describe('Audit Management message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('auditmgmt.errors.invalidTransition');
    expect(keys).toContain('auditmgmt.errors.nextFollowUpRequired');
    expect(keys.some((k) => k.startsWith('auditmgmt.currentStatus.'))).toBe(true);
    expect(keys.some((k) => k.startsWith('auditmgmt.myWork.'))).toBe(true);
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    expect(
      keys.filter((key) => translateMessageKey(i18n.t, key, { date: '2026-04-15', pct: 40 }) === fallback),
    ).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });
});

describe.each(['en', 'ar'] as const)('codes shown in the UI (%s)', (language) => {
  it('are named', async () => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['findingStatus', findingStatuses],
      ['recommendationStatus', recommendationStatuses],
      ['reportStatus', reportStatuses],
      ['view', findingViews],
      ['stage', engagementAllStages],
      ['engagementAction', engagementActions],
      ['planStatus', planStatuses],
      ['workPaperStatus', workPaperStatuses],
      ['logKind', logKinds],
      ['reviewStatus', reviewStatuses],
      ['evidenceSupport', evidenceSupport],
      ['tabs', findingTabs],
      ['calendar.kind', ['follow-up', 'due']],
      ['settings.scope', ['section', 'department']],
      ['department.scope', ['section', 'department']],
    ];
    const missing = groups.flatMap(([group, codes]) =>
      codes.map((code) => `auditmgmt:${group}.${code}`).filter((key) => !i18n.exists(key)),
    );
    expect(missing).toEqual([]);
    expect(dynamicT('auditmgmt:findingStatus.late')).not.toBe('findingStatus.late');
  });
});
