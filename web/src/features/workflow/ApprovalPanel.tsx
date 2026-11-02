import {
  ArrowRightLeft,
  Check,
  CircleDashed,
  CircleSlash,
  Clock,
  CornerUpLeft,
  Send,
  SkipForward,
  TriangleAlert,
  X,
  type LucideIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import {
  newIdempotencyKey,
  useAct,
  useCurrentStatus,
  useSubjectApprovals,
  type History,
  type Instance,
  type MyTask,
  type PersonName,
  type StepState,
} from './api';

type ActionOption = MyTask['actions'][number];

export function usePersonName() {
  const { i18n } = useTranslation();
  return (person: PersonName | null | undefined) =>
    person ? pickText(i18n.language, person.nameAr, person.nameEn) || '—' : '—';
}

const instanceTones: Record<string, Tone> = {
  in_progress: 'info',
  approved: 'success',
  rejected: 'danger',
  returned: 'warning',
  cancelled: 'neutral',
};

const upcomingIcon = { icon: CircleDashed, className: 'border-border text-muted-foreground' };
const stepIcons: Record<string, { icon: LucideIcon; className: string } | undefined> = {
  done: { icon: Check, className: 'border-success bg-success text-success-foreground' },
  current: { icon: Clock, className: 'border-primary text-primary' },
  skipped: { icon: SkipForward, className: 'border-border text-muted-foreground' },
  upcoming: { icon: CircleDashed, className: 'border-border text-muted-foreground' },
  rejected: { icon: X, className: 'border-danger bg-danger text-danger-foreground' },
  returned: { icon: CornerUpLeft, className: 'border-warning bg-warning text-warning-foreground' },
  cancelled: { icon: CircleSlash, className: 'border-border text-muted-foreground' },
  notReached: { icon: CircleDashed, className: 'border-border text-muted-foreground' },
};

const actionVariants: Record<string, 'primary' | 'danger' | 'outline'> = {
  approve: 'primary',
  reject: 'danger',
  return: 'outline',
};

/** Label of a decision: the step's own label for custom buttons, the standard label otherwise. */
function useActionLabel() {
  const { t, i18n } = useTranslation('workflow');
  return (option: Pick<ActionOption, 'code' | 'labelAr' | 'labelEn'>) =>
    pickText(i18n.language, option.labelAr, option.labelEn) ||
    t(`actions.${option.code as 'approve' | 'reject' | 'return'}`, { defaultValue: option.code });
}

interface DecisionDialogProps {
  task: MyTask;
  option: ActionOption | undefined;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirms a decision with its comment (required per step configuration) and, for a delegate, the person they act for.
 * A new idempotency key per opening: a retried click replays instead of deciding twice (CLAUDE.md §11).
 */
export function DecisionDialog({ task, option, onOpenChange }: DecisionDialogProps) {
  const { t } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const name = usePersonName();
  const label = useActionLabel();
  const act = useAct();
  const [comment, setComment] = useState('');
  const [key] = useState(newIdempotencyKey);
  const [onBehalfOf, setOnBehalfOf] = useState<string>(
    task.selfAssigned ? 'self' : (task.onBehalfOf[0]?.userId ?? 'self'),
  );
  const [error, setError] = useState<string>();
  if (!option) return null;
  const choices = [
    ...(task.selfAssigned ? [{ value: 'self', label: t('panel.self') }] : []),
    ...task.onBehalfOf.map((person) => ({
      value: person.userId ?? '',
      label: t('panel.onBehalfOf', { name: name(person) }),
    })),
  ];

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent
        title={label(option)}
        description={option.commentRequired ? t('panel.commentRequired') : undefined}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={actionVariants[option.outcome] === 'danger' ? 'danger' : 'primary'}
              loading={act.isPending}
              onClick={() => {
                if (option.commentRequired && !comment.trim()) {
                  setError(t('errors.commentRequired'));
                  return;
                }
                act.mutate(
                  {
                    taskId: task.taskId,
                    action: option.code,
                    comment: comment.trim() || null,
                    onBehalfOfUserId: onBehalfOf === 'self' ? null : onBehalfOf,
                    idempotencyKey: key,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('panel.decided') });
                      onOpenChange(false);
                    },
                    onError: (failure) => setError(errorMessage(tc, failure)),
                  },
                );
              }}
            >
              {t('panel.confirm')}
            </Button>
          </>
        }
      >
        {choices.length > 1 && (
          <FormField label={t('panel.actingAs')}>
            <Select value={onBehalfOf} onValueChange={setOnBehalfOf} options={choices} />
          </FormField>
        )}
        {choices.length === 1 && !task.selfAssigned && <p className="text-sm">{choices[0]?.label}</p>}
        <FormField
          label={option.commentRequired ? t('panel.comment') : t('panel.commentOptional')}
          required={option.commentRequired}
          error={error}
        >
          <Textarea
            rows={4}
            maxLength={4000}
            value={comment}
            onChange={(e) => {
              setComment(e.target.value);
              setError(undefined);
            }}
          />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

