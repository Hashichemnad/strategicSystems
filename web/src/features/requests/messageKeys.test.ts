import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/requests.json';
import en from '@/i18n/en/requests.json';
import { requestStatuses } from './api';
import { conditionOps, fieldTypes } from './formLogic';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key the Request Framework can return to the SPA (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(requests\.(?:errors|currentStatus|actions|myWork)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Requests');
  return [...keys].sort();
}

/** Key paths with i18next plural suffixes folded (Arabic has more plural forms than English). */
function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null)
    return [prefix.replace(/_(zero|one|two|few|many|other)$/u, '')];
  return Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key));
}

describe('Request Framework message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('requests.errors.duplicateCode');
    expect(keys).toContain('requests.currentStatus.returned');
    expect(keys).toContain('requests.actions.resubmit');
    expect(keys).toContain('requests.myWork.process');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, { stage: 'Review', max: 100, decimals: 2, min: 1, field: 'x' }) ===
        fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect([...new Set(paths(ar))].sort()).toEqual([...new Set(paths(en))].sort());
  });

  it.each(['en', 'ar'] as const)('names every code shown in the UI in %s', async (language) => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['status', requestStatuses],
      ['fieldTypes', fieldTypes],
      ['builder.condition.ops', conditionOps],
      ['builder.stages.kinds', ['approval', 'processing']],
      ['builder.templates.audiences', ['requester', 'processor', 'admin']],
      ['request.tabs', ['overview', 'approvals', 'timeline', 'documents', 'tasks', 'history']],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`requests:${group}.${code}`)).not.toContain(`${group}.`);
  });
});
