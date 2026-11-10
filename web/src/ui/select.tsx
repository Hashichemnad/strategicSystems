import * as SelectPrimitive from '@radix-ui/react-select';
import { Check, ChevronDown } from 'lucide-react';
import { cn } from '@/lib/cn';

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  value: string | undefined;
  onValueChange: (value: string) => void;
  options: readonly SelectOption[];
  placeholder?: string;
  id?: string;
  disabled?: boolean;
  className?: string;
  'aria-label'?: string;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}

/** Accessible single select (Radix). Direction comes from the app's DirectionProvider. */
export function Select({
  value,
  onValueChange,
  options,
  placeholder,
  id,
  disabled,
  className,
  ...aria
}: SelectProps) {
  // Outside a FormField (no id for a <label>) and without an explicit name, the placeholder (else the chosen option) names the
  // control, so a select is never an unnamed button (WCAG 4.1.2). Screens should still pass an aria-label naming the field.
  const name =
    aria['aria-label'] ??
    (id ? undefined : (placeholder ?? options.find((option) => option.value === value)?.label));
  return (
    <SelectPrimitive.Root value={value} onValueChange={onValueChange} disabled={disabled}>
      <SelectPrimitive.Trigger
        id={id}
        {...aria}
        aria-label={name}
        className={cn(
          'flex h-[var(--control-h)] w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm data-[placeholder]:text-muted-foreground aria-invalid:border-danger disabled:opacity-50',
          className,
        )}
      >
        <SelectPrimitive.Value placeholder={placeholder} />
        <SelectPrimitive.Icon>
          <ChevronDown className="size-4 opacity-60" aria-hidden />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>
      <SelectPrimitive.Portal>
        <SelectPrimitive.Content
          position="popper"
          sideOffset={4}
          className="z-50 max-h-72 min-w-[var(--radix-select-trigger-width)] overflow-hidden rounded-md border bg-card text-card-foreground shadow-lg"
        >
          <SelectPrimitive.Viewport className="p-1">
            {options.map((option) => (
              <SelectPrimitive.Item
                key={option.value}
                value={option.value}
                className="relative flex cursor-default items-center rounded-sm py-2 ps-8 pe-3 text-sm outline-none select-none data-[highlighted]:bg-muted"
              >
                <SelectPrimitive.ItemIndicator className="absolute start-2 inline-flex">
                  <Check className="size-4" aria-hidden />
                </SelectPrimitive.ItemIndicator>
                <SelectPrimitive.ItemText>{option.label}</SelectPrimitive.ItemText>
              </SelectPrimitive.Item>
            ))}
          </SelectPrimitive.Viewport>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  );
}
