import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';

const dynamicT = i18n.t.bind(i18n) as unknown as (key: string) => string;

/** Every message key Task Management can return to the SPA: errors, validation, current status, My Work, AI reasons (from its C# sources). */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(tasks\.(?:errors|validation|currentStatus|myWork|ai\.reasons)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Modules/Tasks/Strategy.Modules.Tasks');
  return [...keys].sort();
}

describe('Task Management message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('tasks.errors.openSubtasks');
    expect(keys).toContain('tasks.myWork.awaitingApproval');
    expect(keys).toContain('tasks.currentStatus.onHold');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) => translateMessageKey(i18n.t, key, { count: 1, max: 5, name: 'x', reason: 'y' }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it.each(['en', 'ar'] as const)('name every action and status in %s', async (language) => {
    await i18n.changeLanguage(language);
    for (const action of [
      'assign',
      'accept',
      'reject',
      'reassign',
      'pause',
      'start',
      'submit',
      'approve',
      'return',
      'complete',
      'cancel',
    ])
      expect(dynamicT(`tasks:actions.${action}`)).not.toContain('actions.');
    for (const template of ['full', 'simple'])
      for (const status of [
        'draft',
        'assigned',
        'in_progress',
        'on_hold',
        'submitted',
        'approved',
        'rejected',
        'to_do',
        'done',
        'cancelled',
      ])
        expect(dynamicT(`tasks:status.${template}.${status}`)).not.toContain('status.');
  });
});
