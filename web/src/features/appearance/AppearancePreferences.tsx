import { useTranslation } from 'react-i18next';
import { isUiTheme, useTheme } from '@/app/theme';
import { useMe } from '@/features/auth/session';
import { Checkbox, FormField, Select, useToast } from '@/ui';
import { useAppName, useSavePreferences, useVisibleApps } from './api';

const DEFAULT = '__default';

/**
 * Profile → Preferences, appearance part (docs/design/themes.md §7): the theme (only when Settings → Appearance allows choosing, among
 * the enabled themes), the default app and favourite apps. The server refuses a choice that is not allowed (ADR 0171).
 */
export function AppearancePreferences() {
  const { t } = useTranslation('appearance');
  const me = useMe();
  const { setUiTheme } = useTheme();
  const apps = useVisibleApps();
  const appName = useAppName();
  const save = useSavePreferences();
  const toast = useToast();
  const appearance = me?.appearance;
  if (!appearance) return null;
  const saved = { onSuccess: () => toast({ title: t('profile.saved') }) };
  const themes = appearance.enabledThemes.filter(isUiTheme);
  const favourites = appearance.favouriteApps;

  return (
    <div className="flex flex-col gap-4 border-t pt-4" data-testid="appearance-preferences">
      <h3 className="font-semibold">{t('profile.section')}</h3>
      <div className="grid gap-4 sm:grid-cols-2">
        {appearance.allowUserTheme ? (
          <FormField label={t('profile.theme')}>
            <Select
              value={
                appearance.themeChoice && themes.includes(appearance.themeChoice as never)
                  ? appearance.themeChoice
                  : DEFAULT
              }
              options={[
                {
                  value: DEFAULT,
                  label: t('profile.themeDefault', { theme: t(`themes.${appearance.theme as 'classic'}`) }),
                },
                ...themes.map((theme) => ({ value: theme, label: t(`themes.${theme}`) })),
              ]}
              onValueChange={(value) => {
                if (isUiTheme(value)) setUiTheme(value);
                save.mutate({ uiTheme: value === DEFAULT ? '' : value }, saved);
              }}
            />
          </FormField>
        ) : (
          <p className="text-sm text-muted-foreground" data-testid="theme-locked">
            {t('profile.themeLocked', { theme: t(`themes.${appearance.theme as 'classic'}`) })}
          </p>
        )}
        <FormField label={t('profile.landingApp')}>
          <Select
            value={
              appearance.landingApp && apps.some((a) => a.code === appearance.landingApp)
                ? appearance.landingApp
                : DEFAULT
            }
            options={[
              { value: DEFAULT, label: t('profile.landingDefault') },
              ...apps.map((app) => ({ value: app.code, label: appName(app) })),
            ]}
            onValueChange={(value) => save.mutate({ landingApp: value === DEFAULT ? '' : value }, saved)}
          />
        </FormField>
      </div>
      <fieldset className="flex flex-col gap-2">
        <legend className="pb-1 text-sm font-medium">{t('profile.favourites')}</legend>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {apps.map((app) => (
            <Checkbox
              key={app.code}
              label={appName(app)}
              checked={favourites.includes(app.code)}
              onChange={(event) =>
                save.mutate(
                  {
                    favouriteApps: event.target.checked
                      ? [...favourites, app.code]
                      : favourites.filter((code) => code !== app.code),
                  },
                  saved,
                )
              }
            />
          ))}
        </div>
      </fieldset>
    </div>
  );
}
