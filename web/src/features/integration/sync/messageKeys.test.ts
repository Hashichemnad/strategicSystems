import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';

/**
 * Every message key the Salesforce standby sync can return (read from the C# sources): Problem Details, run log lines,
 * exceptions, mapping validation issues, the health check and the step-up MFA errors of "Make this system primary".
 */
function serverKeys(): string[] {
  const roots = [
    '../src/Platform/Strategy.Platform.Integration',
    '../src/Platform/Strategy.Platform.Kernel/Integration',
    '../src/Platform/Strategy.Platform.Organisation/Application',
  ];
  const files = ['../src/Platform/Strategy.Platform.Identity/Application/StepUpVerifier.cs'];
  const keys = new Set<string>();
  const read = (path: string) => {
    for (const match of readFileSync(path, 'utf8').matchAll(
      /"((?:integration\.sync\.(?:errors|exceptions|validation|log|warnings)|health\.salesforceSync|auth\.errors)\.[A-Za-z]+)"/g,
    ))
      if (match[1]) keys.add(match[1]);
  };
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs')) read(path);
    }
  };
  roots.forEach(walk);
  files.forEach(read);
  return [...keys].sort();
}

describe('Salesforce sync server message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('integration.sync.errors.readOnly');
    expect(keys).toContain('integration.sync.exceptions.unmappedValue');
    expect(keys).toContain('integration.sync.validation.unknownField');
    expect(keys).toContain('integration.sync.log.objectDone');
    expect(keys).toContain('health.salesforceSync.ok');
    expect(keys).toContain('auth.errors.mfaRequiredForAction');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) => translateMessageKey(i18n.t, key, { source: 'Salesforce' }) === fallback,
    );
    expect(missing).toEqual([]);
  });

  it('every exception reason has a message and a label', async () => {
    await i18n.changeLanguage('en');
    for (const reason of [
      'unmappedValue',
      'unknownUser',
      'missingParent',
      'invalidValue',
      'requiredMissing',
      'writeFailed',
      'fileRejected',
      'unknownTarget',
    ]) {
      expect(i18n.exists(`integration:sync.exceptions.${reason}`)).toBe(true);
      expect(i18n.exists(`integration:sync.reasons.${reason}`)).toBe(true);
    }
  });

  it('fills the source name into the read-only error (409 toast)', async () => {
    await i18n.changeLanguage('en');
    expect(translateMessageKey(i18n.t, 'integration.sync.errors.readOnly', { source: 'Salesforce' })).toBe(
      'This record is synced from Salesforce and is read-only while Salesforce is the primary system. Make the change in Salesforce.',
    );
  });
});
