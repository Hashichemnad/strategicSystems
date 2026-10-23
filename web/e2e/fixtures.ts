import { test as base, expect, type APIRequestContext, type Page } from '@playwright/test';
import { createHmac } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export type Language = 'ar' | 'en';
export type SeedUser = 'mariam' | 'ahmed';

export interface E2eOptions {
  language: Language;
  /** Signed in through the API before the test (default: the regular demo user); null = signed out. */
  signedInAs: SeedUser | null;
}

const here = dirname(fileURLToPath(import.meta.url));

/** Screenshots committed for review: docs/screenshots/<phase>/<language>-<name>.png (E2E_SCREENSHOTS = phase folder). */
const screenshotDir = join(here, '..', '..', 'docs', 'screenshots', process.env.E2E_SCREENSHOTS ?? 'phase-1');
mkdirSync(screenshotDir, { recursive: true });

/** The demo seed (Development): password and the administrator's authenticator key (Seed__AdminTotpKey). */
export const seed = {
  password: process.env.E2E_SEED_PASSWORD ?? 'Strategy#Demo2026',
  adminTotpKey: process.env.E2E_ADMIN_TOTP_KEY ?? 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP',
  users: { mariam: 'mariam@strategy.local', ahmed: 'ahmed@strategy.local' } satisfies Record<
    SeedUser,
    string
  >,
};

export const mailpitUrl = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';

// ---- Translations: the SPA's own resources, so specs follow the wording instead of copying it ---------------

type Tree = { [key: string]: string | Tree };
const resources = new Map<string, Tree>();

function namespace(language: Language, ns: string): Tree {
  const key = `${language}/${ns}`;
  let tree = resources.get(key);
  if (!tree) {
    tree = JSON.parse(readFileSync(join(here, '..', 'src', 'i18n', language, `${ns}.json`), 'utf8')) as Tree;
    resources.set(key, tree);
  }
  return tree;
}

/** `tr('ar', 'auth:login.submit')`, with `{{param}}` interpolation; plurals follow the language's rules (`_one`, `_two`, `_few`…), else `_other`. */
export function tr(language: Language, key: string, params: Record<string, string | number> = {}): string {
  const [ns = 'common', path = ''] = key.includes(':') ? key.split(':', 2) : ['common', key];
  let node: string | Tree | undefined = namespace(language, ns);
  for (const part of path.split('.')) {
    node =
      typeof node === 'object'
        ? (node[part] ??
          (typeof params.count === 'number'
            ? node[`${part}_${new Intl.PluralRules(language).select(params.count)}`]
            : undefined) ??
          node[`${part}_other`])
        : undefined;
  }
  if (typeof node !== 'string') throw new Error(`Missing translation ${language}:${key}`);
  return node.replace(/\{\{(\w+)\}\}/gu, (_, name: string) => String(params[name] ?? ''));
}

// ---- Sign-in helpers -------------------------------------------------------------------------------------------

