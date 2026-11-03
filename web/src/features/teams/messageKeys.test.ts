import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/teams.json';
import en from '@/i18n/en/teams.json';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Teams Management can return to the SPA: errors, current status, My Work (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(teams\.(?:errors|currentStatus|myWork)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Teams/Strategy.Modules.Teams');
  return [...keys].sort();
}

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

describe('Teams message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('teams.errors.leaderRequired');
    expect(keys).toContain('teams.errors.bandsGap');
    expect(keys).toContain('teams.myWork.evaluate');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, {
          cycle: '2026',
          date: '2026-01-01',
          threshold: 90,
          total: 90,
          min: 3,
          max: 15,
          at: 9,
          from: 9,
          to: 10,
        }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });

  it.each(['en', 'ar'] as const)(
    'names every team, evaluation and recommendation status in %s',
    async (language) => {
      await i18n.changeLanguage(language);
      for (const status of [
        'draft',
        'pending_approval',
        'active',
        'inactive',
        'pending_evaluation',
        'evaluation_waiting_approval',
        'closed',
        'replaced',
        'cancelled',
      ])
        expect(dynamicT(`teams:status.${status}`)).not.toContain('status.');
      for (const status of ['open', 'submitted', 'returned', 'approved', 'cancelled'])
        expect(dynamicT(`teams:evaluations.statuses.${status}`)).not.toContain('statuses.');
      for (const status of ['open', 'in_progress', 'closed', 'rejected'])
        expect(dynamicT(`teams:recommendations.statuses.${status}`)).not.toContain('statuses.');
    },
  );
});
