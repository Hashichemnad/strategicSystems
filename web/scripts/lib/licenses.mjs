// CLAUDE.md §4.3: only free / OSI licences, none of the named commercial packages.
export const ALLOWED = new Set([
  'MIT',
  'MIT-0',
  'Apache-2.0',
  'BSD-2-Clause',
  'BSD-3-Clause',
  'ISC',
  'MPL-2.0',
  'Unlicense',
  '0BSD',
  'OFL-1.1',
]);

/**
 * Permissive licences accepted only for build / test tooling that never ships in the product (ADR 0007):
 * BlueOak-1.0.0 and Python-2.0 are OSI-approved; CC0-1.0 / CC-BY-4.0 cover data files (caniuse-lite, language tags).
 */
export const DEV_ONLY_ALLOWED = new Set(['BlueOak-1.0.0', 'Python-2.0', 'CC0-1.0', 'CC-BY-4.0']);

export const BANNED = [
  /^ag-grid-enterprise$/,
  /^@ag-grid-enterprise\//,
  /^highcharts/,
  /^dhtmlx/,
  /^@dhx\//,
  /^@bryntum\//,
  /^@progress\/kendo/,
  /^@syncfusion\//,
  /^devextreme/,
  /^@devexpress\//,
];

/** Packages without licence metadata in the lockfile, verified by hand. Keep short; note each in THIRD_PARTY_LICENSES.md. */
export const MANUALLY_VERIFIED = {};

/** SPDX: `A OR B` needs one allowed side, `A AND B` needs both. */
export function isLicenseAllowed(expression, { dev = false } = {}) {
  if (!expression || typeof expression !== 'string') return false;
  const allowed = (id) => ALLOWED.has(id) || (dev && DEV_ONLY_ALLOWED.has(id));
  return expression
    .replace(/[()]/g, '')
    .split(/\s+OR\s+/)
    .some((alternative) => alternative.split(/\s+AND\s+/).every((id) => allowed(id.trim())));
}

/** Checks every package in an npm v3 lockfile; returns human-readable violations. */
export function findViolations(lockfile) {
  const violations = [];
  for (const [path, entry] of Object.entries(lockfile.packages ?? {})) {
    if (path === '' || entry.link) continue;
    const name = entry.name ?? path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length);
    if (BANNED.some((pattern) => pattern.test(name))) {
      violations.push(`${name}@${entry.version} is banned (CLAUDE.md §4.3)`);
      continue;
    }
    const license = entry.license ?? MANUALLY_VERIFIED[name];
    if (!isLicenseAllowed(license, { dev: entry.dev === true })) {
      violations.push(
        `${name}@${entry.version} has licence "${license ?? '(none)'}" outside the allow-list${entry.dev ? '' : ' (runtime dependency)'}`,
      );
    }
  }
  return violations;
}
