import { queryOptions, useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { useTheme } from '@/app/theme';
import { resources } from '@/i18n';
import { pickText, setFormatDefaults } from '@/lib/format';

export type PublicSettings = components['schemas']['PublicSettingsDto'];
export type TranslationOverrides = components['schemas']['TranslationOverridesDto'];

export const publicSettingsKey = ['settings', 'public'] as const;
export const translationOverridesKey = ['settings', 'translations'] as const;

/** Branding and regional settings (public: the sign-in page uses them too). */
export const publicSettingsQuery = queryOptions({
  queryKey: publicSettingsKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/public', { signal })),
  staleTime: 5 * 60_000,
});

export const translationOverridesQuery = queryOptions({
  queryKey: translationOverridesKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/translations', { signal })),
  staleTime: 5 * 60_000,
  refetchInterval: 5 * 60_000,
});

export function usePublicSettings(): PublicSettings | undefined {
  return useQuery(publicSettingsQuery).data;
}

/** Organisation name in the user's language and the logo for the current theme, when set in Settings → Branding. */
export function useBranding(): { name: string | undefined; logoUrl: string | undefined } {
  const settings = usePublicSettings();
  const { i18n } = useTranslation();
  const { theme } = useTheme();
  const name =
    pickText(i18n.language, settings?.organisationNameAr, settings?.organisationNameEn) || undefined;
  const kind =
    theme === 'dark' && settings?.hasLogoDark
      ? 'logo-dark'
      : settings?.hasLogoLight
        ? 'logo-light'
        : settings?.hasLogoDark
          ? 'logo-dark'
          : undefined;
  return { name, logoUrl: kind ? `/api/v1/settings/branding/logo/${kind}` : undefined };
}

/**
 * Applies administrators' translation overrides over the bundled AR/EN resources. Each apply starts from the bundled
 * text, so a cleared override goes back to the default. Keys are dotted paths inside a namespace (`nav.home`).
 */
export function applyTranslationOverrides(
  i18n: {
    addResourceBundle: (
      lng: string,
      ns: string,
      resources: object,
      deep?: boolean,
      overwrite?: boolean,
    ) => unknown;
  },
  overrides: TranslationOverrides,
): void {
  for (const language of Object.keys(resources) as (keyof typeof resources)[]) {
    for (const [namespace, bundle] of Object.entries(resources[language])) {
      i18n.addResourceBundle(language, namespace, structuredClone(bundle), true, true);
    }
  }

  for (const [language, namespaces] of Object.entries(overrides.languages)) {
    for (const [namespace, entries] of Object.entries(namespaces)) {
      const nested: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(entries)) {
        const parts = key.split('.');
        let node = nested;
        const leaf = parts.pop() ?? key;
        for (const part of parts) {
          node = (node[part] ??= {}) as Record<string, unknown>;
        }
        node[leaf] = value;
      }
      i18n.addResourceBundle(language, namespace, nested, true, true);
    }
  }
}

/** Mounted once in <App />: regional formatting, brand colour and translation overrides follow Settings. */
export function SettingsEffects(): null {
  const { i18n } = useTranslation();
  const settings = useQuery(publicSettingsQuery).data;
  const overrides = useQuery(translationOverridesQuery).data;

  useEffect(() => {
    if (!settings) return;
    setFormatDefaults({
      arabicIndicDigits: settings.regional.arabicIndicDigits,
      hijri: settings.regional.hijriDisplay,
      timeZone: settings.regional.timeZone,
    });
    const root = document.documentElement;
    if (settings.primaryColor && settings.primaryColor.toLowerCase() !== '#0f766e')
      root.style.setProperty('--brand-primary', settings.primaryColor);
    else root.style.removeProperty('--brand-primary');
  }, [settings]);

  useEffect(() => {
    if (overrides) applyTranslationOverrides(i18n, overrides);
  }, [overrides, i18n]);

  return null;
}
