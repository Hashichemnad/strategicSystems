import { useMemo } from 'react';
import { cn } from '@/lib/cn';
import { isHtml, sanitizeHtml } from './rich-text';

interface RichTextViewProps {
  value: string | null | undefined;
  /** Shown when the value is empty (default "—"). */
  fallback?: string;
  className?: string;
  /** Compact rendering for table cells (smaller spacing). */
  compact?: boolean;
}

/**
 * Read view of a rich-text field (ADR 0161). HTML is sanitised with DOMPurify (the server's allow-list) before it is rendered;
 * a plain (legacy) value is rendered as React text — escaped — with its paragraphs and line breaks.
 */
export function RichTextView({ value, fallback = '—', className, compact }: RichTextViewProps) {
  const html = useMemo(() => (value && isHtml(value) ? sanitizeHtml(value) : null), [value]);
  const classes = cn('rich-text', compact && 'rich-text-compact', className);
  if (!value || !value.trim())
    return <span className={cn('text-muted-foreground', className)}>{fallback}</span>;
  if (html === null) {
    return (
      <div className={classes} dir="auto">
        {value
          .replace(/\r\n/g, '\n')
          .trim()
          .split(/\n\s*\n/)
          .map((paragraph, index) => (
            <p key={index} className="whitespace-pre-line">
              {paragraph}
            </p>
          ))}
      </div>
    );
  }
  // The only HTML the app renders: sanitised by DOMPurify with the rich-text allow-list just above.
  return <div className={classes} dir="auto" dangerouslySetInnerHTML={{ __html: html }} />;
}
