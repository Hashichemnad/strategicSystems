import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/docreg.json';
import en from '@/i18n/en/docreg.json';
import {
  daysUntil,
  defaultPair,
  documentStatuses,
  validityKinds,
  versionStatuses,
  type DocumentVersion,
} from './api';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key the Document Registry can return to the SPA: errors and My Work (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(/"(docreg\.(?:errors|myWork)\.[A-Za-z]+)"/g))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/DocumentRegistry/Strategy.Modules.DocumentRegistry');
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

describe('Document Registry message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('docreg.errors.versionInProgress');
    expect(keys).toContain('docreg.errors.noText');
    expect(keys).toContain('docreg.myWork.expiring');
    expect(keys).toContain('docreg.myWork.acknowledge');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter((key) => translateMessageKey(i18n.t, key) === fallback);
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect([...new Set(paths(ar))].sort()).toEqual([...new Set(paths(en))].sort());
  });

  it.each(['en', 'ar'] as const)('names every status and validity event in %s', async (language) => {
    await i18n.changeLanguage(language);
    for (const status of documentStatuses)
      expect(dynamicT(`docreg:status.${status}`)).not.toContain('status.');
    for (const status of versionStatuses)
      expect(dynamicT(`docreg:versionStatus.${status}`)).not.toContain('versionStatus.');
    for (const kind of validityKinds)
      expect(dynamicT(`docreg:validityKind.${kind}`)).not.toContain('validityKind.');
  });
});

describe('Document Registry helpers', () => {
  it('counts days to an expiry date', () => {
    const today = new Date(2026, 10, 4);
    expect(daysUntil('2026-11-04', today)).toBe(0);
    expect(daysUntil('2026-12-04', today)).toBe(30);
    expect(daysUntil('2026-11-01', today)).toBe(-3);
  });

  it('compares the previous version with the current one by default', () => {
    const v = (id: string, status: string) => ({ id, status }) as DocumentVersion;
    expect(defaultPair([v('c', 'under_review'), v('b', 'current'), v('a', 'superseded')])).toEqual({
      from: 'a',
      to: 'b',
    });
    expect(defaultPair([v('b', 'current')])).toEqual({ from: undefined, to: 'b' });
  });
});