/** The current approver's buttons (only the decisions this step allows). */
export function DecisionButtons({ task }: { task: MyTask }) {
  const label = useActionLabel();
  const [open, setOpen] = useState<ActionOption>();
  return (
    <>
      <div className="flex flex-wrap gap-2">
        {task.actions.map((option) => (
          <Button
            key={option.code}
            size="sm"
            variant={actionVariants[option.code] ?? 'outline'}
            onClick={() => setOpen(option)}
          >
            {label(option)}
          </Button>
        ))}
      </div>
      {open && (
        <DecisionDialog task={task} option={open} onOpenChange={(value) => !value && setOpen(undefined)} />
      )}
    </>
  );
}

function StepRow({ step, last }: { step: StepState; last: boolean }) {
  const { t, i18n } = useTranslation('workflow');
  const name = usePersonName();
  const { icon: Icon, className } = stepIcons[step.state] ?? upcomingIcon;
  const people = step.approvers.map(name).join('، ');
  return (
    <li className="relative flex gap-3 pb-4" aria-current={step.state === 'current' ? 'step' : undefined}>
      {!last && <span aria-hidden className="absolute top-8 bottom-0 start-[0.9rem] w-px bg-border" />}
      <span
        className={cn(
          'z-10 flex size-7 shrink-0 items-center justify-center rounded-full border-2',
          className,
        )}
      >
        <Icon className="size-3.5" aria-hidden />
      </span>
      <div className="flex min-w-0 flex-col gap-0.5">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium">
            {pickText(i18n.language, step.nameAr, step.nameEn) || step.code}
          </span>
          <StatusChip
            tone={step.state === 'current' ? 'info' : step.state === 'done' ? 'success' : 'neutral'}
          >
            {t(`stepStates.${step.state as 'done'}`)}
          </StatusChip>
          {step.overdue && (
            <StatusChip tone="danger">
              <TriangleAlert className="size-3" aria-hidden /> {t('panel.overdue')}
            </StatusChip>
          )}
        </div>
        {people && <p className="text-sm text-muted-foreground">{people}</p>}
        {step.actedBy && (
          <p className="text-sm">
            {step.onBehalfOf
              ? t('panel.actedOnBehalf', { actor: name(step.actedBy), name: name(step.onBehalfOf) })
              : name(step.actedBy)}
            {step.completedAt && (
              <span className="text-muted-foreground">
                {' '}
                · {formatDateTime(step.completedAt, i18n.language)}
              </span>
            )}
          </p>
        )}
        {step.comment && <p className="rounded bg-muted px-2 py-1 text-sm">{step.comment}</p>}
        {step.state === 'current' && step.dueAt && (
          <p className="text-xs text-muted-foreground">
            {t('panel.due', { date: formatDateTime(step.dueAt, i18n.language) })}
          </p>
        )}
      </div>
    </li>
  );
}

const eventIcons: Record<string, LucideIcon> = {
  submitted: Send,
  resubmitted: Send,
  approved: Check,
  rejected: X,
  returned: CornerUpLeft,
  'step-skipped': SkipForward,
  escalated: TriangleAlert,
  reminded: Clock,
  rerouted: ArrowRightLeft,
};

