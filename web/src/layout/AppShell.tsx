import { Outlet, useRouterState } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { isThemePreference, isUiTheme, useAppearance, useTheme } from '@/app/theme';
import { useMe } from '@/features/auth/session';
import { useRecordVisits } from '@/features/search/visits';
import { isLanguage } from '@/i18n';
import { writePreference } from '@/lib/preferences';
import { Sidebar } from './Sidebar';
import { SkipLink } from './SkipLink';
import { TopBar } from './TopBar';
import { WorkspaceShell } from './WorkspaceShell';

/**
 * App shell. Classic: one sidebar with every system on the start side (right in Arabic) and the top bar — unchanged since Phase 0.
 * Enterprise and Modern: the application-workspace shell (app launcher, per-app tabs or sidebar; docs/design/themes.md §4).
 */
export function AppShell() {
  const { workspace } = useAppearance();
  usePreferencesFromProfile();
  useRecordVisits();
  return workspace ? <WorkspaceShell /> : <ClassicShell />;
}

function ClassicShell() {
  const [menuOpen, setMenuOpen] = useState(false);
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  useEffect(() => setMenuOpen(false), [pathname]);

  return (
    <div className="flex min-h-dvh">
      <SkipLink />
      <Sidebar open={menuOpen} onClose={() => setMenuOpen(false)} />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar onOpenMenu={() => setMenuOpen(true)} />
        <main
          id="main"
          tabIndex={-1}
          className="mx-auto w-full max-w-7xl flex-1 px-4 py-6 outline-none md:px-8 md:py-8"
        >
          <Outlet />
        </main>
      </div>
    </div>
  );
}

/**
 * Language, theme and mode follow the user (saved on the profile; theme and mode resolved on the server from Settings → Appearance,
 * ADR 0171) and win over this browser's last values.
 */
function usePreferencesFromProfile() {
  const me = useMe();
  const { i18n } = useTranslation();
  const { setTheme, setUiTheme } = useTheme();
  const language = me?.language;
  const mode = me?.appearance.mode;
  const uiTheme = me?.appearance.theme;
  useEffect(() => {
    if (isLanguage(language) && language !== i18n.language) {
      writePreference('language', language);
      void i18n.changeLanguage(language);
    }
    // Only when the profile value changes (e.g. after sign-in), not on every language switch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [language]);
  useEffect(() => {
    if (isThemePreference(mode)) setTheme(mode);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode]);
  useEffect(() => {
    if (isUiTheme(uiTheme)) setUiTheme(uiTheme);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [uiTheme]);
}
