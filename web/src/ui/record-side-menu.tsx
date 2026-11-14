import * as DialogPrimitive from '@radix-ui/react-dialog';
import { ChevronDown, PanelLeft, X } from 'lucide-react';
import { useEffect, useRef, useState, useSyncExternalStore, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppearance, type UiTheme } from '@/app/theme';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { Button } from './button';

/** One entry of a record page's side menu (ADR 0221): a section of the record, shown alone in the content area when active. */
export interface RecordMenuItem {
  /** Stable key, written to `?tab=` by the page (the router schemas keep their values). */
  key: string;
  label: string;
  /** Item count shown beside the label (related lists). */
  count?: number;
  /** Not offered to this user / for this record (the item and its content are left out). */
  hidden?: boolean;
  /** Test id of the menu item (defaults to `record-section-<key>`). */
  testId?: string;
}

/** Below Tailwind's `md` breakpoint the menu is a drawer (docs/design/themes.md §4, tested at 360 px). */
export const narrowQuery = '(max-width: 767.98px)';

function subscribe(onChange: () => void) {
  const media = window.matchMedia(narrowQuery);
  media.addEventListener('change', onChange);
  return () => media.removeEventListener('change', onChange);
}

/** True below the `md` breakpoint (the record side menu becomes a drawer). */
export function useNarrowViewport(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(narrowQuery).matches,
    () => false,
  );
}

const containerClasses: Record<UiTheme, string> = {
  classic: 'rounded-lg border bg-card p-2',
  enterprise: 'rounded-lg border border-t-[3px] border-t-app-accent bg-card p-2 shadow-[var(--shadow-card)]',
  modern: 'rounded-xl border bg-card p-2 shadow-[var(--shadow-card)]',
};

const itemClasses: Record<UiTheme, { base: string; active: string; idle: string }> = {
  classic: {
    base: 'rounded-md px-3 py-2',
    active: 'bg-accent font-medium text-accent-foreground',
    idle: 'text-muted-foreground hover:bg-muted hover:text-foreground',
  },
  enterprise: {
    base: 'rounded-[var(--radius)] border-s-[3px] px-3 py-1.5',
    active: 'border-s-app-accent bg-muted font-semibold text-foreground',
    idle: 'border-s-transparent text-muted-foreground hover:bg-muted/60 hover:text-foreground',
  },
  modern: {
    base: 'relative rounded-lg px-3 py-2',
    active:
      'bg-muted font-semibold text-foreground before:absolute before:inset-y-2 before:start-0 before:w-1 before:rounded-full before:bg-app-accent',
    idle: 'text-muted-foreground hover:bg-muted hover:text-foreground',
  },
};

interface RecordSideMenuProps {
  items: readonly RecordMenuItem[];
  active: string;
  onSelect: (key: string) => void;
  /** Id of the content region the items control. */
  controls?: string;
  className?: string;
  /** Test id of the nav (default `record-side-menu`). */
  testId?: string;
}

/**
 * The record page's only section navigation (ADR 0221): every section in the page's order with its count, `aria-current="page"` on
 * the active one, inside a named nav landmark. Arrow keys (Up / Down, Home / End) move between items; Tab enters at the active item
 * (roving tab index) so a long menu (the project page) is one Tab stop. Logical properties only, so it mirrors in RTL.
 */
