import { Columns2, Table2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppearance } from '@/app/theme';
import { cn } from '@/lib/cn';
import { readPreference, writePreference } from '@/lib/preferences';

/** Table or split view of a list page (Enterprise: table with a split-view toggle; Modern: split view by default). */
export function useListView(key: string): {
  view: 'table' | 'split';
  setView: (view: 'table' | 'split') => void;
  available: boolean;
} {
  const { uiTheme } = useAppearance();
  const [chosen, setChosen] = useState(() => readPreference(`listView.${key}`));
  const fallback = uiTheme === 'modern' ? 'split' : 'table';
  const view = uiTheme === 'classic' ? 'table' : chosen === 'split' || chosen === 'table' ? chosen : fallback;
  return {
    view,
    available: uiTheme !== 'classic',
    setView: (next) => {
      writePreference(`listView.${key}`, next);
      setChosen(next);
    },
  };
}

/** Table / split toggle (a group of pressed buttons, like the other view switches). Hidden in Classic. */
export function ListViewToggle({
  value,
  onChange,
}: {
  value: 'table' | 'split';
  onChange: (v: 'table' | 'split') => void;
}) {
  const { t } = useTranslation('appearance');
  return (
    <div role="group" aria-label={t('split.view')} className="inline-flex rounded-md border bg-card p-0.5">
      {(
        [
          ['table', Table2, t('split.table')],
          ['split', Columns2, t('split.split')],
        ] as const
      ).map(([key, Icon, label]) => (
        <button
          key={key}
          type="button"
          aria-pressed={value === key}
          onClick={() => onChange(key)}
          className={cn(
            'inline-flex h-[var(--control-h-sm)] items-center gap-1.5 rounded px-2 text-sm',
            value === key ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:bg-muted',
          )}
          data-testid={`list-view-${key}`}
        >
          <Icon className="size-4" aria-hidden />
          <span className="hidden sm:inline">{label}</span>
        </button>
      ))}
    </div>
  );
}

/**
 * List + preview (SPEC-ADDENDUM §C3–C4): the list at the start, the selected record's preview at the end on wide screens; below `lg`
 * the list only (rows open the record as usual).
 */
export function SplitView({
  list,
  preview,
  hasSelection,
}: {
  list: ReactNode;
  preview: ReactNode;
  hasSelection: boolean;
}) {
  const { t } = useTranslation('appearance');
  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(20rem,26rem)]" data-testid="split-view">
      <div className="min-w-0">{list}</div>
      <aside
        aria-label={t('split.preview')}
        className="motion-panel hidden self-start rounded-lg border bg-card p-4 lg:sticky lg:top-20 lg:block"
        data-testid="split-preview"
      >
        {hasSelection ? (
          preview
        ) : (
          <p className="py-10 text-center text-sm text-muted-foreground">{t('split.selectOne')}</p>
        )}
      </aside>
    </div>
  );
}
