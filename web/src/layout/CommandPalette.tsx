import * as DialogPrimitive from '@radix-ui/react-dialog';
import { useNavigate } from '@tanstack/react-router';
import { Command } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { isUiTheme, useTheme } from '@/app/theme';
import { canIn, useAccess } from '@/features/access/session';
import { useAppName, useSavePreferences, useVisibleApps } from '@/features/appearance/api';
import { useMe } from '@/features/auth/session';
import { normaliseSearch } from '@/features/requests/search';
import { useGlobalSearchGroups } from '@/features/search/GlobalSearch';
import { isLanguage } from '@/i18n';
import { writePreference } from '@/lib/preferences';
import { Button, CommandSearch, type CommandSearchGroup } from '@/ui';
import { useTabLabel } from './WorkspaceShell';

type PaletteEntry = { kind: 'link'; to: string } | { kind: 'run'; run: () => void };

/**
 * Command palette of the Modern theme (SPEC-ADDENDUM §C4, ADR 0174): Ctrl / ⌘ + K. Records come from the Phase 19 global search (same
 * permission-filtered results), pages are the apps and tabs the user can open, actions are the visible apps' quick actions plus language,
 * mode and — when allowed — theme.
 */
export function CommandPalette() {
  const { t } = useTranslation('appearance');
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === 'k' && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        setOpen(true);
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, []);

  return (
    <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
      <DialogPrimitive.Trigger asChild>
        <Button
          variant="outline"
          className="h-9 w-9 justify-start gap-2 px-0 text-muted-foreground max-sm:justify-center sm:w-64 sm:px-3"
          aria-keyshortcuts="Control+K Meta+K"
          data-testid="command-palette-open"
        >
          <Command aria-hidden />
          {/* Named by its visible text (WCAG 2.5.3); on phones the text is for screen readers only. */}
          <span className="sr-only flex-1 truncate text-start sm:not-sr-only sm:truncate">
            {t('palette.placeholder')}
          </span>
          <kbd className="hidden rounded border bg-muted px-1.5 text-[11px] lg:inline" dir="ltr">
            {t('palette.shortcut')}
          </kbd>
        </Button>
      </DialogPrimitive.Trigger>
      {open && <PaletteContent onClose={() => setOpen(false)} />}
    </DialogPrimitive.Root>
  );
}

