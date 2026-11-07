import { lazy, Suspense } from 'react';
import { Skeleton } from './feedback';
import type { RichTextEditorProps } from './rich-text-editor';

const Impl = lazy(() => import('./rich-text-editor'));

/**
 * Rich-text editor (ADR 0161), loaded on first use so the Tiptap chunk stays out of the main bundle. Works inside `FormField`
 * (receives its id and ARIA wiring) and with react-hook-form through `Controller` (`value` / `onChange`).
 */
export function RichTextEditor(props: RichTextEditorProps) {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" data-testid="rich-text-loading" />}>
      <Impl {...props} />
    </Suspense>
  );
}

export type { RichTextEditorProps };