function HistoryList({ history }: { history: readonly History[] }) {
  const { t, i18n } = useTranslation('workflow');
  const name = usePersonName();
  return (
    <ol className="flex flex-col gap-3">
      {history.map((entry) => {
        const Icon = eventIcons[entry.event] ?? CircleDashed;
        const step = pickText(i18n.language, entry.stepNameAr, entry.stepNameEn) || '';
        const data = entry.data as { reason?: string } | null | undefined;
        return (
          <li key={entry.id} className="flex gap-3 text-sm">
            <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
            <div className="flex flex-col gap-0.5">
              <span>
                {t(`panel.events.${entry.event as 'submitted'}`, { step, defaultValue: entry.event })}
                {entry.event === 'step-skipped' && data?.reason && (
                  <span className="text-muted-foreground">
                    {' '}
                    — {t(`panel.skipReasons.${data.reason as 'condition'}`, { defaultValue: '' })}
                  </span>
                )}
              </span>
              <span className="text-xs text-muted-foreground">
                {entry.actor &&
                  (entry.onBehalfOf
                    ? t('panel.actedOnBehalf', { actor: name(entry.actor), name: name(entry.onBehalfOf) })
                    : name(entry.actor))}
                {entry.actor && ' · '}
                {formatDateTime(entry.occurredAt, i18n.language)}
              </span>
              {entry.comment && <p className="rounded bg-muted px-2 py-1">{entry.comment}</p>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

function InstanceCard({ instance, current }: { instance: Instance; current: boolean }) {
  const { t, i18n } = useTranslation('workflow');
  const name = usePersonName();
  return (
    <section
      className={cn('flex flex-col gap-4 rounded-lg border bg-card p-5', !current && 'bg-muted/30')}
      aria-label={t('panel.attempt', { number: instance.attempt })}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-col gap-0.5">
          <h3 className="font-semibold">
            {pickText(i18n.language, instance.processNameAr, instance.processNameEn)} ·{' '}
            {t('panel.attempt', { number: instance.attempt })}
          </h3>
          <p className="text-sm text-muted-foreground">
            {t('panel.submittedBy', {
              name: name(instance.submitter),
              date: formatDateTime(instance.submittedAt, i18n.language),
            })}
          </p>
        </div>
        <StatusChip tone={instanceTones[instance.status] ?? 'neutral'}>
          {t(`status.${instance.status as 'approved'}`)}
        </StatusChip>
      </header>
      {instance.comment && <p className="rounded bg-muted px-3 py-2 text-sm">{instance.comment}</p>}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <ol aria-label={t('panel.title')}>
          {instance.steps.map((step, index) => (
            <StepRow key={step.sort} step={step} last={index === instance.steps.length - 1} />
          ))}
        </ol>
        <div className="flex flex-col gap-3">
          {instance.myTask && (
            <div className="flex flex-col gap-2 rounded-md border border-primary/40 bg-primary/5 p-3">
              <p className="text-sm font-semibold">{t('panel.yourDecision')}</p>
              <DecisionButtons task={instance.myTask} />
            </div>
          )}
          <h4 className="text-sm font-semibold">{t('panel.history')}</h4>
          <HistoryList history={instance.history} />
        </div>
      </div>
    </section>
  );
}

/**
 * Shared approval panel for record pages (CLAUDE.md §10): status, steps with approvers and deadlines, the current approver's
 * decisions and the history timeline; earlier submissions (returned for correction) below.
 */
export function ApprovalPanel({ resourceType, resourceId }: { resourceType: string; resourceId: string }) {
  const { t } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const approvals = useSubjectApprovals(resourceType, resourceId);
  if (approvals.isPending) return <Skeleton className="h-64 rounded-lg" />;
  if (approvals.isError) return <StatusBanner tone="danger" title={errorMessage(tc, approvals.error)} />;
  const [latest, ...earlier] = approvals.data.instances;
  if (!latest)
    return <p className="rounded-lg border bg-card p-6 text-sm text-muted-foreground">{t('panel.empty')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <InstanceCard instance={latest} current />
      {earlier.length > 0 && (
        <details className="flex flex-col gap-3">
          <summary className="cursor-pointer text-sm font-medium text-primary">{t('panel.previous')}</summary>
          <div className="mt-3 flex flex-col gap-3">
            {earlier.map((instance) => (
              <InstanceCard key={instance.id} instance={instance} current={false} />
            ))}
          </div>
        </details>
      )}
    </div>
  );
}

const bannerTones: Record<string, 'info' | 'success' | 'warning' | 'danger'> = {
  info: 'info',
  success: 'success',
  warning: 'warning',
  danger: 'danger',
  neutral: 'info',
};

/**
 * "Current status" banner (SPEC §0.2): what is happening, who acts next, and only the actions the user can take now. Approval
 * decisions open the decision dialog; other actions are handled by the record page through <paramref name="onAction"/>.
 */
export function CurrentStatusBanner({
  resourceType,
  resourceId,
  onAction,
  actionLabel,
}: {
  resourceType: string;
  resourceId: string;
  onAction: (code: string) => void;
  actionLabel?: (code: string, labelKey: string) => string;
}) {
  const { i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const name = usePersonName();
  const status = useCurrentStatus(resourceType, resourceId);
  const approvals = useSubjectApprovals(resourceType, resourceId);
  const [decision, setDecision] = useState<ActionOption>();
  if (!status.data) return status.isPending ? <Skeleton className="h-16 rounded-lg" /> : null;
  const data = status.data;
  const params = Object.fromEntries(
    Object.entries(data.params).map(([key, value]) => [
      key,
      pickText(i18n.language, value.ar, value.en) || '',
    ]),
  );
  const myTask = approvals.data?.instances[0]?.myTask ?? undefined;
  const actors = data.actors.map(name).join('، ');
  return (
    <>
      <StatusBanner
        tone={bannerTones[data.tone] ?? 'info'}
        title={translateMessageKey(tc, data.messageKey, params)}
        description={
          data.actorKey && actors ? `${translateMessageKey(tc, data.actorKey)}: ${actors}` : undefined
        }
        actions={
          data.nextActions.length > 0 && (
            <>
              {data.nextActions.map((action) => (
                <Button
                  key={action.code}
                  size="sm"
                  variant={
                    action.kind === 'danger' ? 'danger' : action.kind === 'secondary' ? 'outline' : 'primary'
                  }
                  onClick={() => {
                    const option = action.taskId
                      ? myTask?.actions.find((a) => a.code === action.code)
                      : undefined;
                    if (option) setDecision(option);
                    else onAction(action.code);
                  }}
                >
                  {actionLabel?.(action.code, action.labelKey) ?? translateMessageKey(tc, action.labelKey)}
                </Button>
              ))}
            </>
          )
        }
      />
      {decision && myTask && (
        <DecisionDialog
          task={myTask}
          option={decision}
          onOpenChange={(open) => !open && setDecision(undefined)}
        />
      )}
    </>
  );
}
