import { useNavigate } from '@tanstack/react-router';
import { Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import {
  Button,
  CommandSearch,
  StatusChip,
  type CommandSearchGroup,
  type CommandSearchItem,
  type Tone,
} from '@/ui';
import { minQueryLength, useRecentItems, useSearch, type SearchItem } from './api';

const tones = new Set<string>(['neutral', 'info', 'success', 'warning', 'danger']);
const matchedKeys = new Set([
  'number',
  'title',
  'externalId',
  'event',
  'subject',
  'location',
  'process',
  'card',
  'description',
  'documentNo',
  'requestNumber',
  'partner',
  'previousProgram',
  'deliverable',
  'auditor',
]);

function useDebounced(value: string, delay: number) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** True when the key press happens in a field the user is typing in (the `/` shortcut must not steal it). */
function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
}

/**
 * Top-bar global search (SPEC-ADDENDUM A1, ADR 0160): every system the user can open, grouped by system, recent items when empty.
 * `/` or Ctrl / ⌘ + K focuses it; on phones an icon opens it over the top bar.
 */
export function GlobalSearch() {
  const { t, i18n } = useTranslation('search');
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [expanded, setExpanded] = useState(false); // phones: the search covers the top bar
  const term = useDebounced(text.trim(), 200);
  const searching = term.length >= minQueryLength;
  const results = useSearch(term);
  const recent = useRecentItems(open && text.trim().length === 0);
  const language = i18n.language;

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const k = event.key.toLowerCase() === 'k' && (event.ctrlKey || event.metaKey);
      const slash =
        event.key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey && !isEditable(event.target);
      if (!k && !slash) return;
      event.preventDefault();
      setExpanded(true);
      window.requestAnimationFrame(() => inputRef.current?.focus());
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, []);

  const links = useRef(new Map<string, string>());
  const groups = useMemo<CommandSearchGroup[]>(() => {
    links.current.clear();
    const toItem = (item: SearchItem, withMatch: boolean): CommandSearchItem => {
      const id = `${item.type}:${item.id}`;
      links.current.set(id, item.link);
      const title = pickText(language, item.titleAr, item.titleEn);
      const type = pickText(language, item.typeNameAr, item.typeNameEn);
      const status = pickText(language, item.statusLabelAr, item.statusLabelEn) || item.statusCode;
      const matched =
        withMatch && item.matchedOn && item.matchedOn !== 'number' && item.matchedOn !== 'title'
          ? `${t(`matchedOn.${matchedKeys.has(item.matchedOn) ? item.matchedOn : 'other'}` as 'matchedOn.other')}: ${item.matchedText ?? ''}`
          : null;
      const tone = (item.tone && tones.has(item.tone) ? item.tone : 'neutral') as Tone;
      return {
        id,
        label: [item.number, title].filter(Boolean).join(' · '),
        description: [type, matched].filter(Boolean).join(' — '),
        meta: status ? <StatusChip tone={tone}>{status}</StatusChip> : undefined,
      };
    };
    if (text.trim().length === 0) {
      const items = (recent.data ?? []).map((r) => toItem(r.item, false));
      return items.length > 0 ? [{ id: 'recent', heading: t('recent'), items }] : [];
    }
    if (!searching) return [];
    return (results.data?.groups ?? []).map((g) => ({
      id: g.systemCode,
      heading: pickText(language, g.nameAr, g.nameEn),
      items: g.items.map((i) => toItem(i, true)),
    }));
  }, [language, recent.data, results.data, searching, t, text]);

  const trimmed = text.trim();
  const emptyText =
    trimmed.length === 0
      ? recent.isSuccess
        ? t('noRecent')
        : undefined
      : trimmed.length < minQueryLength
        ? t('tooShort')
        : results.isError
          ? t('failed')
          : results.isSuccess && term === trimmed
            ? t('noResults', { query: trimmed })
            : undefined;
  const loading =
    (searching && results.isFetching && !results.data) || (trimmed.length === 0 && recent.isLoading && open);

  const close = () => {
    setText('');
    setExpanded(false);
    inputRef.current?.blur();
  };

  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        className="sm:hidden"
        aria-label={t('open')}
        onClick={() => {
          setExpanded(true);
          window.requestAnimationFrame(() => inputRef.current?.focus());
        }}
        data-testid="global-search-open"
      >
        <Search aria-hidden />
      </Button>
      <div
        className={
          expanded
            ? 'absolute inset-x-0 top-0 z-30 flex h-16 items-center gap-2 bg-background px-2 sm:static sm:z-auto sm:h-auto sm:max-w-md sm:flex-1 sm:bg-transparent sm:px-0'
            : 'hidden max-w-md flex-1 sm:flex'
        }
      >
        <CommandSearch
          value={text}
          onValueChange={setText}
          groups={groups}
          onSelect={(item) => {
            const link = links.current.get(item.id);
            close();
            if (link) void navigate({ to: link as '/' });
          }}
          label={t('label')}
          placeholder={t('placeholder')}
          clearLabel={t('clear')}
          shortcutHint={t('shortcut')}
          loading={loading}
          emptyText={emptyText}
          inputRef={inputRef}
          onOpenChange={(next) => {
            setOpen(next);
            if (!next && !text) setExpanded(false);
          }}
          testId="global-search"
        />
        {expanded && (
          <Button variant="ghost" size="icon" className="sm:hidden" aria-label={t('close')} onClick={close}>
            <X aria-hidden />
          </Button>
        )}
      </div>
    </>
  );
}
