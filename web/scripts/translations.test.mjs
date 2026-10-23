import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const web = join(import.meta.dirname, '..');
const repo = join(web, '..');
const languages = ['ar', 'en'];

function load(language) {
  const dir = join(web, 'src', 'i18n', language);
  return Object.fromEntries(
    readdirSync(dir)
      .filter((file) => file.endsWith('.json'))
      .map((file) => [file.slice(0, -5), JSON.parse(readFileSync(join(dir, file), 'utf8'))]),
  );
}

/** Leaf keys of a namespace, with plural suffixes folded into their base key. */
function keysOf(tree, prefix = '') {
  return Object.entries(tree).flatMap(([key, value]) =>
    typeof value === 'string'
      ? [`${prefix}${key.replace(/_(zero|one|two|few|many|other)$/u, '')}`]
      : keysOf(value, `${prefix}${key}.`),
  );
}

function csharpFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory())
      return ['bin', 'obj', 'Migrations'].includes(entry.name) ? [] : csharpFiles(path);
    return entry.name.endsWith('.cs') ? [path] : [];
  });
}

const resources = Object.fromEntries(languages.map((language) => [language, load(language)]));
const keys = Object.fromEntries(
  languages.map((language) => [
    language,
    new Set(
      Object.entries(resources[language]).flatMap(([namespace, tree]) =>
        keysOf(tree).map((key) => `${namespace}.${key}`),
      ),
    ),
  ]),
);

describe('translations', () => {
  it('have the same keys in Arabic and English', () => {
    expect([...keys.en].filter((key) => !keys.ar.has(key))).toEqual([]);
    expect([...keys.ar].filter((key) => !keys.en.has(key))).toEqual([]);
  });

  it('cover every message key the server sends', () => {
    const namespaces = Object.keys(resources.en).join('|');
    const literal = new RegExp(`"((?:${namespaces})\\.[A-Za-z0-9]+(?:\\.[A-Za-z0-9]+)*)"`, 'gu');
    const found = new Set();
    for (const file of csharpFiles(join(repo, 'src'))) {
      for (const [, key] of readFileSync(file, 'utf8').matchAll(literal)) {
        // Message keys live in the errors / validation namespaces or a module's `errors` / `messages` group;
        // other dotted literals are stable error codes or permission codes.
        const namespace = key.split('.')[0];
        if (namespace === 'errors' || namespace === 'validation' || /\.(errors|messages)\./u.test(key)) {
          found.add(key);
        }
      }
    }
    found.delete('validation.failed'); // an error code, not a message key

    expect(found.size).toBeGreaterThan(50);
    const missing = [...found].filter((key) => languages.some((language) => !keys[language].has(key)));
    expect(missing.sort()).toEqual([]);
  });
});
