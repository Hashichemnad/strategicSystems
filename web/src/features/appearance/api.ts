import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { useAccess } from '@/features/access/session';
import { meKey, useMe } from '@/features/auth/session';
import { pickText } from '@/lib/format';
import { visibleApps, type ResolvedApp } from '@/layout/apps';

export type AppearanceSettings = components['schemas']['AppearanceSettings'];
export type AppearanceDocument = components['schemas']['SettingsDocumentOfAppearanceSettings'];

export const appearanceKey = ['settings', 'appearance'] as const;

/** Settings → Appearance (any signed-in user reads it: app overrides and landing rules shape their shell). */
export const appearanceQuery = queryOptions({
  queryKey: appearanceKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/appearance', { signal })),
  staleTime: 5 * 60_000,
});

export function useAppearanceSettings() {
  return useQuery(appearanceQuery);
}

export function useSaveAppearance() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { value: AppearanceSettings; version: number }) =>
      unwrap(await api.PUT('/api/v1/settings/appearance', { body: input })),
    onSuccess: (document) => {
      queryClient.setQueryData(appearanceKey, document);
      // The effective theme of everyone (and the sign-in pages' defaults) may have changed.
      void queryClient.invalidateQueries({ queryKey: meKey });
      void queryClient.invalidateQueries({ queryKey: ['settings', 'public'] });
    },
  });
}

export interface PreferencesInput {
  language?: string | null;
  theme?: string | null;
  uiTheme?: string | null;
  landingApp?: string | null;
  favouriteApps?: string[] | null;
}

/** Saves profile preferences (null fields stay unchanged; "" clears a theme choice or default app). */
export function useSavePreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: PreferencesInput) => {
      const body = {
        language: input.language ?? null,
        theme: input.theme ?? null,
        uiTheme: input.uiTheme ?? null,
        landingApp: input.landingApp ?? null,
        favouriteApps: input.favouriteApps ?? null,
      };
      unwrap(await api.PUT('/api/v1/identity/me/preferences', { body }));
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: meKey }),
  });
}

/** The apps the signed-in user can open, with Settings → Appearance overrides applied. */
export function useVisibleApps(): ResolvedApp[] {
  const access = useAccess();
  const settings = useAppearanceSettings().data;
  return visibleApps(access, settings?.value?.apps ?? {});
}

/** The display name of an app in the user's language: the admin's name, else the translation. */
export function useAppName(): (app: Pick<ResolvedApp, 'code' | 'nameAr' | 'nameEn'>) => string {
  const { t, i18n } = useTranslation('appearance');
  return (app) => pickText(i18n.language, app.nameAr, app.nameEn) || t(`apps.${app.code}.name`);
}

/** The user's favourite apps (profile). */
export function useFavouriteApps(): { favourites: string[]; toggle: (code: string) => void } {
  const me = useMe();
  const save = useSavePreferences();
  const favourites = me?.appearance.favouriteApps ?? [];
  return {
    favourites,
    toggle: (code) =>
      save.mutate({
        favouriteApps: favourites.includes(code)
          ? favourites.filter((app) => app !== code)
          : [...favourites, code],
      }),
  };
}
