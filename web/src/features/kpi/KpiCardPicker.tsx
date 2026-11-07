import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { pickText } from '@/lib/format';
import { Button, Input } from '@/ui';

export type KpiCardLookup = components['schemas']['KpiCardLookupDto'];

const PAGE_SIZE = 20;

/** Server search of the KPI card lookup (ADR 0169): pages of 20, scoped to the cards the user may read, Arabic-normalised. */
export function useKpiCardLookup(search: string, enabled = true) {
  return useInfiniteQuery({
    queryKey: ['kpi', 'cards', 'lookup', search],
    initialPageParam: 1,
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/cards/lookup', {
          params: { query: { page: pageParam, pageSize: PAGE_SIZE, ...(search ? { search } : {}) } },
          signal,
        }),
      ),
    getNextPageParam: (last) => (last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** The selected card's label, even when the card is not on the current search page. */
export function useKpiCardLabel(id: string | null | undefined) {
  return useQuery({
    queryKey: ['kpi', 'cards', 'lookup', 'ids', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/cards/lookup', { params: { query: { ids: [id ?? ''] } }, signal }))
        .items[0] ?? null,
    enabled: Boolean(id),
    staleTime: 60_000,
  });
}

/**
 * The one KPI card picker (ADR 0169): an ARIA combobox with server search as you type (debounced), *Show more* paging, ↑ ↓ Home End Enter Esc,
 * and the selected card's code and name. Clearing sets null. Lists only the cards the user may read; logical CSS, works in RTL.
 */
export function KpiCardPicker({
  value,
  onChange,
  disabled,
  'aria-label': ariaLabel,
  testId,
  fallbackLabel,
}: {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  'aria-label'?: string;
  testId?: string;
  /** Shown for the selected card while (or if) the lookup cannot resolve it, e.g. a card the user may no longer read. */
  fallbackLabel?: string;
}) {
  const { t, i18n } = useTranslation('kpi');
  const listId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [text]);
  useEffect(() => setActive(0), [debounced]);
  const selected = useKpiCardLabel(value);
  const results = useKpiCardLookup(debounced, open);
  const items = results.data?.pages.flatMap((p) => p.items) ?? [];
  const total = results.data?.pages[0]?.totalCount ?? 0;
  const label = (c: { code: string; nameAr?: string | null; nameEn?: string | null }) =>
    `${c.code} ${pickText(i18n.language, c.nameAr, c.nameEn)}`.trim();
  const optionId = (index: number) => `${listId}-o${index}`;

  const choose = (card: KpiCardLookup) => {
    onChange(card.id);
    setText('');
    setOpen(false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const last = items.length - 1;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!open) setOpen(true);
        else setActive((i) => Math.min(i + 1, last));
        break;
      case 'ArrowUp':
        event.preventDefault();
        setActive((i) => Math.max(i - 1, 0));
        break;
      case 'Home':
        if (open) {
          event.preventDefault();
          setActive(0);
        }
        break;
      case 'End':
        if (open) {
          event.preventDefault();
          setActive(Math.max(last, 0));
        }
        break;
      case 'Enter':
        if (open && items[active]) {
          event.preventDefault();
          choose(items[active]);
        }
        break;
      case 'Escape':
        if (open) {
          event.preventDefault();
          setOpen(false);
        }
        break;
      default:
        break;
    }
  };

  useEffect(() => {
    const option = open ? document.getElementById(optionId(active)) : null;
    if (option && 'scrollIntoView' in option) option.scrollIntoView({ block: 'nearest' });
    // optionId is stable for a given listId
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, open]);

  if (value && !open) {
    const shown = selected.data ? label(selected.data) : selected.isPending ? '…' : (fallbackLabel ?? '—');
    return (
      <div className="flex items-center gap-2" data-testid={testId}>
        <span
          className="min-w-0 flex-1 truncate text-sm"
          data-testid={testId ? `${testId}-selected` : undefined}
        >
          <bdi>{shown}</bdi>
        </span>
        {!disabled && (
          <>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                setOpen(true);
                window.setTimeout(() => inputRef.current?.focus(), 0);
              }}
            >
              {t('picker.change')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => onChange(null)} aria-label={t('picker.clear')}>
              <X className="size-4" aria-hidden />
            </Button>
          </>
        )}
      </div>
    );
  }

  return (
    <div className="relative" data-testid={testId}>
      <Input
        ref={inputRef}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && items[active] ? optionId(active) : undefined}
        aria-label={ariaLabel ?? t('picker.search')}
        placeholder={t('picker.search')}
        disabled={disabled}
        value={text}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
        onChange={(e) => {
          setText(e.target.value);
          setOpen(true);
        }}
      />
      {open && (
        <div className="absolute z-20 mt-1 w-full rounded-md border bg-popover p-1 shadow-md">
          <p className="sr-only" aria-live="polite">
            {results.isFetching ? t('picker.loading') : t('picker.results', { count: total })}
          </p>
          <ul
            id={listId}
            role="listbox"
            aria-label={ariaLabel ?? t('picker.search')}
            className="max-h-64 overflow-y-auto"
          >
            {items.map((c, index) => (
              // eslint-disable-next-line jsx-a11y/click-events-have-key-events -- options are chosen from the keyboard on the combobox (aria-activedescendant)
              <li
                key={c.id}
                id={optionId(index)}
                role="option"
                aria-selected={index === active}
                className={`cursor-pointer rounded px-2 py-1.5 text-start text-sm ${index === active ? 'bg-muted' : ''} ${c.id === value ? 'font-semibold' : ''}`}
                onMouseEnter={() => setActive(index)}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => choose(c)}
              >
                <bdi>{label(c)}</bdi>
              </li>
            ))}
          </ul>
          {results.isFetching && items.length === 0 && (
            <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('picker.loading')}</p>
          )}
          {results.data && !results.isFetching && items.length === 0 && (
            <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('picker.none')}</p>
          )}
          <div className="flex items-center justify-between gap-2 border-t pt-1">
            {results.hasNextPage ? (
              <Button
                size="sm"
                variant="ghost"
                loading={results.isFetchingNextPage}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => void results.fetchNextPage()}
              >
                {t('picker.more')}
              </Button>
            ) : (
              <span />
            )}
            <Button size="sm" variant="ghost" onClick={() => setOpen(false)}>
              {t('picker.close')}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
