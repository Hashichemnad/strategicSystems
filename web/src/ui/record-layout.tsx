import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppearance } from '@/app/theme';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';

export interface Highlight {
  key: string;
  label: string;
  value: ReactNode;
}

export interface RelatedList {
  key: string;
  label: string;
  count?: number;
  onOpen: () => void;
}

interface RecordLayoutProps {
  /** Link back to the list. */
  back?: ReactNode;
  /** The record's PageHeader (title, number, chips, primary actions). */
  header: ReactNode;
  /** Key fields: the Enterprise highlights panel and the Modern key-facts card. */
  highlights?: readonly Highlight[];
  /** The record's StageStepper (shown as a path bar in Enterprise). */
  path?: ReactNode;
  /** Current-status banner. */
  status?: ReactNode;
  /** Progress strip or other summary above the tabs. */
  summary?: ReactNode;
  /** Related lists (Enterprise / Modern side column): count cards that open the related tab. */
  related?: readonly RelatedList[];
  /** The tabs (details and related records). */
  children: ReactNode;
  testId?: string;
}

/**
 * Record page layout slots (SPEC-ADDENDUM §C3–C4, docs/design/themes.md §4): one record page, three arrangements.
 * Classic: stack (header · stepper · status · summary · tabs) — unchanged. Enterprise: highlights panel (header + key fields), path bar,
 * status, then two columns — tabs at the start, related lists at the end. Modern: header card with key facts chips, path card, status,
 * then tabs beside a key-facts / related card on wide screens.
 */
export function RecordLayout({
  back,
  header,
  highlights = [],
  path,
  status,
  summary,
  related = [],
  children,
  testId,
}: RecordLayoutProps) {
  const { uiTheme } = useAppearance();
  const { t, i18n } = useTranslation('appearance');

  if (uiTheme === 'classic') {
    return (
      <div className="flex flex-col gap-6" data-testid={testId}>
        {back}
        {header}
        {path}
        {status}
        {summary}
        {children}
      </div>
    );
  }

  const facts = highlights.length > 0 && (
    <dl
      className={cn(
        'grid gap-x-6 gap-y-3',
        uiTheme === 'enterprise'
          ? 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-6'
          : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-1',
      )}
    >
      {highlights.map((item) => (
        <div key={item.key} className="min-w-0">
          <dt className="text-xs text-muted-foreground">{item.label}</dt>
          <dd className="truncate text-sm font-medium">{item.value}</dd>
        </div>
      ))}
    </dl>
  );

  const relatedLists = related.length > 0 && (
    <section aria-labelledby="record-related" className="flex flex-col gap-2 rounded-lg border bg-card p-4">
      <h2 id="record-related" className="text-sm font-semibold">
        {t('record.related')}
      </h2>
      <ul className="flex flex-col gap-1">
        {related.map((item) => (
          <li key={item.key}>
            <button
              type="button"
              onClick={item.onOpen}
              className="flex w-full items-center justify-between gap-2 rounded-md border px-3 py-2 text-start text-sm hover:border-app-accent hover:bg-muted/40"
              data-testid={`related-${item.key}`}
            >
              <span className="font-medium">{item.label}</span>
              {item.count !== undefined && (
                <span className="rounded-full bg-muted px-2 text-xs tabular-nums text-muted-foreground">
                  {formatNumber(item.count, i18n.language)}
                  <span className="sr-only"> — {t('record.items', { count: item.count })}</span>
                </span>
              )}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );

  if (uiTheme === 'enterprise') {
    return (
      <div className="flex flex-col gap-3" data-testid={testId} data-record-layout="enterprise">
        {back}
        <section
          aria-label={t('record.highlights')}
          className="flex flex-col gap-4 rounded-lg border border-t-[3px] border-t-app-accent bg-card p-4"
          data-testid="record-highlights"
        >
          {header}
          {facts && <div className="border-t pt-3">{facts}</div>}
        </section>
        {path && <div className="rounded-lg border bg-card p-3">{path}</div>}
        {status}
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <div className="flex min-w-0 flex-col gap-3">
            {summary}
            <div className="rounded-lg border bg-card p-3">{children}</div>
          </div>
          {relatedLists && <aside className="flex flex-col gap-3">{relatedLists}</aside>}
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5" data-testid={testId} data-record-layout="modern">
      {back}
      <div className="rounded-xl border bg-card p-6">{header}</div>
      {path && <div className="rounded-xl border bg-card px-6 py-4">{path}</div>}
      {status}
      {summary}
      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_17rem]">
        <div className="min-w-0">{children}</div>
        {(facts || relatedLists) && (
          <aside className="flex flex-col gap-4">
            {facts && (
              <section
                aria-labelledby="record-facts"
                className="flex flex-col gap-3 rounded-xl border bg-card p-4"
              >
                <h2 id="record-facts" className="text-sm font-semibold">
                  {t('record.keyFacts')}
                </h2>
                {facts}
              </section>
            )}
            {relatedLists}
          </aside>
        )}
      </div>
    </div>
  );
}
