import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext, Page } from '@playwright/test';
import { apiPost, expect, mailpitUrl, screenshotPath, test, tr, useMailpit, type Language } from './fixtures';

/**
 * Phase 21 — Reporting 2.0 (docs/design/reporting-2.md): report types (seeded and edited by the reporting administrator), the report builder
 * with grouping, subtotals and a chart, My reports and folders, record dossiers (project dossier as PDF and Word, the template designer) and a
 * scheduled dossier e-mail. Mariam is the demo reporting administrator and report author. Run after the Portfolio specs (projects exist).
 * Screenshots: docs/screenshots/phase-21 (E2E_SCREENSHOTS=phase-21).
 */

test.use({ signedInAs: 'mariam' });

const gotenbergUrl = process.env.E2E_GOTENBERG_URL ?? 'http://localhost:3000';

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
}

/** First page of a PDF as a PNG next to the screenshots (pdftoppm), so reviewers see the real document. */
function pdfToPng(pdf: Buffer, language: Language, name: string, page = 1) {
  const dir = mkdtempSync(join(tmpdir(), 'dossier-'));
  const file = join(dir, 'file.pdf');
  writeFileSync(file, pdf);
  execFileSync('pdftoppm', [
    '-png',
    '-r',
    '70',
    '-f',
    String(page),
    '-l',
    String(page),
    '-singlefile',
    file,
    screenshotPath(language, name).replace(/\.png$/u, ''),
  ]);
}

