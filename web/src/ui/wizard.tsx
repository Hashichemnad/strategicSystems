import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { Button } from './button';
import { StageStepper } from './stage-stepper';

export interface WizardStep {
  key: string;
  title: string;
  content: ReactNode;
  /** Runs before moving forward; return false to stay (e.g. react-hook-form `trigger`). */
  validate?: () => boolean | Promise<boolean>;
}

interface WizardProps {
  steps: readonly WizardStep[];
  onFinish: () => void | Promise<void>;
  onSaveDraft?: () => void | Promise<void>;
  finishLabel?: string;
  /** Names for Back / Next when the wizard contains another one (e.g. the setup wizard's import step), so both can be told apart. */
  backLabel?: string;
  nextLabel?: string;
  busy?: boolean;
  /** Blocks finishing and saving a draft (e.g. AI-filled values still to review) while steps stay navigable. */
  finishDisabled?: boolean;
  className?: string;
}

/** Multi-step form with per-step validation, save-as-draft and a progress stepper (SPEC §0.3). */
export function Wizard({
  steps,
  onFinish,
  onSaveDraft,
  finishLabel,
  backLabel,
  nextLabel,
  busy,
  finishDisabled,
  className,
}: WizardProps) {
  const { t } = useTranslation();
  const [index, setIndex] = useState(0);
  const step = steps[index];
  if (!step) return null;
  const last = index === steps.length - 1;

  async function next() {
    if (step?.validate && !(await step.validate())) return;
    if (last) await onFinish();
    else setIndex((i) => i + 1);
  }

  return (
    <div className={cn('flex flex-col gap-6', className)}>
      <StageStepper stages={steps.map((s) => ({ key: s.key, label: s.title }))} current={step.key} />
      <section aria-labelledby={`wizard-step-${step.key}`} className="flex flex-col gap-4">
        <div className="flex flex-col gap-0.5">
          <p className="text-xs text-muted-foreground">
            {t('wizard.stepOf', { current: index + 1, total: steps.length })}
          </p>
          <h2 id={`wizard-step-${step.key}`} className="text-lg font-semibold">
            {step.title}
          </h2>
        </div>
        {step.content}
      </section>
      <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-4">
        <div>
          {onSaveDraft && (
            <Button variant="ghost" onClick={() => void onSaveDraft()} disabled={busy || finishDisabled}>
              {t('actions.saveDraft')}
            </Button>
          )}
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => setIndex((i) => i - 1)} disabled={index === 0 || busy}>
            {backLabel ?? t('actions.back')}
          </Button>
          <Button onClick={() => void next()} loading={busy} disabled={last && finishDisabled}>
            {last ? (finishLabel ?? t('actions.finish')) : (nextLabel ?? t('actions.next'))}
          </Button>
        </div>
      </div>
    </div>
  );
}
