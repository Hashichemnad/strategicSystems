import { createContext, use, type ReactNode } from 'react';
import { cn } from '@/lib/cn';

interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  /** Record number, status chips… shown beside the title. */
  meta?: ReactNode;
  actions?: ReactNode;
  className?: string;
}

/**
 * True inside a page that embeds another page (e.g. a setup wizard step showing a Settings page): the embedded page's
 * header becomes a sub-heading, so the screen keeps one top-level heading.
 */
const EmbeddedPageContext = createContext(false);

export function EmbeddedPage({ children }: { children: ReactNode }) {
  return <EmbeddedPageContext value={true}>{children}</EmbeddedPageContext>;
}

/** Page or record header: title, meta chips, description and the primary actions (end-aligned). */
export function PageHeader({ title, description, meta, actions, className }: PageHeaderProps) {
  const embedded = use(EmbeddedPageContext);
  return (
    <header className={cn('flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between', className)}>
      <div className="flex min-w-0 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2">
          {embedded ? (
            <h3 className="text-lg font-semibold">{title}</h3>
          ) : (
            <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          )}
          {meta}
        </div>
        {description && <p className="max-w-3xl text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}
