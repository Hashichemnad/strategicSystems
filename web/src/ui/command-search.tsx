import { Search, X } from 'lucide-react';
import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
  type Ref,
} from 'react';
import { cn } from '@/lib/cn';
import { Skeleton } from './feedback';

export interface CommandSearchItem {
  /** Unique across all groups. */
  id: string;
  label: string;
  /** Second line (type, what matched…). */
  description?: string;
  /** Trailing content (status chip…). */
  meta?: ReactNode;
}

export interface CommandSearchGroup {
  id: string;
  heading: string;
  items: CommandSearchItem[];
}

export interface CommandSearchProps {
  value: string;
  onValueChange: (value: string) => void;
  groups: CommandSearchGroup[];
  onSelect: (item: CommandSearchItem) => void;
  /** Accessible name of the input. */
  label: string;
  placeholder?: string;
  loading?: boolean;
  /** Shown in the list when there is nothing to show (e.g. "No results" or "Type at least 2 characters"). */
  emptyText?: string;
  /** Short hint shown in the input on wide screens (e.g. "Ctrl K"). */
  shortcutHint?: string;
  clearLabel: string;
  inputRef?: Ref<HTMLInputElement>;
  className?: string;
  onOpenChange?: (open: boolean) => void;
  testId?: string;
  /**
   * The list is part of the page flow and always shown (the command palette's dialog, ADR 0174) instead of a popup under the input;
   * Escape reports `onOpenChange(false)` so the dialog can close.
   */
  inline?: boolean;
}

/**
 * Search box with grouped results (ARIA 1.2 combobox + listbox, CLAUDE.md §10): ↑ / ↓ move (wrapping), Home / End, Enter opens,
 * Esc closes (a second Esc clears the text). Focus stays in the input; the active option is announced through
 * `aria-activedescendant`. Used by the top-bar global search and, in Phase 20, the command palette.
 */
