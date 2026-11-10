import * as DialogPrimitive from '@radix-ui/react-dialog';
import { Link } from '@tanstack/react-router';
import { LayoutGrid, Search, Star, X } from 'lucide-react';
import { useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppName, useFavouriteApps, useVisibleApps } from '@/features/appearance/api';
import { normaliseSearch as normaliseForMatch } from '@/features/requests/search';
import { cn } from '@/lib/cn';
import { Button } from '@/ui';
import type { ResolvedApp } from './apps';
import { AppIcon } from './WorkspaceShell';

/**
 * App launcher (SPEC-ADDENDUM §C2): tiles for the apps the user can open — favourites first, then the systems, then the shared apps;
 * search in both languages; star to pin. Arrow keys move between tiles, Enter opens, Escape closes.
 */
export function AppLauncher() {
  const { t } = useTranslation('appearance');
  const [open, setOpen] = useState(false);
  return (
    <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
      <DialogPrimitive.Trigger asChild>
        <Button variant="ghost" size="icon" aria-label={t('launcher.open')} data-testid="app-launcher">
          <LayoutGrid aria-hidden />
        </Button>
      </DialogPrimitive.Trigger>
      {open && <LauncherContent onClose={() => setOpen(false)} />}
    </DialogPrimitive.Root>
  );
}

function LauncherContent({ onClose }: { onClose: () => void }) {
  const { t, i18n } = useTranslation('appearance');
  const { t: tc } = useTranslation();
  const apps = useVisibleApps();
  const appName = useAppName();
  const { favourites, toggle } = useFavouriteApps();
  const [query, setQuery] = useState('');
  const gridRef = useRef<HTMLDivElement>(null);
  const translate = t as unknown as (key: string) => string;

  const sections = useMemo(() => {
    const term = normaliseForMatch(query);
    const matching = apps.filter((app) => {
      if (!term) return true;
      const names = [appName(app), app.nameAr, app.nameEn, translate(`apps.${app.code}.description`)];
      return names.some((name) => name && normaliseForMatch(name).includes(term));
    });
    const favouriteApps = favourites
      .map((code) => matching.find((app) => app.code === code))
      .filter((app): app is ResolvedApp => !!app);
    return [
      { key: 'favourites', title: t('launcher.favourites'), apps: favouriteApps },
      { key: 'systems', title: t('launcher.systems'), apps: matching.filter((a) => a.group === 'systems') },
      { key: 'shared', title: t('launcher.shared'), apps: matching.filter((a) => a.group === 'shared') },
    ].filter((section) => section.apps.length > 0);
    // appName / translate change with the language.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apps, favourites, query, i18n.language]);

  // Arrow keys move between the tiles' links (grid order; reading direction follows the document).
  const onGridKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const links = Array.from(gridRef.current?.querySelectorAll<HTMLElement>('[data-app-tile]') ?? []);
    const index = links.indexOf(document.activeElement as HTMLElement);
    if (index < 0) return;
    const rtl = document.documentElement.dir === 'rtl';
    const columns = Math.max(
      1,
      Math.round(
        (gridRef.current?.clientWidth ?? 1) / Math.max(1, links[0]?.parentElement?.clientWidth ?? 1),
      ),
    );
    const step: Record<string, number> = {
      ArrowRight: rtl ? -1 : 1,
      ArrowLeft: rtl ? 1 : -1,
      ArrowDown: columns,
      ArrowUp: -columns,
    };
    const delta = step[event.key];
    if (delta === undefined) return;
    event.preventDefault();
    links[Math.min(links.length - 1, Math.max(0, index + delta))]?.focus();
  };

  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-40 bg-black/40" />
      <DialogPrimitive.Content
        className="motion-panel fixed inset-x-0 top-4 z-50 mx-auto flex max-h-[calc(100dvh-2rem)] w-[calc(100%-1rem)] max-w-4xl flex-col gap-4 overflow-y-auto rounded-xl border bg-card p-4 text-card-foreground shadow-xl sm:top-16 sm:p-6"
        aria-describedby={undefined}
        data-testid="app-launcher-dialog"
      >
        <div className="flex items-center justify-between gap-4">
          <DialogPrimitive.Title className="text-lg font-semibold">
            {t('launcher.title')}
          </DialogPrimitive.Title>
          <DialogPrimitive.Close asChild>
            <Button variant="ghost" size="icon" className="size-8" aria-label={tc('actions.close')}>
              <X aria-hidden />
            </Button>
          </DialogPrimitive.Close>
        </div>
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            aria-label={t('launcher.search')}
            placeholder={t('launcher.search')}
            className="h-[var(--control-h)] w-full rounded-md border border-input bg-card ps-9 pe-3 text-sm"
            data-testid="app-launcher-search"
          />
        </div>
        {/* Arrow-key navigation between the tile links inside (each tile is a link; the grid only moves focus). */}
        {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
        <div ref={gridRef} onKeyDown={onGridKeyDown} className="flex flex-col gap-5">
          {sections.length === 0 && (
            <p className="py-8 text-center text-sm text-muted-foreground">{t('launcher.none')}</p>
          )}
          {sections.map((section) => (
            <section key={section.key} aria-labelledby={`launcher-${section.key}`}>
              <h3 id={`launcher-${section.key}`} className="pb-2 text-sm font-semibold text-muted-foreground">
                {section.title}
              </h3>
              <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
                {section.apps.map((app) => {
                  const name = appName(app);
                  const favourite = favourites.includes(app.code);
                  return (
                    <li
                      key={app.code}
                      className="group relative flex items-start gap-3 rounded-lg border bg-card p-3 hover:border-app-accent hover:bg-muted/40"
                      style={{ ['--app-accent' as string]: app.accent }}
                      data-testid={`app-tile-${app.code}`}
                    >
                      <AppIcon app={app} />
                      <div className="min-w-0 flex-1 pe-7">
                        <Link
                          to={app.href as '/'}
                          onClick={onClose}
                          className="font-semibold after:absolute after:inset-0 after:rounded-lg focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:outline-ring"
                          data-app-tile
                        >
                          {name}
                        </Link>
                        <p className="text-xs text-muted-foreground">
                          {translate(`apps.${app.code}.description`)}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => toggle(app.code)}
                        aria-pressed={favourite}
                        aria-label={t(favourite ? 'launcher.removeFavourite' : 'launcher.addFavourite', {
                          name,
                        })}
                        className={cn(
                          'absolute end-2 top-2 z-10 rounded p-1 hover:bg-muted',
                          favourite ? 'text-warning' : 'text-muted-foreground',
                        )}
                        data-testid={`app-favourite-${app.code}`}
                      >
                        <Star className={cn('size-4', favourite && 'fill-current')} aria-hidden />
                      </button>
                    </li>
                  );
                })}
              </ul>
            </section>
          ))}
        </div>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}
