import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** WCAG 2.1 A + AA rules (docs/accessibility.md). Best-practice rules are not part of the gate. */
export const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const here = dirname(fileURLToPath(import.meta.url));
const reportFile = join(here, '..', 'test-results', 'a11y', 'violations.jsonl');
mkdirSync(dirname(reportFile), { recursive: true });

/** Waits until the screen has settled: no pending requests, no skeletons, fonts loaded. */
export async function settle(page: Page) {
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page
    .locator('[aria-busy="true"]')
    .first()
    .waitFor({ state: 'detached', timeout: 10_000 })
    .catch(() => undefined);
  await page.evaluate(() => document.fonts.ready);
}

/**
 * Runs axe-core on the current page and fails the test on any WCAG 2.1 A / AA violation, listing rule, impact and
 * the offending elements. Every result (also a clean one) is appended to test-results/a11y/violations.jsonl so the
 * report in docs/accessibility.md can be rebuilt.
 */
export async function expectAccessible(page: Page, screen: string, options: { exclude?: string[] } = {}) {
  await settle(page);
  let builder = new AxeBuilder({ page }).withTags(wcagTags);
  for (const selector of options.exclude ?? []) builder = builder.exclude(selector);
  const result = await builder.analyze();
  const project = test.info().project.name;
  appendFileSync(
    reportFile,
    JSON.stringify({
      project,
      screen,
      url: new URL(page.url()).pathname,
      passes: result.passes.length,
      violations: result.violations.map((v) => ({
        id: v.id,
        impact: v.impact,
        nodes: v.nodes.map((n) => ({ target: n.target.join(' '), summary: n.failureSummary })),
      })),
    }) + '\n',
  );
  const summary = result.violations.map(
    (v) =>
      `${v.id} (${v.impact ?? 'n/a'}): ${v.help}\n` +
      v.nodes
        .slice(0, 5)
        .map((n) => `    ${n.target.join(' ')} — ${n.failureSummary?.split('\n').slice(1).join(' ') ?? ''}`)
        .join('\n'),
  );
  expect.soft(summary, `${project} ${screen} (${page.url()})`).toEqual([]);
}