export function CommandSearch({
  value,
  onValueChange,
  groups,
  onSelect,
  label,
  placeholder,
  loading = false,
  emptyText,
  shortcutHint,
  clearLabel,
  inputRef,
  className,
  onOpenChange,
  testId = 'command-search',
  inline = false,
}: CommandSearchProps) {
  const baseId = useId();
  const listId = `${baseId}-list`;
  const [openState, setOpenState] = useState(false);
  const open = inline || openState;
  const [active, setActive] = useState(0);
  const containerRef = useRef<HTMLDivElement>(null);
  const items = useMemo(() => groups.flatMap((g) => g.items), [groups]);
  const optionId = (index: number) => `${baseId}-option-${index}`;

  const setOpen = (next: boolean) => {
    setOpenState(next);
    onOpenChange?.(next);
  };

  // A new result set starts at the first option.
  useEffect(() => setActive(0), [groups]);

  // Keep the active option in view while moving with the keyboard.
  useEffect(() => {
    if (!open) return;
    document.getElementById(optionId(active))?.scrollIntoView({ block: 'nearest' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, open]);

  const choose = (item: CommandSearchItem | undefined) => {
    if (!item) return;
    onSelect(item);
    if (!inline) setOpen(false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!open) setOpen(true);
        else if (items.length > 0) setActive((i) => (i + 1) % items.length);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!open) setOpen(true);
        else if (items.length > 0) setActive((i) => (i - 1 + items.length) % items.length);
        break;
      case 'Home':
        if (open && items.length > 0) {
          event.preventDefault();
          setActive(0);
        }
        break;
      case 'End':
        if (open && items.length > 0) {
          event.preventDefault();
          setActive(items.length - 1);
        }
        break;
      case 'Enter':
        if (open && items[active]) {
          event.preventDefault();
          choose(items[active]);
        }
        break;
      case 'Escape':
        event.preventDefault();
        if (inline) onOpenChange?.(false);
        else if (open) setOpen(false);
        else if (value) onValueChange('');
        break;
      default:
        break;
    }
  };

  const expanded = open && (items.length > 0 || loading || Boolean(emptyText));
  let index = -1;

  return (
    <div
      ref={containerRef}
      className={cn('relative w-full', className)}
      data-testid={testId}
      onBlur={(event) => {
        if (!inline && !containerRef.current?.contains(event.relatedTarget)) setOpen(false);
      }}
    >
      <div className="relative">
        <Search
          className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden
        />
        <input
          ref={inputRef}
          type="search"
          role="combobox"
          aria-label={label}
          aria-expanded={expanded}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={expanded && items[active] ? optionId(active) : undefined}
          autoComplete="off"
          spellCheck={false}
          value={value}
          placeholder={placeholder}
          onChange={(event) => {
            onValueChange(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onClick={() => setOpen(true)}
          onKeyDown={onKeyDown}
          className="h-10 w-full rounded-md border border-input bg-card ps-9 pe-16 text-sm placeholder:text-muted-foreground [&::-webkit-search-cancel-button]:hidden"
          data-testid={`${testId}-input`}
        />
        <div className="absolute end-2 top-1/2 flex -translate-y-1/2 items-center gap-1">
          {value ? (
            <button
              type="button"
              className="rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground"
              aria-label={clearLabel}
              onClick={() => onValueChange('')}
            >
              <X className="size-4" aria-hidden />
            </button>
          ) : (
            shortcutHint && (
              <kbd
                className="hidden rounded border bg-muted px-1.5 text-[11px] text-muted-foreground lg:inline"
                dir="ltr"
              >
                {shortcutHint}
              </kbd>
            )
          )}
        </div>
      </div>
      <div
        id={listId}
        role="listbox"
        aria-label={label}
        hidden={!expanded}
        className={
          inline
            ? 'mt-2 max-h-[min(60vh,28rem)] overflow-y-auto p-1'
            : 'absolute inset-x-0 top-full z-50 mt-1 max-h-[70vh] overflow-y-auto rounded-md border bg-card p-1 shadow-lg sm:end-auto sm:w-[min(36rem,calc(100vw-2rem))]'
        }
        data-testid={`${testId}-list`}
      >
        {loading && items.length === 0 && (
          <div className="space-y-2 p-2" aria-hidden>
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        )}
        {!loading && items.length === 0 && emptyText && (
          <div role="presentation" className="px-3 py-6 text-center text-sm text-muted-foreground">
            {emptyText}
          </div>
        )}
        {groups
          .filter((group) => group.items.length > 0)
          .map((group) => {
            const headingId = `${baseId}-group-${group.id}`;
            return (
              <div key={group.id} role="group" aria-labelledby={headingId} className="py-1">
                <div
                  id={headingId}
                  role="presentation"
                  className="px-2 py-1 text-xs font-semibold text-muted-foreground uppercase"
                >
                  {group.heading}
                </div>
                {group.items.map((item) => {
                  index += 1;
                  const position = index;
                  const selected = position === active;
                  return (
                    // Keyboard selection happens in the combobox input (aria-activedescendant); the option itself is never focused.
                    // eslint-disable-next-line jsx-a11y/click-events-have-key-events
                    <div
                      key={item.id}
                      id={optionId(position)}
                      role="option"
                      aria-selected={selected}
                      tabIndex={-1}
                      className={cn(
                        'flex cursor-pointer items-center gap-3 rounded px-2 py-1.5 text-sm',
                        selected ? 'bg-accent text-accent-foreground' : 'hover:bg-muted',
                      )}
                      onMouseDown={(event) => event.preventDefault()} // keep focus in the input
                      onMouseMove={() => setActive(position)}
                      onClick={() => choose(item)}
                      data-testid={`${testId}-option`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="truncate font-medium">{item.label}</div>
                        {item.description && (
                          <div className="truncate text-xs text-muted-foreground">{item.description}</div>
                        )}
                      </div>
                      {item.meta && <div className="shrink-0">{item.meta}</div>}
                    </div>
                  );
                })}
              </div>
            );
          })}
      </div>
    </div>
  );
}
