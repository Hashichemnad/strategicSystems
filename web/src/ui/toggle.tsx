import type { InputHTMLAttributes, ReactNode, Ref } from 'react';
import { useId } from 'react';
import { cn } from '@/lib/cn';

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: ReactNode;
  description?: ReactNode;
  ref?: Ref<HTMLInputElement>;
}

/** Native checkbox (keyboard, forms and screen readers for free) with a label and optional description. */
export function Checkbox({ label, description, className, id, ref, ...props }: CheckboxProps) {
  const generated = useId();
  const inputId = id ?? generated;
  return (
    <div className={cn('flex items-start gap-2', className)}>
      <input
        ref={ref}
        id={inputId}
        type="checkbox"
        className="mt-0.5 size-4 shrink-0 accent-primary disabled:opacity-50"
        aria-describedby={description ? `${inputId}-description` : undefined}
        {...props}
      />
      <span className="flex flex-col gap-0.5">
        <label htmlFor={inputId} className="text-sm font-medium">
          {label}
        </label>
        {description && (
          <span id={`${inputId}-description`} className="text-xs text-muted-foreground">
            {description}
          </span>
        )}
      </span>
    </div>
  );
}

interface SwitchProps {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
  id?: string;
  className?: string;
}

/** On / off control (role="switch"). The thumb moves towards the end side, so it mirrors in RTL. */
export function Switch({
  checked,
  onCheckedChange,
  label,
  description,
  disabled,
  id,
  className,
}: SwitchProps) {
  const generated = useId();
  const switchId = id ?? generated;
  return (
    <div className={cn('flex items-start justify-between gap-4', className)}>
      <span className="flex flex-col gap-0.5">
        <label htmlFor={switchId} className="text-sm font-medium">
          {label}
        </label>
        {description && (
          <span id={`${switchId}-description`} className="text-xs text-muted-foreground">
            {description}
          </span>
        )}
      </span>
      <button
        id={switchId}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-describedby={description ? `${switchId}-description` : undefined}
        disabled={disabled}
        onClick={() => onCheckedChange(!checked)}
        className={cn(
          'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full border-2 border-transparent transition-colors disabled:opacity-50',
          checked ? 'bg-primary' : 'bg-muted-foreground/30',
        )}
      >
        <span
          aria-hidden
          className={cn(
            'size-5 rounded-full bg-card shadow transition-transform',
            checked ? 'ltr:translate-x-5 rtl:-translate-x-5' : 'translate-x-0',
          )}
        />
      </button>
    </div>
  );
}
