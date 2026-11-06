import { useEffect, useRef, useState, type HTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';

interface ScrollRegionProps extends HTMLAttributes<HTMLDivElement> {
  /** Accessible name of the region while it scrolls (defaults to a generic "Scrollable content"). */
  label?: string;
}

/**
 * A box that may scroll (wide tables, long lists). While its content overflows it becomes a named, focusable region, so
 * keyboard users can scroll it with the arrow keys (WCAG 2.1.1, axe scrollable-region-focusable); otherwise it adds no
 * tab stop.
 */
export function ScrollRegion({ label, className, children, ...props }: ScrollRegionProps) {
  const { t } = useTranslation();
  const ref = useRef<HTMLDivElement>(null);
  const [scrolls, setScrolls] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const measure = () =>
      setScrolls(
        element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1,
      );
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    for (const child of Array.from(element.children)) observer.observe(child);
    return () => observer.disconnect();
  }, [children]);

  return (
    <div
      ref={ref}
      // relative: absolutely positioned descendants (sr-only text) stay inside the scrolled box instead of widening the page.
      className={cn('relative', className, scrolls && 'focus-visible:outline-2 focus-visible:outline-ring')}
      {...(scrolls ? { tabIndex: 0, role: 'region', 'aria-label': label ?? t('a11y.scrollRegion') } : {})}
      {...props}
    >
      {children}
    </div>
  );
}
