import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight } from 'lucide-react';
import type { HTMLAttributes, ReactNode, TdHTMLAttributes, ThHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { Button } from './button';

export function Table({ className, ...props }: HTMLAttributes<HTMLTableElement>) {
  return (
    <div className="w-full overflow-x-auto rounded-lg border bg-card">
      <table className={cn('w-full border-collapse text-sm', className)} {...props} />
    </div>
  );
}

export function TableHead(props: HTMLAttributes<HTMLTableSectionElement>) {
  return <thead className="bg-muted/60 text-muted-foreground" {...props} />;
}

export function TableBody(props: HTMLAttributes<HTMLTableSectionElement>) {
  return <tbody className="divide-y" {...props} />;
}

export function TableRow({ className, ...props }: HTMLAttributes<HTMLTableRowElement>) {
  return <tr className={cn('hover:bg-muted/40', className)} {...props} />;
}

export function TableHeaderCell({ className, ...props }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      scope="col"
      className={cn('h-11 px-4 text-start align-middle font-medium whitespace-nowrap', className)}
      {...props}
    />
  );
}

export function TableCell({ className, ...props }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn('px-4 py-3 align-middle', className)} {...props} />;
}

export type SortDirection = 'asc' | 'desc' | null;

interface SortableHeaderProps {
  children: ReactNode;
  direction: SortDirection;
  onSort: () => void;
  className?: string;
}

/** Column header that toggles sorting; announces the current order through aria-sort. */
export function SortableHeader({ children, direction, onSort, className }: SortableHeaderProps) {
  const Icon = direction === 'asc' ? ArrowUp : direction === 'desc' ? ArrowDown : ArrowUpDown;
  return (
    <TableHeaderCell
      className={className}
      aria-sort={direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none'}
    >
      <button
        type="button"
        onClick={onSort}
        className="-ms-2 inline-flex items-center gap-1 rounded px-2 py-1 hover:text-foreground"
      >
        {children}
        <Icon className="size-3.5" aria-hidden />
      </button>
    </TableHeaderCell>
  );
}

interface PaginationProps {
  page: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}

/** Previous / next paging with the result count. Chevrons mirror in RTL. */
export function Pagination({ page, totalPages, totalCount, onPageChange }: PaginationProps) {
  const { t, i18n } = useTranslation();
  const pages = Math.max(1, totalPages);
  return (
    <nav
      className="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground"
      aria-label={t('table.page', { page, pages })}
    >
      <span>{t('table.results', { count: totalCount })}</span>
      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="icon"
          className="size-8"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          aria-label={t('table.previous')}
        >
          <ChevronLeft className="rtl:rotate-180" aria-hidden />
        </Button>
        <span aria-live="polite">
          {t('table.page', {
            page: formatNumber(page, i18n.language),
            pages: formatNumber(pages, i18n.language),
          })}
        </span>
        <Button
          variant="outline"
          size="icon"
          className="size-8"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= pages}
          aria-label={t('table.nextPage')}
        >
          <ChevronRight className="rtl:rotate-180" aria-hidden />
        </Button>
      </div>
    </nav>
  );
}
