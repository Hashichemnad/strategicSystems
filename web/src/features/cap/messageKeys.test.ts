import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import arBench from '@/i18n/ar/bench.json';
import arCap from '@/i18n/ar/cap.json';
import enBench from '@/i18n/en/bench.json';
import enCap from '@/i18n/en/cap.json';
import {
  benchProcesses,
  comparatorScopes,
  outreachChannels,
  outreachResponses,
  practiceCategories,
  studyStages,
  studyStatuses,
  studyViews,
  visitMethods,
} from '@/features/bench/api';
import {
  actionStatuses,
  actionViews,
  extensionStatuses,
  fishboneCategories,
  priorities,
  qualityFlags,
  raiseModes,
  remarkStages,
  remarkStatuses,
  remarkViews,
  rootCauseMethods,
  suggestionKinds,
  validationStatuses,
} from './api';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key a module can return to the SPA (from its C# sources). */
function serverKeys(prefix: string, dir: string): string[] {
  const keys = new Set<string>();
  const pattern = new RegExp(`"(${prefix}\\.(?:errors|currentStatus|actions|myWork)\\.[A-Za-z]+)"`, 'g');
  const walk = (folder: string) => {
    for (const name of readdirSync(folder)) {
      const path = join(folder, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(pattern)) if (match[1]) keys.add(match[1]);
    }
  };
  walk(dir);
  return [...keys].sort();
}

function paths(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null)
    return [prefix.replace(/_(zero|one|two|few|many|other)$/, '')];
  return [
    ...new Set(
      Object.entries(value).flatMap(([key, child]) => paths(child, prefix ? `${prefix}.${key}` : key)),
    ),
  ];
}

const modules = [
  { name: 'cap', dir: '../src/Modules/Cap/Strategy.Modules.Cap', ar: arCap, en: enCap },
  {
    name: 'bench',
    dir: '../src/Modules/Benchmarking/Strategy.Modules.Benchmarking',
    ar: arBench,
    en: enBench,
  },
] as const;

describe.each(modules)('$name message keys', ({ name, dir, ar, en }) => {
  const keys = serverKeys(name, dir);

  it('finds the keys', () => {
    expect(keys).toContain(`${name}.errors.invalidTransition`);
    expect(keys.some((k) => k.startsWith(`${name}.currentStatus.`))).toBe(true);
    expect(keys.some((k) => k.startsWith(`${name}.myWork.`))).toBe(true);
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) =>
        translateMessageKey(i18n.t, key, { date: '2026-04-15', done: 1, total: 2, stage: 'x' }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('has the same keys in Arabic and English', () => {
    expect([...new Set(paths(ar))].sort()).toEqual([...new Set(paths(en))].sort());
  });
});

describe.each(['en', 'ar'] as const)('codes shown in the UI (%s)', (language) => {
  it('are named in CAP', async () => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['remarkStatus', remarkStatuses],
      ['stage', remarkStages],
      ['actionStatus', actionStatuses],
      ['validationStatus', validationStatuses],
      ['priority', priorities],
      ['rootCauseMethod', rootCauseMethods],
      ['fishbone', fishboneCategories],
      ['remarkView', remarkViews],
      ['actionView', actionViews],
      ['suggestionKind', suggestionKinds],
      ['raiseMode', raiseModes],
      ['quality', qualityFlags],
      ['extensionStatus', extensionStatuses],
      ['suggestionStatus', ['open', 'raised', 'dismissed']],
      ['sourceStatus', ['active', 'closed']],
      ['origin', ['manual', 'system']],
      ['pmoDecision', ['confirmed', 'sent-back']],
      ['process', ['cap.action-validation', 'cap.completion', 'cap.extension']],
      ['reports', ['register', 'departmental', 'overdue']],
      [
        'tabs',
        [
          'overview',
          'root-cause',
          'actions',
          'tasks',
          'documents',
          'history',
          'progress',
          'evidence',
          'extensions',
          'approvals',
        ],
      ],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`cap:${group}.${code}`)).not.toContain(`${group}.`);
  });

  it('are named in Benchmarking', async () => {
    await i18n.changeLanguage(language);
    const groups: [string, readonly string[]][] = [
      ['stage', studyStages],
      ['status', studyStatuses],
      ['view', studyViews],
      ['scope', comparatorScopes],
      ['method', visitMethods],
      ['category', practiceCategories],
      ['channel', outreachChannels],
      ['response', outreachResponses],
      ['process', benchProcesses],
      ['origin', ['analysis', 'manual']],
      [
        'tabs',
        [
          'overview',
          'search',
          'visit',
          'analysis',
          'actions',
          'impact',
          'approvals',
          'tasks',
          'documents',
          'history',
        ],
      ],
    ];
    for (const [group, codes] of groups)
      for (const code of codes) expect(dynamicT(`bench:${group}.${code}`)).not.toContain(`${group}.`);
  });
});
