import * as LabelPrimitive from '@radix-ui/react-label';
import { useId, type ReactElement, type ReactNode } from 'react';
import { cloneElement } from 'react';
import { cn } from '@/lib/cn';

export function Label({ className, ...props }: LabelPrimitive.LabelProps) {
  return <LabelPrimitive.Root className={cn('text-sm font-medium', className)} {...props} />;
}

interface FieldControlProps {
  id?: string;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}

interface FormFieldProps {
  label: ReactNode;
  /** Already-translated error message; shown under the control and announced. */
  error?: string | undefined;
  hint?: ReactNode;
  required?: boolean;
  className?: string;
  children: ReactElement<FieldControlProps>;
}

/** Label + control + hint + inline error, wired with ids and ARIA so screen readers read them together. */
export function FormField({ label, error, hint, required, className, children }: FormFieldProps) {
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label htmlFor={id}>
        {label}
        {required && (
          <span className="ms-0.5 text-danger" aria-hidden>
            *
          </span>
        )}
      </Label>
      {cloneElement(children, {
        id,
        'aria-invalid': error ? true : undefined,
        'aria-describedby': describedBy,
      })}
      {hint && (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} role="alert" className="text-xs font-medium text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
