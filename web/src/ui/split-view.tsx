import { Columns2, Table2 } from 'lucide-react';
import { Button } from './button';
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

/** A row of the split view's list: the title opens the record; *Preview* shows it beside the list on wide screens. */
export function SplitCard({
  title,
  chips,
  progress,
  selected,
  onPreview,
}: {
  title: ReactNode;
  chips: ReactNode;
  progress?: { value: number; label: string; tone?: 'danger' | 'success' };
  selected: boolean;
  onPreview: () => void;
}) {
  const { t } = useTranslation('appearance');
  return (
    <li
      className={cn(
        'flex flex-col gap-2 rounded-lg border bg-card p-4',
        selected && 'border-app-accent ring-1 ring-app-accent',
      )}
      data-testid="split-card"
    >
      <div className="flex items-start justify-between gap-3">
        {title}
        <Button
          variant="ghost"
          size="sm"
          className="hidden shrink-0 lg:inline-flex"
          aria-pressed={selected}
          onClick={onPreview}
          data-testid="split-card-preview"
        >
          {t('split.preview')}
        </Button>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">{chips}</div>
      {progress && (
        <div className="flex items-center gap-2">
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted" aria-hidden>
            <div
              className={cn(
                'h-full rounded-full',
                progress.tone === 'danger'
                  ? 'bg-danger'
                  : progress.tone === 'success'
                    ? 'bg-success'
                    : 'bg-primary',
              )}
              style={{ inlineSize: `${Math.min(100, Math.max(0, progress.value * 100))}%` }}
            />
          </div>
          <span className="text-xs tabular-nums">{progress.label}</span>
        </div>
      )}
    </li>
  );
}

/** The split view's preview: title, the row's fields and *Open the record*. */
export function SplitPreview({
  title,
  rows,
  open,
}: {
  title: ReactNode;
  rows: [string, ReactNode][];
  open: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4" data-testid="split-preview-panel">
      <h2 className="text-lg font-semibold" dir="auto">
        {title}
      </h2>
      <dl className="grid grid-cols-1 gap-3">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-sm font-medium">{value}</dd>
          </div>
        ))}
      </dl>
      <Button asChild>{open}</Button>
    </div>
  );
}