function PaletteContent({ onClose }: { onClose: () => void }) {
  const { t, i18n } = useTranslation('appearance');
  const { t: ts } = useTranslation('search');
  const { t: tshell } = useTranslation('shell');
  const navigate = useNavigate();
  const [text, setText] = useState('');
  const records = useGlobalSearchGroups(text, true);
  const apps = useVisibleApps();
  const appName = useAppName();
  const tabLabel = useTabLabel();
  const me = useMe();
  const { setTheme, setUiTheme } = useTheme();
  const save = useSavePreferences();
  const access = useAccess();
  const translate = t as unknown as (key: string) => string;

  const { groups, entries } = useMemo(() => {
    const entries = new Map<string, PaletteEntry>();
    const term = normaliseSearch(text);
    const matches = (...labels: string[]) =>
      !term || labels.some((label) => normaliseSearch(label).includes(term));

    const pages = apps.flatMap((app) => {
      const name = appName(app);
      const appItem = matches(name)
        ? [{ id: `page:${app.code}`, label: name, description: translate(`apps.${app.code}.description`) }]
        : [];
      entries.set(`page:${app.code}`, { kind: 'link', to: app.href });
      const tabs = term
        ? app.visibleTabs
            .filter(
              (tab) =>
                tab.key !== 'home' && matches(tabLabel(app.code, tab), `${name} ${tabLabel(app.code, tab)}`),
            )
            .map((tab) => {
              const id = `page:${app.code}:${tab.key}`;
              entries.set(id, { kind: 'link', to: tab.to });
              return { id, label: tabLabel(app.code, tab), description: name };
            })
        : [];
      return [...appItem, ...tabs];
    });

    const quick = apps.flatMap((app) =>
      (app.quickActions ?? [])
        .filter((action) => canIn(access, ...action.permissions))
        .map((action) => ({ app, action })),
    );
    const actions: CommandSearchGroup['items'] = [];
    const seen = new Set<string>();
    for (const { app, action } of quick) {
      const label = translate(`quickActions.${action.key}`);
      const id = `action:${app.code}:${action.key}`;
      if (seen.has(action.to) || !matches(label, appName(app))) continue;
      seen.add(action.to);
      entries.set(id, { kind: 'link', to: action.to });
      actions.push({ id, label, description: appName(app) });
    }
    const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';
    const languageLabel = t('palette.switchLanguage', { language: tshell('language.switchTo') });
    if (matches(languageLabel)) {
      entries.set('action:language', {
        kind: 'run',
        run: () => {
          if (!isLanguage(otherLanguage)) return;
          writePreference('language', otherLanguage);
          void i18n.changeLanguage(otherLanguage);
          save.mutate({ language: otherLanguage });
        },
      });
      actions.push({ id: 'action:language', label: languageLabel });
    }
    if (me?.appearance.allowUserMode !== false) {
      for (const mode of ['light', 'dark', 'system'] as const) {
        const label = t(`palette.mode${mode === 'light' ? 'Light' : mode === 'dark' ? 'Dark' : 'System'}`);
        if (!matches(label)) continue;
        entries.set(`action:mode:${mode}`, {
          kind: 'run',
          run: () => {
            setTheme(mode);
            save.mutate({ theme: mode });
          },
        });
        actions.push({ id: `action:mode:${mode}`, label });
      }
    }
    if (me?.appearance.allowUserTheme) {
      for (const theme of me.appearance.enabledThemes.filter(isUiTheme)) {
        const label = t('palette.useTheme', { theme: t(`themes.${theme}`) });
        if (!matches(label)) continue;
        entries.set(`action:theme:${theme}`, {
          kind: 'run',
          run: () => {
            setUiTheme(theme);
            save.mutate({ uiTheme: theme });
          },
        });
        actions.push({ id: `action:theme:${theme}`, label });
      }
    }

    const groups: CommandSearchGroup[] = [
      ...records.groups.map((group) => ({ ...group, id: `records-${group.id}` })),
      { id: 'pages', heading: t('palette.pages'), items: pages.slice(0, term ? 8 : 20) },
      { id: 'actions', heading: t('palette.actions'), items: actions.slice(0, 8) },
    ];
    for (const group of records.groups) {
      for (const item of group.items) {
        const link = records.links.current.get(item.id);
        if (link) entries.set(item.id, { kind: 'link', to: link });
      }
    }
    return { groups, entries };
    // translate / labels change with the language; records with their results.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apps, access, text, records.groups, i18n.language, me?.appearance]);

  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-40 bg-black/30 backdrop-blur-[2px]" />
      <DialogPrimitive.Content
        className="motion-panel fixed inset-x-0 top-4 z-50 mx-auto flex w-[calc(100%-1rem)] max-w-2xl flex-col rounded-2xl border bg-card p-3 text-card-foreground shadow-2xl sm:top-24"
        aria-describedby={undefined}
        data-testid="command-palette"
        onEscapeKeyDown={onClose}
      >
        <DialogPrimitive.Title className="sr-only">{t('palette.open')}</DialogPrimitive.Title>
        <CommandSearch
          inline
          value={text}
          onValueChange={setText}
          groups={groups}
          onSelect={(item) => {
            const entry = entries.get(item.id);
            onClose();
            if (entry?.kind === 'link') void navigate({ to: entry.to as '/' });
            else entry?.run();
          }}
          label={t('palette.open')}
          placeholder={t('palette.placeholder')}
          clearLabel={ts('clear')}
          loading={records.loading}
          emptyText={groups.every((group) => group.items.length === 0) ? t('palette.empty') : undefined}
          onOpenChange={(next) => {
            if (!next) onClose();
          }}
          testId="command-palette-search"
        />
        <p className="hidden px-2 pt-2 text-xs text-muted-foreground sm:block">{t('palette.hint')}</p>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}
