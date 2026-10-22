import { Check, Copy } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { Button } from './button';

interface CopyFieldProps {
  value: string;
  label: string;
  className?: string;
}

/** A value shown once (token, key, recovery codes) with a copy button. Left-to-right in both languages. */
export function CopyField({ value, label, className }: CopyFieldProps) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  return (
    <div className={cn('flex items-center gap-2 rounded-md border bg-muted/50 p-2', className)}>
      <code
        dir="ltr"
        className="min-w-0 flex-1 overflow-x-auto font-mono text-sm break-all whitespace-pre-wrap"
        aria-label={label}
      >
        {value}
      </code>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="size-8"
        aria-label={copied ? t('copy.copied') : t('copy.copy')}
        onClick={() => {
          void navigator.clipboard
            .writeText(value)
            .then(() => setCopied(true))
            .catch(() => setCopied(false));
        }}
      >
        {copied ? <Check aria-hidden /> : <Copy aria-hidden />}
      </Button>
    </div>
  );
}
