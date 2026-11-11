import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useAppearance } from '@/app/theme';
import { cn } from '@/lib/cn';

export interface Stage {
  key: string;
  label: string;
}

interface StageStepperProps {
  stages: readonly Stage[];
  /** Key of the current stage; earlier stages show as completed. */
  current: string;
  className?: string;
}

/**
 * Record lifecycle stepper (CLAUDE.md §10). Reads in the user's direction; the current stage has aria-current. One component, three
 * looks (docs/design/themes.md §4): Classic circles and lines, the Enterprise **path** (chevron segments with the current stage as
 * guidance), the Modern progress track.
 */
export function StageStepper({ stages, current, className }: StageStepperProps) {
  const { t } = useTranslation();
  const { uiTheme } = useAppearance();
  const currentIndex = Math.max(
    0,
    stages.findIndex((stage) => stage.key === current),
  );
  const stateOf = (index: number) =>
    index < currentIndex ? 'completed' : index === currentIndex ? 'current' : 'upcoming';

  if (uiTheme === 'enterprise')
    return <PathBar stages={stages} currentIndex={currentIndex} className={className} />;

  if (uiTheme === 'modern') {
    const ratio = stages.length > 1 ? currentIndex / (stages.length - 1) : 1;
    return (
      <nav aria-label={t('stepper.label')} className={cn('flex w-full flex-col gap-3', className)}>
        <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted" aria-hidden>
          <div
            className="h-full rounded-full bg-app-accent transition-[inline-size] duration-500"
            style={{ inlineSize: `${Math.round(ratio * 100)}%` }}
          />
        </div>
        <ol className="flex flex-wrap items-center gap-x-5 gap-y-2">
          {stages.map((stage, index) => {
            const state = stateOf(index);
            return (
              <li
                key={stage.key}
                className="flex items-center gap-1.5 text-sm"
                aria-current={state === 'current' ? 'step' : undefined}
              >
                <span
                  aria-hidden
                  className={cn(
                    'flex size-4 items-center justify-center rounded-full',
                    state === 'completed' && 'bg-app-accent text-white',
                    state === 'current' && 'ring-4 ring-app-accent/25 bg-app-accent',
                    state === 'upcoming' && 'border-2 border-border',
                  )}
                >
                  {state === 'completed' && <Check className="size-3" />}
                </span>
                <span className={cn(state === 'current' ? 'font-semibold' : 'text-muted-foreground')}>
                  {stage.label}
                  <span className="sr-only"> — {t(`stepper.${state}`)}</span>
                </span>
              </li>
            );
          })}
        </ol>
      </nav>
    );
  }

  return (
    <nav aria-label={t('stepper.label')} className={cn('w-full', className)}>
      {/* Wraps instead of scrolling: every stage stays visible at any width and in both directions. */}
      <ol className="flex flex-wrap items-center gap-x-2 gap-y-3">
        {stages.map((stage, index) => {
          const state = stateOf(index);
          return (
            <li
              key={stage.key}
              className="flex items-center gap-2"
              aria-current={state === 'current' ? 'step' : undefined}
            >
              <span
                className={cn(
                  'flex size-7 items-center justify-center rounded-full border-2 text-xs font-semibold',
                  state === 'completed' && 'border-primary bg-primary text-primary-foreground',
                  state === 'current' && 'border-primary text-primary',
                  state === 'upcoming' && 'border-border text-muted-foreground',
                )}
              >
                {state === 'completed' ? <Check className="size-4" aria-hidden /> : index + 1}
              </span>
              <span
                className={cn('text-sm', state === 'current' ? 'font-semibold' : 'text-muted-foreground')}
              >
                {stage.label}
                <span className="sr-only"> — {t(`stepper.${state}`)}</span>
              </span>
              {index < stages.length - 1 && (
                <span
                  aria-hidden
                  className={cn(
                    'h-0.5 w-8 rounded sm:w-12',
                    index < currentIndex ? 'bg-primary' : 'bg-border',
                  )}
                />
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/**
 * Enterprise path bar: chevron segments pointing in the reading direction (mirrored in RTL), completed segments filled, the current one
 * in the primary colour, with "Current stage: …" guidance underneath. Segments wrap on narrow screens (no horizontal page scroll).
 */
function PathBar({
  stages,
  currentIndex,
  className,
}: {
  stages: readonly Stage[];
  currentIndex: number;
  className?: string;
}) {
  const { t } = useTranslation();
  const { t: ta } = useTranslation('appearance');
  const current = stages[currentIndex];
  return (
    <nav aria-label={t('stepper.label')} className={cn('flex w-full flex-col gap-2', className)}>
      <ol className="flex flex-wrap gap-y-1">
        {stages.map((stage, index) => {
          const state = index < currentIndex ? 'completed' : index === currentIndex ? 'current' : 'upcoming';
          return (
            <li
              key={stage.key}
              aria-current={state === 'current' ? 'step' : undefined}
              className={cn(
                '-me-1 flex h-8 min-w-24 flex-1 items-center justify-center gap-1 px-5 text-center text-xs font-medium',
                'ltr:[clip-path:polygon(0_0,calc(100%-10px)_0,100%_50%,calc(100%-10px)_100%,0_100%,10px_50%)]',
                'rtl:[clip-path:polygon(10px_0,100%_0,calc(100%-10px)_50%,100%_100%,10px_100%,0_50%)]',
                state === 'completed' && 'bg-success-soft text-success',
                state === 'current' && 'bg-primary text-primary-foreground',
                state === 'upcoming' && 'bg-muted text-muted-foreground',
              )}
            >
              {state === 'completed' && <Check className="size-3.5 shrink-0" aria-hidden />}
              <span className="truncate">{stage.label}</span>
              <span className="sr-only"> — {t(`stepper.${state}`)}</span>
            </li>
          );
        })}
      </ol>
      {current && (
        <p className="text-xs text-muted-foreground" data-testid="path-guidance">
          {ta('record.guidance', { stage: current.label })}
        </p>
      )}
    </nav>
  );
}
