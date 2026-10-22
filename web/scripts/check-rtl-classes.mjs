// Fails when a source file uses physical left/right Tailwind classes (see scripts/lib/rtl-classes.mjs).
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { findPhysicalClasses } from './lib/rtl-classes.mjs';

const root = new URL('../src', import.meta.url).pathname;

function* files(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (/\.(tsx?|css)$/.test(name) && !name.endsWith('.d.ts') && !name.includes('.test.')) yield path;
  }
}

let failures = 0;
for (const file of files(root)) {
  for (const { line, match } of findPhysicalClasses(readFileSync(file, 'utf8'))) {
    console.error(
      `src/${relative(root, file)}:${line}  physical class "${match}" — use the logical equivalent`,
    );
    failures++;
  }
}

if (failures > 0) {
  console.error(
    `\n${failures} physical left/right class(es) found. RTL needs logical properties (CLAUDE.md §4.2).`,
  );
  process.exit(1);
}
console.log('RTL check: no physical left/right classes.');