export function RecordSideMenu({
  items,
  active,
  onSelect,
  controls,
  className,
  testId,
}: RecordSideMenuProps) {
  const { t, i18n } = useTranslation('appearance');
  const { uiTheme } = useAppearance();
  const visible = items.filter((item) => !item.hidden);
  const [focusKey, setFocusKey] = useState<string>();
  const list = useRef<HTMLUListElement>(null);
  const tabStop = visible.some((item) => item.key === focusKey) ? focusKey : active;
  const styles = itemClasses[uiTheme];

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const buttons = Array.from(
      list.current?.querySelectorAll<HTMLButtonElement>('button[data-section]') ?? [],
    );
    const index = buttons.findIndex((button) => button === document.activeElement);
    if (index < 0) return;
    const next =
      event.key === 'ArrowDown'
        ? (index + 1) % buttons.length
        : event.key === 'ArrowUp'
          ? (index - 1 + buttons.length) % buttons.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? buttons.length - 1
              : -1;
    if (next < 0) return;
    event.preventDefault();
    buttons[next]?.focus();
  };

  return (
    <nav
      aria-label={t('record.sections')}
      className={cn(containerClasses[uiTheme], className)}
      data-testid={testId ?? 'record-side-menu'}
    >
      <ul ref={list} className="flex flex-col gap-0.5">
        {visible.map((item) => {
          const current = item.key === active;
          return (
            <li key={item.key}>
              <button
                type="button"
                data-section={item.key}
                data-testid={item.testId ?? `record-section-${item.key}`}
                aria-current={current ? 'page' : undefined}
                aria-controls={controls}
                tabIndex={item.key === tabStop ? 0 : -1}
                onFocus={() => setFocusKey(item.key)}
                onKeyDown={onKeyDown}
                onClick={() => onSelect(item.key)}
                className={cn(
                  'flex w-full items-center justify-between gap-2 text-start text-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
                  styles.base,
                  current ? styles.active : styles.idle,
                )}
              >
                <span className="min-w-0 break-words">{item.label}</span>
                {item.count !== undefined && (
                  <span
                    className={cn(
                      'shrink-0 rounded-full px-2 text-xs tabular-nums text-muted-foreground',
                      current ? 'bg-card' : 'bg-muted',
                    )}
                  >
                    {formatNumber(item.count, i18n.language)}
                    <span className="sr-only"> — {t('record.items', { count: item.count })}</span>
                  </span>
                )}
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

interface RecordSectionsDrawerProps {
  items: readonly RecordMenuItem[];
  active: string;
  onSelect: (key: string) => void;
  controls?: string;
}

/**
 * Phones (below `md`): the side menu in a sheet from the inline-start edge, opened by a "Sections" button that names the current
 * section. Choosing a section closes it; Radix returns focus to the button (WCAG 2.4.3).
 */
export function RecordSectionsDrawer({ items, active, onSelect, controls }: RecordSectionsDrawerProps) {
  const { t } = useTranslation('appearance');
  const { t: tc } = useTranslation();
  const [open, setOpen] = useState(false);
  const narrow = useNarrowViewport();
  const current = items.find((item) => item.key === active && !item.hidden);

  useEffect(() => {
    if (!narrow) setOpen(false);
  }, [narrow]);

  return (
    <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
      <DialogPrimitive.Trigger asChild>
        <Button
          variant="outline"
          className="w-full justify-between gap-2"
          aria-controls={undefined}
          data-testid="record-sections-button"
        >
          <span className="flex min-w-0 items-center gap-2">
            <PanelLeft className="size-4 shrink-0 rtl:-scale-x-100" aria-hidden />
            <span className="text-muted-foreground">{t('record.sectionsButton')}</span>
            <span className="truncate font-semibold">{current?.label}</span>
          </span>
          <ChevronDown className="size-4 shrink-0" aria-hidden />
        </Button>
      </DialogPrimitive.Trigger>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-40 bg-black/40" />
        <DialogPrimitive.Content
          aria-describedby={undefined}
          className="fixed inset-y-0 start-0 z-50 flex w-72 max-w-[85vw] flex-col gap-3 overflow-y-auto border-e bg-background p-3 text-foreground shadow-xl"
          data-testid="record-sections-drawer"
        >
          <div className="flex items-center justify-between gap-2 px-1">
            <DialogPrimitive.Title className="text-base font-semibold">
              {t('record.sections')}
            </DialogPrimitive.Title>
            <DialogPrimitive.Close asChild>
              <Button variant="ghost" size="icon" className="size-8" aria-label={tc('actions.close')}>
                <X aria-hidden />
              </Button>
            </DialogPrimitive.Close>
          </div>
          <RecordSideMenu
            items={items}
            active={active}
            controls={controls}
            testId="record-sections-drawer-menu"
            onSelect={(key) => {
              setOpen(false);
              onSelect(key);
            }}
          />
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
