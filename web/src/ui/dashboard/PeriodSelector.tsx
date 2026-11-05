import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatMonth, formatNumber } from '@/lib/format';
import { Button } from '../button';
import { Input } from '../input';
import { Select } from '../select';
import { Switch } from '../toggle';
import {
  changeKind,
  isMonth,
  normalizePeriod,
  periodKinds,
  shiftPeriod,
  type PeriodKind,
  type PeriodValue,
} from './period';

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** "March 2026", "Q1 2026", "2026" or "Jan 2026 – Mar 2026" in the user's language. */
export function periodLabel(period: PeriodValue, language: string, t: Translate): string {
  const [year = '', month = '01'] = period.from.split('-');
  const y = formatNumber(Number(year), language, undefined).replace(/[,٬\s]/g, '');
  switch (period.kind) {
    case 'month':
      return formatMonth(period.from, language);
    case 'quarter':
      return t('period.quarterLabel', {
        quarter: formatNumber(Math.floor((Number(month) - 1) / 3) + 1, language),
        year: y,
      });
    case 'year':
      return y;
    case 'custom':
      return period.from === period.to
        ? formatMonth(period.from, language)
        : t('period.range', {
            from: formatMonth(period.from, language),
            to: formatMonth(period.to, language),
          });
  }
}

export interface PeriodSelectorProps {
  value: PeriodValue;
  onChange: (value: PeriodValue) => void;
  /** Kinds offered (default: all four). */
  kinds?: readonly PeriodKind[];
  /** Shows the "compare with previous period" switch (default true). */
  allowCompare?: boolean;
  className?: string;
  testId?: string;
}

/** Month / quarter / year / custom whole-month period with step buttons and "compare with previous period" (URL-synced by the page). */
export function PeriodSelector({
  value,
  onChange,
  kinds = periodKinds,
  allowCompare = true,
  className,
  testId,
}: PeriodSelectorProps) {
  const { t: tt, i18n } = useTranslation('charts');
  const t = tt as unknown as Translate;
  const id = useId();
  const language = i18n.language;
  return (
    <div
      role="group"
      aria-label={t('period.label')}
      className={cn('flex flex-wrap items-center gap-2', className)}
      data-testid={testId}
    >
      <Select
        aria-label={t('period.kind')}
        value={value.kind}
        onValueChange={(kind) => onChange(changeKind(value, kind as PeriodKind))}
        options={kinds.map((k) => ({ value: k, label: t(`period.kinds.${k}`) }))}
        className="w-36"
      />
      {value.kind === 'custom' ? (
        <>
          <label className="sr-only" htmlFor={`${id}-from`}>
            {t('period.from')}
          </label>
          <Input
            id={`${id}-from`}
            type="month"
            value={value.from}
            onChange={(e) =>
              isMonth(e.target.value) && onChange(normalizePeriod({ ...value, from: e.target.value }))
            }
            className="w-40"
          />
          <span aria-hidden>–</span>
          <label className="sr-only" htmlFor={`${id}-to`}>
            {t('period.to')}
          </label>
          <Input
            id={`${id}-to`}
            type="month"
            value={value.to}
            onChange={(e) =>
              isMonth(e.target.value) && onChange(normalizePeriod({ ...value, to: e.target.value }))
            }
            className="w-40"
          />
        </>
      ) : (
        <span className="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon"
            className="size-8"
            aria-label={t('period.previous')}
            onClick={() => onChange(shiftPeriod(value, -1))}
          >
            <ChevronLeft className="rtl:rotate-180" aria-hidden />
          </Button>
          <output className="min-w-28 text-center text-sm font-medium" aria-live="polite">
            {periodLabel(value, language, t)}
          </output>
          <Button
            variant="ghost"
            size="icon"
            className="size-8"
            aria-label={t('period.next')}
            onClick={() => onChange(shiftPeriod(value, 1))}
          >
            <ChevronRight className="rtl:rotate-180" aria-hidden />
          </Button>
        </span>
      )}
      {allowCompare && (
        <Switch
          checked={value.compare}
          onCheckedChange={(compare) => onChange({ ...value, compare })}
          label={t('period.compare')}
          className="items-center gap-2"
        />
      )}
    </div>
  );
}
