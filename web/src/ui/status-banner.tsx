import { CircleAlert, CircleCheck, Info, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '@/lib/cn';

type BannerTone = 'info' | 'success' | 'warning' | 'danger';

const tones: Record<BannerTone, { className: string; icon: LucideIcon }> = {
  info: { className: 'border-info/30 bg-info-soft', icon: Info },
  success: { className: 'border-success/30 bg-success-soft', icon: CircleCheck },
  warning: { className: 'border-warning/30 bg-warning-soft', icon: TriangleAlert },
  danger: { className: 'border-danger/30 bg-danger-soft', icon: CircleAlert },
};

const iconTones: Record<BannerTone, string> = {
  info: 'text-info',
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
};

interface StatusBannerProps {
  tone?: BannerTone;
  /** What is happening now, e.g. "Waiting for the charter to be approved". */
  title: ReactNode;
  /** Who acts next and how, from the current-status engine. */
  description?: ReactNode;
  /** Only the actions valid for this user right now (guided actions, SPEC §0.3). */
  actions?: ReactNode;
  className?: string;
}

/** The "current status" banner of a record page. */
export function StatusBanner({ tone = 'info', title, description, actions, className }: StatusBannerProps) {
  const { className: toneClass, icon: Icon } = tones[tone];
  return (
    <section
      // Errors interrupt (alert); everything else is announced politely (status).
      role={tone === 'danger' ? 'alert' : 'status'}
      className={cn(
        'flex flex-col gap-3 rounded-lg border p-4 sm:flex-row sm:items-center sm:justify-between',
        toneClass,
        className,
      )}
    >
      <div className="flex items-start gap-3">
        <Icon className={cn('mt-0.5 size-5 shrink-0', iconTones[tone])} aria-hidden />
        <div className="flex flex-col gap-0.5">
          <p className="text-sm font-semibold">{title}</p>
          {description && <p className="text-sm text-muted-foreground">{description}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap gap-2 sm:shrink-0">{actions}</div>}
    </section>
  );
}
