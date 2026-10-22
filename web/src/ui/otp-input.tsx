import { useRef, type ClipboardEvent, type KeyboardEvent } from 'react';
import { cn } from '@/lib/cn';

interface OtpInputProps {
  value: string;
  onChange: (value: string) => void;
  length?: number;
  label: string;
  autoFocus?: boolean;
  disabled?: boolean;
  invalid?: boolean;
  id?: string;
}

/**
 * One-time code entry: one box per digit, always left-to-right (codes are read that way in Arabic too),
 * paste fills all boxes, Backspace moves back. Works with SMS / password-manager autofill (one-time-code).
 */
export function OtpInput({
  value,
  onChange,
  length = 6,
  label,
  autoFocus,
  disabled,
  invalid,
  id,
}: OtpInputProps) {
  const inputs = useRef<(HTMLInputElement | null)[]>([]);
  const digits = Array.from({ length }, (_, i) => value[i] ?? '');

  function setAt(index: number, digit: string) {
    const next = digits.slice();
    next[index] = digit;
    onChange(next.join('').slice(0, length));
  }

  function onKeyDown(index: number, event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Backspace' && !digits[index] && index > 0) {
      inputs.current[index - 1]?.focus();
      setAt(index - 1, '');
      event.preventDefault();
    } else if (event.key === 'ArrowLeft' && index > 0) {
      inputs.current[index - 1]?.focus();
    } else if (event.key === 'ArrowRight' && index < length - 1) {
      inputs.current[index + 1]?.focus();
    }
  }

  function onPaste(event: ClipboardEvent<HTMLInputElement>) {
    const pasted = event.clipboardData.getData('text').replace(/\D/gu, '').slice(0, length);
    if (pasted) {
      event.preventDefault();
      onChange(pasted);
      inputs.current[Math.min(pasted.length, length - 1)]?.focus();
    }
  }

  return (
    <div role="group" aria-label={label} dir="ltr" className="flex gap-2" id={id}>
      {digits.map((digit, index) => (
        <input
          key={index}
          ref={(element) => {
            inputs.current[index] = element;
          }}
          value={digit}
          inputMode="numeric"
          autoComplete={index === 0 ? 'one-time-code' : 'off'}
          maxLength={length}
          aria-label={`${label} ${index + 1}`}
          aria-invalid={invalid || undefined}
          // eslint-disable-next-line jsx-a11y/no-autofocus -- the code is the only thing on this step
          autoFocus={autoFocus && index === 0}
          disabled={disabled}
          className={cn(
            'size-11 rounded-md border border-input bg-card text-center text-lg font-semibold tabular-nums aria-invalid:border-danger',
          )}
          onPaste={onPaste}
          onKeyDown={(event) => onKeyDown(index, event)}
          onChange={(event) => {
            const typed = event.target.value.replace(/\D/gu, '');
            if (typed.length > 1) {
              onChange((digits.slice(0, index).join('') + typed).slice(0, length));
              inputs.current[Math.min(index + typed.length, length - 1)]?.focus();
              return;
            }

            setAt(index, typed);
            if (typed && index < length - 1) inputs.current[index + 1]?.focus();
          }}
        />
      ))}
    </div>
  );
}
