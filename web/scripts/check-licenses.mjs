// Fails when web/package-lock.json contains a banned package or a licence outside the allow-list.
import { readFileSync } from 'node:fs';
import { findViolations } from './lib/licenses.mjs';

const lockfile = JSON.parse(readFileSync(new URL('../package-lock.json', import.meta.url), 'utf8'));
const violations = findViolations(lockfile);
if (violations.length > 0) {
  console.error(violations.join('\n'));
  console.error(`\n${violations.length} licence violation(s). See CLAUDE.md §4.3.`);
  process.exit(1);
}
console.log(`Licence check: ${Object.keys(lockfile.packages).length - 1} npm packages, all allowed.`);