/** RFC 6238 code (SHA-1, 30 s, 6 digits) for a base32 key, as an authenticator app shows it. */
export function totp(base32Key: string, at = Date.now()): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = Array.from(base32Key.replace(/[=\s]/gu, '').toUpperCase())
    .map((c) => alphabet.indexOf(c).toString(2).padStart(5, '0'))
    .join('');
  const key = Buffer.from((bits.match(/.{8}/gu) ?? []).map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 30_000)));
  const hmac = createHmac('sha1', key).update(counter).digest();
  const offset = (hmac.at(-1) ?? 0) & 0xf;
  return ((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

/**
 * Time steps already used per key, kept in a file rather than in memory: Playwright restarts the worker after a failed
 * test, and runs follow each other on one machine, and a reused step is refused by the server.
 */
const usedStepsFile = join(tmpdir(), 'strategy-e2e-totp-steps.json');

function readUsedSteps(): Record<string, number> {
  try {
    return JSON.parse(readFileSync(usedStepsFile, 'utf8')) as Record<string, number>;
  } catch {
    return {};
  }
}

/**
 * The code an authenticator app shows next. The server accepts each time step once (and never an older one), within
 * two steps of the current one, so repeated sign-ins with one key take the following steps. The clock is waited for only
 * when two steps ahead are used; the running test then gets that time added to its timeout.
 */
export async function nextTotp(base32Key: string): Promise<string> {
  const key = base32Key.replace(/\s/gu, '').toUpperCase();
  for (;;) {
    const current = Math.floor(Date.now() / 30_000);
    const steps = readUsedSteps();
    const step = Math.max(current, (steps[key] ?? current - 1) + 1);
    if (step <= current + 2) {
      writeFileSync(usedStepsFile, JSON.stringify({ ...steps, [key]: step }));
      return totp(key, step * 30_000);
    }
    const wait = 30_000 - (Date.now() % 30_000) + 100;
    try {
      base.info().setTimeout(base.info().timeout + wait);
    } catch {
      // Not inside a test (worker fixture): its own timeout applies.
    }
    await new Promise((resolve) => setTimeout(resolve, wait));
  }
}

/** A page (its browser session) or a separate API session. */
export type Requester = Page | APIRequestContext;
const requestOf = (requester: Requester): APIRequestContext =>
  'goto' in requester ? requester.request : requester;

async function antiforgeryHeaders(requester: Requester): Promise<Record<string, string>> {
  const response = await requestOf(requester).get('/api/v1/antiforgery/token');
  const { token, headerName } = (await response.json()) as { token: string; headerName: string };
  return { [headerName]: token, 'X-Client': 'spa' };
}

/** POST as the SPA does (antiforgery token for the current session + header). */
export async function apiPost(requester: Requester, url: string, data?: unknown) {
  return requestOf(requester).post(url, { data: data ?? {}, headers: await antiforgeryHeaders(requester) });
}

export async function apiPut(requester: Requester, url: string, data: unknown) {
  return requestOf(requester).put(url, { data, headers: await antiforgeryHeaders(requester) });
}

/** Signs a seeded user in through the API (a page's browser context shares the session cookie). */
export async function signInViaApi(requester: Requester, user: SeedUser) {
  const login = await apiPost(requester, '/api/v1/identity/auth/login', {
    login: seed.users[user],
    password: seed.password,
  });
  expect(login.status()).toBe(200);
  const { status } = (await login.json()) as { status: string };
  if (status === 'mfaRequired') {
    // A run started less than 90 s after the previous one may find this step already used: take the next one.
    let verify = await apiPost(requester, '/api/v1/identity/auth/mfa/verify', {
      method: 'totp',
      code: await nextTotp(seed.adminTotpKey),
    });
    for (let retry = 0; retry < 3 && verify.status() === 400; retry++) {
      verify = await apiPost(requester, '/api/v1/identity/auth/mfa/verify', {
        method: 'totp',
        code: await nextTotp(seed.adminTotpKey),
      });
    }
    expect(verify.status()).toBe(200);
  } else {
    expect(status).toBe('signedIn');
  }
}

export async function savePreferences(page: Page, language: Language) {
  const response = await apiPut(page, '/api/v1/identity/me/preferences', { language, theme: 'light' });
  expect(response.ok()).toBe(true);
}

// ---- Organisation data (as an administrator) --------------------------------------------------------------------

export async function unitIdByCode(requester: Requester, code: string): Promise<string> {
  const response = await requestOf(requester).get('/api/v1/org/units/tree');
  const units = (await response.json()) as { id: string; code: string }[];
  const unit = units.find((u) => u.code === code);
  if (!unit) throw new Error(`Unit ${code} not found`);
  return unit.id;
}

/** Points the email settings at Mailpit (the dev SMTP sink) and links back to this installation. */
export async function useMailpit(admin: Requester, baseURL: string) {
  const current = (await (await requestOf(admin).get('/api/v1/identity/admin/smtp')).json()) as {
    version: number;
  };
  const response = await apiPut(admin, '/api/v1/identity/admin/smtp', {
    enabled: true,
    host: new URL(mailpitUrl).hostname,
    port: Number(process.env.E2E_MAILPIT_SMTP_PORT ?? 1025),
    security: 'none',
    userName: null,
    password: null,
    clearPassword: false,
    fromAddress: 'strategy@strategy.local',
    fromNameAr: 'نظام الاستراتيجية',
    fromNameEn: 'Strategy Suite',
    publicBaseUrl: baseURL,
    version: current.version,
  });
  expect(response.status()).toBe(200);
}

/**
 * A fresh employee with an account and a known password (invitation email → reset endpoint); `page` stays signed out.
 * Tests that change MFA or tokens use one, so the shared demo users stay as seeded.
 */
export async function createUserWithPassword(
  page: Page,
  admin: APIRequestContext,
  baseURL: string,
  tag: string,
) {
  const email = `user.${tag}@strategy.local`;
  const password = `Start#${tag}Pw9`;
  await useMailpit(admin, baseURL);
  const employee = await apiPost(admin, '/api/v1/org/employees', {
    employeeNo: `E2E-${tag}`,
    nameAr: `مستخدم تجريبي ${tag}`,
    nameEn: `Test User ${tag}`,
    email,
    unitId: await unitIdByCode(admin, 'DEP-STR'),
    joinDate: '2024-01-01',
  });
  expect(employee.status()).toBe(201);
  const since = new Date(Date.now() - 1000);
  const account = await apiPost(admin, '/api/v1/identity/admin/users', {
    employeeId: ((await employee.json()) as { id: string }).id,
    sendInvitation: true,
  });
  expect(account.ok()).toBe(true);

  const link = new URL(
    /https?:\/\/\S*\/reset-password\?\S+/u.exec((await latestEmail(page, email, since)).text)?.[0] ?? '',
  );
  const reset = await apiPost(page, '/api/v1/identity/auth/password/reset', {
    userId: link.searchParams.get('user'),
    token: link.searchParams.get('token'),
    newPassword: password,
    invitation: true,
  });
  expect(reset.ok()).toBe(true);
  return { email, password };
}

// ---- Mailpit (dev SMTP sink) -----------------------------------------------------------------------------------

/** Waits for the newest message to an address and returns its plain-text body. */
export async function latestEmail(
  page: Page,
  to: string,
  since: Date,
): Promise<{ subject: string; text: string }> {
  let found: { ID: string; Subject: string; Created: string } | undefined;
  await expect
    .poll(
      async () => {
        const response = await page.request.get(`${mailpitUrl}/api/v1/search`, {
          params: { query: `to:"${to}"`, limit: '5' },
        });
        const { messages } = (await response.json()) as {
          messages: { ID: string; Subject: string; Created: string }[];
        };
        found = messages.find((m) => new Date(m.Created) >= since);
        return Boolean(found);
      },
      { timeout: 20_000 },
    )
    .toBe(true);
  const message = (await (await page.request.get(`${mailpitUrl}/api/v1/message/${found?.ID}`)).json()) as {
    Subject: string;
    Text: string;
  };
  return { subject: message.Subject, text: message.Text };
}

export const test = base.extend<
  E2eOptions & { shot: (name: string, options?: { fullPage?: boolean }) => Promise<void> },
  { adminApi: APIRequestContext }
>({
  /**
   * One administrator session per worker (the admin signs in with an authenticator code, and each code is accepted once):
   * used for setup calls and shared with pages of tests that run as the administrator. Never signed out.
   */
  adminApi: [
    async ({ playwright }, use, workerInfo) => {
      const context = await playwright.request.newContext({ baseURL: workerInfo.project.use.baseURL });
      await signInViaApi(context, 'mariam');
      await use(context);
      await context.dispose();
    },
    { scope: 'worker' },
  ],
  language: ['en', { option: true }],
  signedInAs: ['ahmed', { option: true }],
  page: async ({ page, language, signedInAs, adminApi }, use) => {
    await page.addInitScript((lang) => {
      if (!sessionStorage.getItem('e2e.initialised')) {
        localStorage.setItem('strategy.language', lang);
        localStorage.setItem('strategy.theme', 'light');
        sessionStorage.setItem('e2e.initialised', '1');
      }
    }, language);
    if (signedInAs === 'mariam') {
      await page.context().addCookies((await adminApi.storageState()).cookies);
    } else if (signedInAs) {
      await signInViaApi(page, signedInAs);
    }
    if (signedInAs) {
      // The profile's language wins after sign-in; keep it on this project's language.
      await savePreferences(page, language);
    }
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
  const response = await apiPost(page, '/api/v1/sample/items', body);
  expect(response.status()).toBe(201);
  return (await response.json()) as { id: string };
}
