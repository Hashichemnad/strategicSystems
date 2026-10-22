import { test as base, expect, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export interface E2eOptions {
  language: 'ar' | 'en';
}

/** Screenshots committed for review: docs/screenshots/phase-0/<language>-<name>.png */
const screenshotDir = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  'docs',
  'screenshots',
  'phase-0',
);
mkdirSync(screenshotDir, { recursive: true });

export const test = base.extend<
  E2eOptions & { shot: (name: string, options?: { fullPage?: boolean }) => Promise<void> }
>({
  language: ['en', { option: true }],
  page: async ({ page, language }, use) => {
    await page.addInitScript((lang) => {
      if (!sessionStorage.getItem('e2e.initialised')) {
        localStorage.setItem('strategy.language', lang);
        localStorage.setItem('strategy.theme', 'light');
        sessionStorage.setItem('e2e.initialised', '1');
      }
    }, language);
    await use(page);
  },
  shot: async ({ page, language }, use) => {
    await use(async (name, options) => {
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({
        path: join(screenshotDir, `${language}-${name}.png`),
        fullPage: options?.fullPage ?? false,
      });
    });
  },
});

export { expect };

export const text = {
  ar: {
    home: 'أعمالي',
    sample: 'الوحدة النموذجية',
    newItem: 'بند جديد',
    create: 'إنشاء',
    save: 'حفظ',
    edit: 'تعديل',
    deactivate: 'إيقاف',
    inactive: 'غير نشط',
    created: 'تم إنشاء البند',
    search: 'البحث بالاسم',
    required: 'أدخل القيمة بالعربية أو الإنجليزية أو كلتيهما.',
    switchTo: 'English',
    uiKit: 'مكونات الواجهة',
    openMenu: 'فتح القائمة',
  },
  en: {
    home: 'My Work',
    sample: 'Sample module',
    newItem: 'New item',
    create: 'Create',
    save: 'Save',
    edit: 'Edit',
    deactivate: 'Deactivate',
    inactive: 'Inactive',
    created: 'Item created',
    search: 'Search by name',
    required: 'Enter this in Arabic, English or both.',
    switchTo: 'العربية',
    uiKit: 'UI kit',
    openMenu: 'Open menu',
  },
} as const;

/** Creates an item through the API exactly as the SPA does (antiforgery token + header). */
export async function createItemViaApi(page: Page, body: { nameAr?: string; nameEn?: string }) {
  const tokenResponse = await page.request.get('/api/v1/antiforgery/token');
  const { token, headerName } = (await tokenResponse.json()) as { token: string; headerName: string };
  const response = await page.request.post('/api/v1/sample/items', {
    data: body,
    headers: { [headerName]: token, 'X-Client': 'spa' },
  });
  expect(response.status()).toBe(201);
  return (await response.json()) as { id: string };
}
