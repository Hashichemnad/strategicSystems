import * as PopoverPrimitive from '@radix-ui/react-popover';
import { arSA, enGB } from 'date-fns/locale';
import { CalendarDays } from 'lucide-react';
import { useState } from 'react';
import { DayPicker } from 'react-day-picker';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { formatDate } from '@/lib/format';

interface DatePickerProps {
  /** Calendar date as `YYYY-MM-DD` (maps to `DateOnly` on the server), or undefined. */
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  id?: string;
  placeholder?: string;
  disabled?: boolean;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}

function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function fromIsoDate(value: string): Date {
  const [y = 0, m = 1, d = 1] = value.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/** Date-only picker; the calendar mirrors in Arabic and uses the user's locale for month and day names. */
export function DatePicker({ value, onChange, id, placeholder, disabled, ...aria }: DatePickerProps) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const selected = value ? fromIsoDate(value) : undefined;
  const rtl = i18n.dir() === 'rtl';

  return (
    <PopoverPrimitive.Root open={open} onOpenChange={setOpen}>
      <PopoverPrimitive.Trigger
        id={id}
        disabled={disabled}
        {...aria}
        aria-label={selected ? undefined : t('datePicker.open')}
        className={cn(
          'flex h-10 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm aria-invalid:border-danger disabled:opacity-50',
          !selected && 'text-muted-foreground',
        )}
      >
        {selected ? formatDate(selected, i18n.language) : (placeholder ?? t('datePicker.placeholder'))}
        <CalendarDays className="size-4 opacity-60" aria-hidden />
      </PopoverPrimitive.Trigger>
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={4}
          className="z-50 rounded-lg border bg-card p-3 text-card-foreground shadow-lg"
        >
          <DayPicker
            mode="single"
            selected={selected}
            defaultMonth={selected}
            onSelect={(date) => {
              onChange(date ? toIsoDate(date) : undefined);
              setOpen(false);
            }}
            locale={i18n.language === 'ar' ? arSA : enGB}
            dir={rtl ? 'rtl' : 'ltr'}
            numerals="latn"
            classNames={{
              root: 'text-sm',
              months: 'flex flex-col',
              month_caption: 'mb-2 flex h-8 items-center justify-center font-semibold',
              nav: 'absolute inset-x-3 top-3 flex justify-between',
              button_previous: 'rounded p-1.5 hover:bg-muted',
              button_next: 'rounded p-1.5 hover:bg-muted',
              chevron: 'size-4 fill-current',
              weekday: 'size-9 text-xs font-medium text-muted-foreground',
              day: 'p-0',
              day_button: 'size-9 rounded-md hover:bg-muted',
              selected: '[&>button]:bg-primary [&>button]:text-primary-foreground',
              today: 'font-bold text-primary',
              outside: 'text-muted-foreground/60',
            }}
          />
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}
