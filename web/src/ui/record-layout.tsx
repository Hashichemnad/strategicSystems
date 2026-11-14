import { useId, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppearance } from '@/app/theme';
import { cn } from '@/lib/cn';
import {
  RecordSectionsDrawer,
  RecordSideMenu,
  useNarrowViewport,
  type RecordMenuItem,
} from './record-side-menu';

export interface Highlight {
  key: string;
  label: string;
  value: ReactNode;
}

/** A section of a record page (ADR 0221): one side-menu item and the content shown while it is active. */
export interface RecordSection extends RecordMenuItem {
  content: ReactNode;
}

export interface RecordLayoutProps {
  /** Link back to the list (and a module's secondary navigation). */
  back?: ReactNode;
  /** The record's PageHeader (title, number, chips, primary actions). */
  header: ReactNode;
  /** Key fields: the Enterprise highlights panel and the Modern key-facts card. */
  highlights?: readonly Highlight[];
  /** The record's StageStepper (shown as a path bar in Enterprise). */
  path?: ReactNode;
  /** Current-status banner. */
  status?: ReactNode;
  /** Progress strip or other summary above the sections. */
  summary?: ReactNode;
  /** Every section of the record in menu order (details first, then related records, documents, history). */
  sections: readonly RecordSection[];
  /** Key of the section shown (the page's `?tab=`); an unknown or hidden key shows the first visible section. */
  activeSection: string;
  /** Called with the chosen section's key; pages navigate to `?tab=<key>` (or set local state). */
  onSectionChange: (key: string) => void;
  /** Rendered after the layout, outside the content area (dialogs opened from the header). */
  children?: ReactNode;
  testId?: string;
}

/**
 * Record page layout slots (SPEC-ADDENDUM §C3–C4, docs/design/themes.md §4, ADR 0221): one record page, three arrangements, and in
 * every theme a single side menu of the record's sections (no header tab strip). The menu sits at the inline start on `md` and wider;
 * below `md` it is a drawer opened by the "Sections" button in the record header. Only the active section's content is rendered, in
 * a region named after it.
 * Classic: stack (header · stepper · status · summary) then menu + content. Enterprise: highlights panel (header + key fields), path
 * bar, status, then menu + content card. Modern: header card, path card, status, summary, then menu and key-facts card + content.
 */
export function RecordLayout({
  back,
  header,
  highlights = [],
  path,
  status,
  summary,
  sections,
  activeSection,
  onSectionChange,
  children,
  testId,
}: RecordLayoutProps) {
  const { uiTheme } = useAppearance();
  const { t } = useTranslation('appearance');
  const narrow = useNarrowViewport();
  const contentId = useId();
  const visible = sections.filter((section) => !section.hidden);
  const active = visible.find((section) => section.key === activeSection) ?? visible[0];
  const items: RecordMenuItem[] = visible.map(({ key, label, count, testId: itemTestId }) => ({
    key,
    label,
    count,
    testId: itemTestId,
  }));

  const sectionsButton = narrow && active && (
    <RecordSectionsDrawer items={items} active={active.key} onSelect={onSectionChange} controls={contentId} />
  );
  const menu = !narrow && active && (
    <RecordSideMenu items={items} active={active.key} onSelect={onSectionChange} controls={contentId} />
  );
  const content = (
    <div
      id={contentId}
      role="region"
      aria-label={active?.label}
      className={cn('min-w-0', uiTheme === 'enterprise' && 'rounded-lg border bg-card p-3')}
      data-testid="record-section-content"
      data-section={active?.key}
    >
      {active?.content}
    </div>
  );
  const grid = 'grid md:grid-cols-[12rem_minmax(0,1fr)] md:items-start lg:grid-cols-[14rem_minmax(0,1fr)]';

  if (uiTheme === 'classic') {
    return (
      <div className="flex flex-col gap-6" data-testid={testId} data-record-layout="classic">
        {back}
        {header}
        {sectionsButton}
        {path}
        {status}
        {summary}
        <div className={cn(grid, 'gap-6')}>
          {menu}
          {content}
        </div>
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
          : 'grid-cols-1 sm:grid-cols-2 md:grid-cols-1',
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
          {sectionsButton}
        </section>
        {path && <div className="rounded-lg border bg-card p-3">{path}</div>}
        {status}
        <div className={cn(grid, 'gap-3')}>
          {menu}
          <div className="flex min-w-0 flex-col gap-3">
            {summary}
            {content}
          </div>
        </div>
        {children}
      </div>
    );
  }

  const factsCard = facts && (
    <section aria-labelledby="record-facts" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 id="record-facts" className="text-sm font-semibold">
        {t('record.keyFacts')}
      </h2>
      {facts}
    </section>
  );

  return (
    <div className="flex flex-col gap-5" data-testid={testId} data-record-layout="modern">
      {back}
      <div className="flex flex-col gap-4 rounded-xl border bg-card p-6">
        {header}
        {sectionsButton}
      </div>
      {path && <div className="rounded-xl border bg-card px-6 py-4">{path}</div>}
      {status}
      {summary}
      <div className={cn(grid, 'gap-5')}>
        {narrow ? (
          factsCard
        ) : (
          <div className="flex min-w-0 flex-col gap-4">
            {menu}
            {factsCard}
          </div>
        )}
        {content}
      </div>
      {children}
    </div>
  );
}
