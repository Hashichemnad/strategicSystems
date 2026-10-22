import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
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

/** Record lifecycle stepper (CLAUDE.md §10). Reads in the user's direction; the current stage has aria-current. */
export function StageStepper({ stages, current, className }: StageStepperProps) {
  const { t } = useTranslation();
  const currentIndex = Math.max(
    0,
    stages.findIndex((stage) => stage.key === current),
  );

  return (
    <nav aria-label={t('stepper.label')} className={cn('w-full overflow-x-auto', className)}>
      <ol className="flex min-w-max items-center gap-2">
        {stages.map((stage, index) => {
          const state = index < currentIndex ? 'completed' : index === currentIndex ? 'current' : 'upcoming';
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
