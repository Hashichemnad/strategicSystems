import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { pickText } from '@/lib/format';
import { Button, Input } from '@/ui';
import { useEmployees } from './api';

interface EmployeePickerProps {
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  excludeId?: string | undefined;
  id?: string;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}

/**
 * Search-as-you-type employee chooser (combobox pattern): type a name, number or email; Arabic spelling variants match
 * (the server normalises the text). Shows the chosen person with a clear button.
 */
export function EmployeePicker({ value, onChange, excludeId, id, ...aria }: EmployeePickerProps) {
  const { t, i18n } = useTranslation('org');
  const listId = useId();
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [text]);

  const results = useEmployees(
    { search: debounced, pageSize: 8, status: 'active' },
    open && debounced.length > 0,
  );
  const selected = useQuery({
    queryKey: ['org', 'employees', value, 'ref'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/employees/{id}', { params: { path: { id: value ?? '' } }, signal })),
    enabled: !!value,
  });
  const options = (results.data?.items ?? []).filter((e) => e.id !== excludeId);

  if (value && selected.data) {
    return (
      <div
        id={id}
        className="flex h-10 items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm"
      >
        <span className="truncate">
          {pickText(i18n.language, selected.data.nameAr, selected.data.nameEn)}{' '}
          <span className="text-muted-foreground" dir="ltr">
            ({selected.data.employeeNo})
          </span>
        </span>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-7"
          aria-label={t('picker.clear')}
          onClick={() => onChange(undefined)}
        >
          <X aria-hidden />
        </Button>
      </div>
    );
  }

  const choose = (employeeId: string) => {
    onChange(employeeId);
    setOpen(false);
    setText('');
  };

  return (
    <div className="relative">
      <Input
        id={id}
        role="combobox"
        aria-expanded={open && options.length > 0}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && options[active] ? `${listId}-${active}` : undefined}
        placeholder={t('picker.placeholder')}
        value={text}
        autoComplete="off"
        onChange={(event) => {
          setText(event.target.value);
          setOpen(true);
          setActive(0);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') setActive((i) => Math.min(i + 1, options.length - 1));
          else if (event.key === 'ArrowUp') setActive((i) => Math.max(i - 1, 0));
          else if (event.key === 'Enter' && options[active]) choose(options[active].id);
          else if (event.key === 'Escape') setOpen(false);
          else return;
          event.preventDefault();
        }}
        {...aria}
      />
      {open && options.length > 0 && (
        <ul
          id={listId}
          role="listbox"
          className="absolute inset-x-0 top-full z-50 mt-1 max-h-64 overflow-auto rounded-md border bg-card p-1 shadow-md"
        >
          {options.map((employee, index) => (
            <li
              key={employee.id}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === active}
              className="cursor-pointer rounded px-2 py-1.5 text-sm aria-selected:bg-accent"
              onMouseDown={(event) => {
                event.preventDefault();
                choose(employee.id);
              }}
            >
              <span className="block">{pickText(i18n.language, employee.nameAr, employee.nameEn)}</span>
              <span className="block text-xs text-muted-foreground">
                <span dir="ltr">{employee.employeeNo}</span> ·{' '}
                {pickText(i18n.language, employee.unitNameAr, employee.unitNameEn)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
