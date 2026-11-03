import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';

/** Every message key the AI platform and the MCP server can return (read from their C# sources). */
function serverKeys(): string[] {
  const roots = ['../src/Platform/Strategy.Platform.AI', '../src/Platform/Strategy.Platform.Mcp'];
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"((?:ai|mcp)\.(?:(?:errors|validation)\.[A-Za-z]+|provider\.testPassed))"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  roots.forEach(walk);
  return [...keys].sort();
}

describe('AI and MCP server message keys', () => {
  const keys = serverKeys();

  it('finds the keys', () => {
    expect(keys).toContain('ai.errors.featureDisabled');
    expect(keys).toContain('mcp.errors.disabled');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter(
      (key) => translateMessageKey(i18n.t, key, { maxMb: 10, limit: 5 }) === fallback,
    );
    expect(missing).toEqual([]);
  });
});
