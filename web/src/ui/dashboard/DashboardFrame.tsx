import type { ReactNode } from 'react';
import { cn } from '@/lib/cn';
import { PageHeader } from '../page-header';
import { AsOf, ExportMenu, type ExportMenuProps } from './parts';
import type { PeriodValue } from './period';
import { PeriodSelector, type PeriodSelectorProps } from './PeriodSelector';

export interface DashboardFrameProps {
  title: ReactNode;
  description?: ReactNode;
  /** Header actions (buttons) beside the export menu. */
  actions?: ReactNode;
  /** Filter controls (unit, category…) shown in a row under the header. */
  filters?: ReactNode;
  /** The period; with `onPeriodChange` a PeriodSelector is shown in the filter row. */
  period?: PeriodValue;
  onPeriodChange?: (period: PeriodValue) => void;
  periodOptions?: Pick<PeriodSelectorProps, 'kinds' | 'allowCompare'>;
  /** When the figures were computed. */
  asOf?: string | Date | null;
  /** Export menu callbacks (PDF / Excel / PNG). */
  exports?: Omit<ExportMenuProps, 'testId'>;
  children: ReactNode;
  testId?: string;
  className?: string;
}

/** Dashboard page frame (ADR 0148): header with actions and export, filters with the period selector, "as of", then the widgets. */
export function DashboardFrame({
  title,
  description,
  actions,
  filters,
  period,
  onPeriodChange,
  periodOptions,
  asOf,
  exports,
  children,
  testId,
  className,
}: DashboardFrameProps) {
  const hasExports = Boolean(exports && (exports.onPdf || exports.onExcel || exports.onPng));
  const showPeriod = period !== undefined && onPeriodChange !== undefined;
  return (
    <div className={cn('flex flex-col gap-6', className)} data-testid={testId}>
      <PageHeader
        title={title}
        description={description}
        actions={
          actions || hasExports ? (
            <>
              {actions}
              {hasExports && exports && (
                <ExportMenu {...exports} testId={testId ? `${testId}-export` : 'dashboard-export'} />
              )}
            </>
          ) : undefined
        }
      />
      {(filters || showPeriod || asOf) && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-3">
            {showPeriod && <PeriodSelector value={period} onChange={onPeriodChange} {...periodOptions} />}
            {filters}
          </div>
          <AsOf at={asOf} />
        </div>
      )}
      {children}
    </div>
  );
}