test.describe('Reporting 2.0', () => {
  test('report types, builder with grouping and chart, My reports and folders, project dossier, designer, scheduled dossier', async ({
    page,
    language,
    adminApi,
    baseURL,
    shot,
  }) => {
    test.setTimeout(600_000);
    const b = (key: string, params: Record<string, string | number> = {}) =>
      tr(language, `reportBuilder:${key}`, params);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;

    const records = await json<{ id: string; number: string }[]>(
      await adminApi.get('/api/v1/reporting/dossier-templates/portfolio.project-dossier/records'),
    );
    const project = records[0];
    if (!project)
      throw new Error('No project to render: run after the Portfolio specs (they create the projects).');

    // ---- Report type editor (Settings → Report types): the seeded "projects with milestones and tasks" -------------------------
    await page.goto('/settings/report-types');
    await expect(page.getByTestId('report-type-list')).toBeVisible();
    await page
      .getByRole('button', {
        name: language === 'ar' ? 'المشاريع مع المعالم والمهام' : 'Projects with milestones and tasks',
      })
      .click();
    await expect(page.getByTestId('report-type-editor')).toBeVisible();
    await expect(page.getByTestId('report-type-node-c')).toBeVisible();
    await settle(page);
    await shot('report-type-editor', { fullPage: true });

    // ---- Report builder: projects with milestones, grouped by stage and owning unit, subtotals and a chart --------------------
    const folder = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/reporting/folders', {
        name: `${b('home.myReports')} ${tag}`,
        kind: 'private',
      }),
    );
    const shared = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/reporting/folders', { name: `PMO ${tag}`, kind: 'shared' }),
    );
    const definition = {
      reportType: 'portfolio.projects-milestones',
      dataset: '',
      columns: [
        'a.number',
        language === 'ar' ? 'a.nameAr' : 'a.nameEn',
        'a.performance',
        language === 'ar' ? 'b.titleAr' : 'b.titleEn',
        'b.status',
        'b.plannedEnd',
        'b.completion',
      ],
      filters: [],
      groupBy: ['a.stage', 'a.unit'],
      sort: [{ field: 'a.number', descending: false }],
      summaries: [
        { column: 'b.completion', functions: ['avg', 'min', 'max'] },
        { column: 'a.performance', functions: ['avg'] },
      ],
      chart: { type: 'bar', category: 'a.stage', series: null, measure: null, aggregate: 'sum' },
      highlights: [{ column: 'b.completion', operator: 'lte', values: ['0.25'], tone: 'bad' }],
      showDetails: true,
    };
    const report = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/reporting/reports', {
        name: `${tag} — ${b('builder.title')}`,
        definition,
        folderId: folder.id,
      }),
    );
    await apiPost(adminApi, '/api/v1/reporting/reports', {
      name: `${tag} published`,
      definition,
      folderId: shared.id,
    });
    await page.goto(`/reports/${report.id}`);
    await expect(page.getByTestId('report-builder')).toBeVisible();
    await page.getByTestId('report-run').click();
    await expect(page.getByTestId('report-preview')).toBeVisible();
    await expect(page.getByTestId('report-group').first()).toBeVisible();
    await expect(page.getByTestId('report-chart')).toBeVisible();
    await settle(page);
    await shot('report-builder-grouping-chart', { fullPage: true });

    // ---- My reports and folders ---------------------------------------------------------------------------------------------
    await page.goto('/reports');
    await expect(page.getByTestId('report-folders')).toBeVisible();
    await page
      .getByTestId('report-folders')
      .getByRole('button', { name: `PMO ${tag}` })
      .click();
    await expect(page.getByTestId('report-list')).toContainText(`${tag} published`);
    await settle(page);
    await shot('my-reports-folders', { fullPage: true });

    // ---- Project dossier: PDF and Word (Word shown through Gotenberg's LibreOffice route) --------------------------------------
    await page.goto(`/portfolio/projects/${project.id}`);
    await page.getByTestId('dossier-open').click();
    await expect(page.getByTestId('dossier-dialog')).toBeVisible();
    await shot('dossier-dialog');
    await page.keyboard.press('Escape');
    const pdf = await adminApi.post('/api/v1/reporting/dossiers/portfolio.project-dossier/render', {
      data: { recordId: project.id, format: 'pdf', language },
    });
    expect(pdf.ok(), await pdf.text()).toBe(true);
    const pdfBytes = await pdf.body();
    pdfToPng(pdfBytes, language, 'project-dossier-pdf-cover', 1);
    pdfToPng(pdfBytes, language, 'project-dossier-pdf', 2);
    pdfToPng(pdfBytes, language, 'project-dossier-pdf-gantt', 3);
    const docx = await adminApi.post('/api/v1/reporting/dossiers/portfolio.project-dossier/render', {
      data: { recordId: project.id, format: 'docx', language },
    });
    expect(docx.ok(), await docx.text()).toBe(true);
    const converted = await page.request.post(`${gotenbergUrl}/forms/libreoffice/convert`, {
      multipart: {
        files: {
          name: 'dossier.docx',
          mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          buffer: await docx.body(),
        },
      },
    });
    expect(converted.ok(), await converted.text()).toBe(true);
    pdfToPng(await converted.body(), language, 'project-dossier-word', 2);

    // ---- Dossier template designer ---------------------------------------------------------------------------------------------
    await page.goto('/settings/dossier-templates');
    await expect(page.getByTestId('dossier-template-list')).toBeVisible();
    await page.getByRole('button', { name: language === 'ar' ? 'ملف المشروع' : 'Project dossier' }).click();
    await expect(page.getByTestId('dossier-designer')).toBeVisible();
    await page
      .getByTestId('dossier-sections')
      .getByRole('button', {
        name: new RegExp(language === 'ar' ? 'المؤشرات ونتائجها' : 'KPIs with results'),
      })
      .click();
    await expect(page.getByTestId('dossier-section-editor')).toBeVisible();
    await settle(page);
    await shot('dossier-template-designer', { fullPage: true });

    // ---- Scheduled dossier: each project manager gets their projects' dossiers (here: sent now, to Mariam) ---------------------
    await page.goto('/reports/dossiers');
    await expect(page.getByTestId('dossiers-page')).toBeVisible();
    await page.getByRole('tab', { name: b('dossiers.schedule') }).click();
    await expect(page.getByTestId('dossier-schedule')).toBeVisible();
    await settle(page);
    await shot('scheduled-dossier', { fullPage: true });

    await useMailpit(adminApi, baseURL ?? '');
    const me = await json<{ id: string }>(await adminApi.get('/api/v1/identity/me'));
    const template = (
      await json<{ id: string; code: string }[]>(await adminApi.get('/api/v1/reporting/dossier-templates'))
    ).find((x) => x.code === 'portfolio.project-dossier');
    if (!template) throw new Error('The seeded project dossier template is missing.');
    const regional = await json<{ value: { timeZone: string } | null }>(
      await adminApi.get('/api/v1/settings/regional'),
    );
    const zone = regional.value?.timeZone ?? 'Asia/Dubai';
    const at = new Date(Date.now() + 70_000);
    const time = new Intl.DateTimeFormat('en-GB', {
      timeZone: zone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(at);
    await json(
      await apiPost(adminApi, '/api/v1/reporting/schedules', {
        name: `Dossier ${tag}`,
        targetKind: 'dossier',
        targetId: template.id,
        frequency: 'daily',
        time,
        day: 1,
        periodRule: 'previous-month',
        format: 'pdf',
        active: true,
        recipients: [{ kind: 'user', ref: me.id }],
        dossier: {
          filters: [{ field: 'a.number', operator: 'eq', values: [project.number] }],
          language,
          recipientField: null,
        },
      }),
    );
    const since = new Date();
    await page.waitForTimeout(Math.max(0, at.getTime() - Date.now() + 5_000));
    await apiPost(adminApi, '/api/v1/scheduling/jobs/reporting.deliver/run');
    let messageId = '';
    await expect
      .poll(
        async () => {
          const response = await page.request.get(`${mailpitUrl}/api/v1/search`, {
            params: { query: tag, limit: '10' },
          });
          const { messages } = (await response.json()) as { messages: { ID: string; Created: string }[] };
          messageId = messages.find((m) => new Date(m.Created) >= since)?.ID ?? '';
          return messageId;
        },
        { timeout: 120_000, intervals: [3_000] },
      )
      .not.toBe('');
    const mail = await page.context().newPage();
    await mail.goto(`${mailpitUrl}/view/${messageId}`);
    await mail.waitForLoadState('networkidle');
    await mail.screenshot({ path: screenshotPath(language, 'scheduled-dossier-email'), fullPage: true });
    await mail.close();
  });
});
