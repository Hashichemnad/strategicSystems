import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/ip.json';
import en from '@/i18n/en/ip.json';
import { ipSources, ipStatuses, periodKinds, signatureStatuses } from './api';
import { ipStageKeys } from './logic';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/**
 * Every message key the IP Registry can return to the SPA (from its C# sources). Permission, lookup, numbering,
 * notification and audit-event codes (`ip.record.read`, `ip.property-type`, `ip.renewalReminder`…) are not messages.
 */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(ip\.(?:errors|currentStatus|myWork)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/IpRegistry/Strategy.Modules.IpRegistry');
  return [...keys].sort();
}

/** Key paths without i18next plural suffixes (Arabic has six plural forms, English two). */
function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null)
    return [prefix.replace(/_(zero|one|two|few|many|other)$/, '')];
  return [
    ...new Set(
      Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key)),
    ),
  ];
}

describe('IP message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    for (const key of [
      'ip.errors.term',
      'ip.errors.duplicate',
      'ip.errors.reminderMonths',
      'ip.errors.contributorWho',
      'ip.errors.sharesTotal',
      'ip.errors.notAllowed',
      'ip.errors.expiryBeforeRegistration',
      'ip.errors.file',
      'ip.errors.period',
    ])
      expect(keys).toContain(key);
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    expect(keys.filter((key) => translateMessageKey(i18n.t, key) === fallback)).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect(paths(ar).sort()).toEqual(paths(en).sort());
  });

  it.each(['en', 'ar'] as const)('names every code shown in the UI in %s', async (language) => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['status', ipStatuses],
      ['source', ipSources],
      ['signature', signatureStatuses],
      ['periodKind', periodKinds],
      ['stage', ipStageKeys],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`ip:${group}.${code}`)).not.toContain(`${group}.`);
  });
});
