import { createContext, use, useEffect, useState, type ReactNode } from 'react';
import { readPreference, writePreference } from '@/lib/preferences';

/** Light / dark mode (the profile's `theme` field, kept for compatibility). */
export type ThemePreference = 'light' | 'dark' | 'system';

/** Visual theme: token set + layout variant (SPEC-ADDENDUM §C1, docs/design/themes.md, ADR 0170). */
export type UiTheme = 'classic' | 'enterprise' | 'modern';

export const uiThemes: readonly UiTheme[] = ['classic', 'enterprise', 'modern'];

interface ThemeContextValue {
  /** Light / dark / system. */
  theme: ThemePreference;
  setTheme: (theme: ThemePreference) => void;
  uiTheme: UiTheme;
  setUiTheme: (theme: UiTheme) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'light' || value === 'dark' || value === 'system';
}

export function isUiTheme(value: unknown): value is UiTheme {
  return value === 'classic' || value === 'enterprise' || value === 'modern';
}

/**
 * Applies the theme's design tokens (`data-theme` on <html>) and light / dark mode (the `dark` class). The effective values come from
 * the server (`me.appearance`, resolved there — ADR 0171) or, signed out, from the installation defaults; the last values are kept in
 * this browser so a reload does not flash another theme.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(() => {
    const stored = readPreference('theme');
    return isThemePreference(stored) ? stored : 'system';
  });
  const [uiTheme, setUiThemeState] = useState<UiTheme>(() => {
    const stored = readPreference('uiTheme');
    return isUiTheme(stored) ? stored : 'classic';
  });

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () =>
      document.documentElement.classList.toggle(
        'dark',
        theme === 'dark' || (theme === 'system' && media.matches),
      );
    apply();
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [theme]);

  useEffect(() => {
    document.documentElement.dataset.theme = uiTheme;
  }, [uiTheme]);

  const setTheme = (next: ThemePreference) => {
    writePreference('theme', next);
    setThemeState(next);
  };
  const setUiTheme = (next: UiTheme) => {
    writePreference('uiTheme', next);
    setUiThemeState(next);
  };

  return <ThemeContext value={{ theme, setTheme, uiTheme, setUiTheme }}>{children}</ThemeContext>;
}

export function useTheme(): ThemeContextValue {
  const value = use(ThemeContext);
  if (!value) throw new Error('useTheme must be used inside <ThemeProvider>');
  return value;
}

/**
 * The layout variant of the current theme. Only the shell and the shared layout components (record layout, split view, stage stepper,
 * tabs, page header) read it — every other screen follows the tokens (docs/design/themes.md §4).
 */
export function useAppearance(): { uiTheme: UiTheme; workspace: boolean } {
  const value = use(ThemeContext);
  const uiTheme = value?.uiTheme ?? 'classic';
  return { uiTheme, workspace: uiTheme !== 'classic' };
}
