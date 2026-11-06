import { AlertTriangle, BarChart3, Pencil, Play, Plus, Send, Square, XCircle } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { cn } from '@/lib/cn';
import {
  barOption,
  Button,
  Chart,
  Checkbox,
  ConfirmDialog,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  lineOption,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  token,
  useToast,
  type Tone,
} from '@/ui';
import type { ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  agileApi,
  useAgile,
  useAgileMutation,
  useBurndown,
  type AgileMetrics,
  type AgileView,
  type Release,
  type Sprint,
} from './agileApi';
import { burndownSeries } from './boardLogic';

const sprintTone: Record<string, Tone> = {
  planned: 'neutral',
  active: 'info',
  closed: 'warning',
  'in-approval': 'warning',
  approved: 'success',
  cancelled: 'neutral',
};

/** Series colours from the design tokens (light / dark). */
const colours = {
  committed: token('neutral'),
  completed: token('primary'),
  ideal: token('neutral'),
  remaining: token('info'),
  todo: token('neutral'),
  doing: token('warning'),
  done: token('primary'),
  scope: token('neutral'),
};

/** The sprint the charts open on: the active one, otherwise the latest started one, otherwise the first. */
function initialSprint(sprints: readonly Sprint[]): string | undefined {
  const started = [...sprints]
    .filter((s) => s.status !== 'planned' && s.status !== 'cancelled')
    .sort((a, b) => b.number - a.number);
  return sprints.find((s) => s.status === 'active')?.id ?? started[0]?.id ?? sprints[0]?.id;
}

type Action =
  | { kind: 'edit'; sprint?: Sprint }
  | { kind: 'start' | 'cancel' | 'close' | 'submit' | 'impediment'; sprint: Sprint };

/** Sprints (design §30–§31): list and lifecycle actions, review / retrospective, impediments, burndown and delivery metrics. */
export function SprintsTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('agile');
  const view = useAgile(project.id);
  const [selected, setSelected] = useState<string | undefined>(undefined);
  const [action, setAction] = useState<Action | null>(null);
  const lang = i18n.language;
  if (view.isPending) return <Skeleton className="h-64" />;
  if (view.error) return <FieldErrors error={view.error} />;
  const data = view.data;
  const perms = data.permissions;
  const sprints = [...data.sprints].sort((a, b) => a.number - b.number);
  const current = selected ?? initialSprint(sprints);
  const currentSprint = sprints.find((s) => s.id === current);
  const n = (value: number | null | undefined) =>
    value === null || value === undefined ? '—' : formatNumber(value, lang);
  return (
    <div className="flex flex-col gap-6" data-testid="agile-sprints">
      <StatusBanner
        tone="info"
        title={data.approvalMode === 'sprint' ? t('sprints.modeSprint') : t('sprints.modeMonthly')}
      />
      <section className="flex flex-col gap-3">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">{t('sprints.title')}</h2>
          {perms.canRunSprints && (
            <Button size="sm" onClick={() => setAction({ kind: 'edit' })} data-testid="sprint-add">
              <Plus aria-hidden />
              {t('sprints.add')}
            </Button>
          )}
        </header>
        {sprints.length === 0 ? (
          <EmptyState title={t('sprints.none')} />
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('sprints.number')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.name')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.dates')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.committed')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.completed')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.planned')}</TableHeaderCell>
                  <TableHeaderCell>{t('sprints.capacity')}</TableHeaderCell>
                  <TableHeaderCell>{t('common.actions')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {sprints.map((s) => (
                  <TableRow
                    key={s.id}
                    data-testid="sprint-row"
                    data-status={s.status}
                    className={cn(s.id === current && 'bg-muted/50')}
                  >
                    <TableCell className="tabular-nums font-medium">{n(s.number)}</TableCell>
                    <TableCell dir="auto">
                      {s.name ?? t('charts.sprintN', { n: n(s.number) })}
                      {s.goal && <span className="block text-xs text-muted-foreground">{s.goal}</span>}
                    </TableCell>
                    <TableCell className="text-sm">
                      {s.startDate ? formatDate(s.startDate, lang) : '—'} –{' '}
                      {s.endDate ? formatDate(s.endDate, lang) : '—'}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={sprintTone[s.status] ?? 'neutral'}>
                        {t(`status.sprint.${s.status as 'planned'}`)}
                      </StatusChip>
                      {s.approvalPeriod && (
                        <span className="block text-xs text-muted-foreground">
                          {t('sprints.approvalPeriod', { period: s.approvalPeriod })}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">{n(s.committedPoints)}</TableCell>
                    <TableCell className="tabular-nums">{n(s.completedPoints)}</TableCell>
                    <TableCell className="tabular-nums">{n(s.plannedPoints)}</TableCell>
                    <TableCell className="tabular-nums">{n(s.capacityPoints)}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        <Button
                          size="sm"
                          variant={s.id === current ? 'secondary' : 'ghost'}
                          aria-label={t('sprints.select', { n: n(s.number) })}
                          aria-pressed={s.id === current}
                          onClick={() => setSelected(s.id)}
                        >
                          <BarChart3 aria-hidden />
                        </Button>
                        <SprintActions sprint={s} view={data} onAction={setAction} />
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      <BurndownPanel sprint={currentSprint} />
      <MetricsPanels metrics={data.metrics} releases={data.releases} />

      {action?.kind === 'edit' && (
        <SprintDialog project={project} view={data} sprint={action.sprint} onClose={() => setAction(null)} />
      )}
      {(action?.kind === 'start' || action?.kind === 'cancel') && (
        <ConfirmAction
          project={project}
          kind={action.kind}
          sprint={action.sprint}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'close' && (
        <ReviewDialog project={project} view={data} sprint={action.sprint} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'submit' && (
        <SubmitDialog project={project} sprint={action.sprint} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'impediment' && (
        <ImpedimentDialog project={project} sprint={action.sprint} onClose={() => setAction(null)} />
      )}
    </div>
  );
}

function SprintActions({
  sprint,
  view,
  onAction,
}: {
  sprint: Sprint;
  view: AgileView;
  onAction: (action: Action) => void;
}) {
  const { t } = useTranslation('agile');
  const run = view.permissions.canRunSprints;
  const buttons: ReactNode[] = [];
  const add = (
    key: string,
    label: string,
    icon: ReactNode,
    next: Action,
    variant: 'ghost' | 'outline' = 'outline',
  ) =>
    buttons.push(
      <Button
        key={key}
        size="sm"
        variant={variant}
        onClick={() => onAction(next)}
        data-testid={`sprint-${key}`}
      >
        {icon}
        {label}
      </Button>,
    );
  if (run && (sprint.status === 'planned' || sprint.status === 'active'))
    add('edit', t('sprints.edit'), <Pencil aria-hidden />, { kind: 'edit', sprint }, 'ghost');
  if (run && sprint.status === 'planned')
    add('start', t('sprints.start'), <Play className="rtl:-scale-x-100" aria-hidden />, {
      kind: 'start',
      sprint,
    });
  if (run && sprint.status === 'active')
    add('close', t('sprints.close'), <Square aria-hidden />, { kind: 'close', sprint });
  if (run && sprint.status === 'closed')
    add('review', t('sprints.editReview'), <Pencil aria-hidden />, { kind: 'close', sprint }, 'ghost');
  if (view.permissions.canSubmitSprint && sprint.status === 'closed')
    add('submit', t('sprints.submit'), <Send className="rtl:-scale-x-100" aria-hidden />, {
      kind: 'submit',
      sprint,
    });
  if (run && (sprint.status === 'active' || sprint.status === 'planned'))
    add(
      'impediment',
      t('sprints.impediment'),
      <AlertTriangle aria-hidden />,
      { kind: 'impediment', sprint },
      'ghost',
    );
  if (run && sprint.status === 'planned')
    add('cancel', t('sprints.cancel'), <XCircle aria-hidden />, { kind: 'cancel', sprint }, 'ghost');
  return <>{buttons}</>;
}

function DialogFooter({
  loading,
  onSave,
  label,
  testId,
}: {
  loading: boolean;
  onSave: () => void;
  label?: string;
  testId?: string;
}) {
  const { t: tc } = useTranslation();
  return (
    <>
      <DialogClose asChild>
        <Button variant="outline">{tc('actions.cancel')}</Button>
      </DialogClose>
      <Button loading={loading} onClick={onSave} data-testid={testId ?? 'agile-dialog-save'}>
        {label ?? tc('actions.save')}
      </Button>
    </>
  );
}

function SprintDialog({
  project,
  view,
  sprint,
  onClose,
}: {
  project: ProjectFull;
  view: AgileView;
  sprint?: Sprint | undefined;
  onClose: () => void;
}) {
  const { t } = useTranslation('agile');
  const toast = useToast();
  const save = useAgileMutation(project.id, agileApi.saveSprint);
  const [v, setV] = useState({
    name: sprint?.name ?? '',
    goal: sprint?.goal ?? '',
    startDate: sprint?.startDate ?? undefined,
    endDate: sprint?.endDate ?? undefined,
    capacity:
      sprint?.capacityPoints !== null && sprint?.capacityPoints !== undefined
        ? String(sprint.capacityPoints)
        : '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const submit = () =>
    save.mutate(
      {
        id: project.id,
        ...(sprint ? { itemId: sprint.id, version: sprint.version } : {}),
        values: {
          name: v.name.trim() || null,
          goal: v.goal.trim() || null,
          startDate: v.startDate ?? null,
          endDate: v.endDate ?? null,
          capacityPoints: v.capacity.trim() === '' ? null : Number(v.capacity),
        },
      },
      {
        onSuccess: () => {
          toast({ title: t('common.saved') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={sprint ? t('sprints.edit') : t('sprints.add')}
        description={
          view.sprintLengthDays ? `${t('methodology.sprintLength')}: ${view.sprintLengthDays}` : undefined
        }
        className="max-w-xl"
        footer={<DialogFooter loading={save.isPending} onSave={submit} />}
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="sprint-dialog">
          {save.error && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
          <FormField label={t('sprints.name')} className="md:col-span-2">
            <Input
              dir="auto"
              value={v.name}
              onChange={(e) => set({ name: e.target.value })}
              data-testid="sprint-name"
            />
          </FormField>
          <FormField label={t('sprints.goal')} className="md:col-span-2">
            <Textarea dir="auto" value={v.goal} onChange={(e) => set({ goal: e.target.value })} />
          </FormField>
          <FormField label={t('sprints.startDate')} required>
            <DatePicker value={v.startDate} onChange={(d) => set({ startDate: d })} />
          </FormField>
          <FormField label={t('sprints.endDate')} required>
            <DatePicker value={v.endDate} onChange={(d) => set({ endDate: d })} />
          </FormField>
          <FormField label={t('sprints.capacity')}>
            <Input
              dir="ltr"
              type="number"
              min={0}
              value={v.capacity}
              onChange={(e) => set({ capacity: e.target.value })}
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ConfirmAction({
  project,
  kind,
  sprint,
  onClose,
}: {
  project: ProjectFull;
  kind: 'start' | 'cancel';
  sprint: Sprint;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const start = useAgileMutation(project.id, agileApi.startSprint);
  const cancel = useAgileMutation(project.id, agileApi.cancelSprint);
  const mutation = kind === 'start' ? start : cancel;
  const n = formatNumber(sprint.number, i18n.language);
  return (
    <ConfirmDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title={kind === 'start' ? t('sprints.confirmStart', { n }) : t('sprints.cancel')}
      description={kind === 'start' ? t('sprints.confirmStartText') : t('sprints.confirmCancelText', { n })}
      confirmLabel={kind === 'start' ? t('sprints.start') : t('sprints.cancel')}
      destructive={kind === 'cancel'}
      loading={mutation.isPending}
      onConfirm={() =>
        mutation.mutate(
          { sid: sprint.id, version: sprint.version },
          {
            onSuccess: () => {
              toast({ title: kind === 'start' ? t('sprints.started') : t('sprints.cancelled') });
              onClose();
            },
            onError: (error) => toast({ tone: 'error', title: errorMessage(tc, error) }),
          },
        )
      }
    />
  );
}

/** Close an active sprint with its review and retrospective, or edit the review of a closed sprint. */
function ReviewDialog({
  project,
  view,
  sprint,
  onClose,
}: {
  project: ProjectFull;
  view: AgileView;
  sprint: Sprint;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const toast = useToast();
  const lang = i18n.language;
  const close = useAgileMutation(project.id, agileApi.closeSprint);
  const done = view.stories.filter(
    (s) => s.sprintId === sprint.id && s.status !== 'cancelled' && s.category === 'done',
  );
  const [v, setV] = useState({
    demoOutcome: sprint.demoOutcome ?? '',
    stakeholderFeedback: sprint.stakeholderFeedback ?? '',
    wentWell: sprint.wentWell ?? '',
    toImprove: sprint.toImprove ?? '',
    actions: sprint.actions ?? '',
  });
  const [accepted, setAccepted] = useState<Set<string>>(
    () =>
      new Set(
        sprint.status === 'active' ? done.map((s) => s.id) : done.filter((s) => s.accepted).map((s) => s.id),
      ),
  );
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const toggle = (id: string, on: boolean) =>
    setAccepted((current) => {
      const next = new Set(current);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  const submit = () =>
    close.mutate(
      {
        sid: sprint.id,
        version: sprint.version,
        values: {
          demoOutcome: v.demoOutcome.trim() || null,
          stakeholderFeedback: v.stakeholderFeedback.trim() || null,
          wentWell: v.wentWell.trim() || null,
          toImprove: v.toImprove.trim() || null,
          actions: v.actions.trim() || null,
          acceptedStoryIds: [...accepted],
        },
      },
      {
        onSuccess: () => {
          toast({ title: sprint.status === 'active' ? t('sprints.closed') : t('common.saved') });
          onClose();
        },
      },
    );
  const n = formatNumber(sprint.number, lang);
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('sprints.review.title', { n })}
        description={sprint.status === 'active' ? t('sprints.review.unfinishedNote') : undefined}
        className="max-w-3xl"
        footer={
          <DialogFooter
            loading={close.isPending}
            onSave={submit}
            label={sprint.status === 'active' ? t('sprints.close') : undefined}
            testId="sprint-close-save"
          />
        }
      >
        <div className="flex flex-col gap-4" data-testid="sprint-review-dialog">
          {close.error && <FieldErrors error={close.error} />}
          <fieldset className="grid gap-3 md:grid-cols-2">
            <legend className="mb-2 text-sm font-semibold">{t('sprints.review.reviewSection')}</legend>
            <FormField label={t('sprints.review.demoOutcome')} required>
              <Textarea
                dir="auto"
                value={v.demoOutcome}
                onChange={(e) => set({ demoOutcome: e.target.value })}
                data-testid="sprint-demo-outcome"
              />
            </FormField>
            <FormField label={t('sprints.review.stakeholderFeedback')}>
              <Textarea
                dir="auto"
                value={v.stakeholderFeedback}
                onChange={(e) => set({ stakeholderFeedback: e.target.value })}
              />
            </FormField>
          </fieldset>
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-semibold">{t('sprints.review.accepted')}</legend>
            <p className="text-xs text-muted-foreground">{t('sprints.review.acceptedHint')}</p>
            {done.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('sprints.review.noDone')}</p>
            ) : (
              <ul className="flex flex-col gap-2" data-testid="sprint-accepted-list">
                {done.map((s) => (
                  <li key={s.id}>
                    <Checkbox
                      label={
                        <>
                          <bdi className="text-xs text-muted-foreground">{s.code}</bdi>{' '}
                          <span dir="auto">{s.title}</span> ·{' '}
                          {t('common.pointsValue', { value: formatNumber(s.storyPoints, lang) })}
                        </>
                      }
                      checked={accepted.has(s.id)}
                      disabled={s.approvedDone}
                      onChange={(e) => toggle(s.id, e.target.checked)}
                    />
                  </li>
                ))}
              </ul>
            )}
          </fieldset>
          <fieldset className="grid gap-3 md:grid-cols-3">
            <legend className="mb-2 text-sm font-semibold">{t('sprints.review.retroSection')}</legend>
            <FormField label={t('sprints.review.wentWell')}>
              <Textarea dir="auto" value={v.wentWell} onChange={(e) => set({ wentWell: e.target.value })} />
            </FormField>
            <FormField label={t('sprints.review.toImprove')}>
              <Textarea dir="auto" value={v.toImprove} onChange={(e) => set({ toImprove: e.target.value })} />
            </FormField>
            <FormField label={t('sprints.review.actions')}>
              <Textarea dir="auto" value={v.actions} onChange={(e) => set({ actions: e.target.value })} />
            </FormField>
          </fieldset>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function SubmitDialog({
  project,
  sprint,
  onClose,
}: {
  project: ProjectFull;
  sprint: Sprint;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const toast = useToast();
  const submit = useAgileMutation(project.id, agileApi.submitSprint);
  const [comment, setComment] = useState('');
  const send = () =>
    submit.mutate(
      { sid: sprint.id, version: sprint.version, comment: comment.trim() || null },
      {
        onSuccess: () => {
          toast({ title: t('sprints.submitted') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('sprints.submitTitle', { n: formatNumber(sprint.number, i18n.language) })}
        description={t('sprints.modeSprint')}
        footer={
          <DialogFooter
            loading={submit.isPending}
            onSave={send}
            label={t('sprints.submit')}
            testId="sprint-submit-confirm"
          />
        }
      >
        <div className="flex flex-col gap-3" data-testid="sprint-submit-dialog">
          {submit.error && <FieldErrors error={submit.error} />}
          <FormField label={t('sprints.comment')}>
            <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ImpedimentDialog({
  project,
  sprint,
  onClose,
}: {
  project: ProjectFull;
  sprint: Sprint;
  onClose: () => void;
}) {
  const { t } = useTranslation('agile');
  const toast = useToast();
  const raise = useAgileMutation(project.id, agileApi.raiseImpediment);
  const [v, setV] = useState<{
    title: string;
    description: string;
    owner: string | undefined;
    priority: string;
  }>({
    title: '',
    description: '',
    owner: undefined,
    priority: 'medium',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const send = () =>
    raise.mutate(
      {
        sid: sprint.id,
        title: v.title.trim() || null,
        description: v.description.trim() || null,
        ownerEmployeeId: v.owner ?? null,
        priority: v.priority,
      },
      {
        onSuccess: () => {
          toast({ title: t('sprints.impedimentRaised') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('sprints.impedimentForm.title')}
        description={t('sprints.impedimentForm.hint')}
        className="max-w-xl"
        footer={<DialogFooter loading={raise.isPending} onSave={send} label={t('sprints.impediment')} />}
      >
        <div className="grid gap-3" data-testid="impediment-dialog">
          {raise.error && <FieldErrors error={raise.error} />}
          <FormField label={t('sprints.impedimentForm.name')} required>
            <Input
              dir="auto"
              value={v.title}
              onChange={(e) => set({ title: e.target.value })}
              data-testid="impediment-title"
            />
          </FormField>
          <FormField label={t('sprints.impedimentForm.description')}>
            <Textarea
              dir="auto"
              value={v.description}
              onChange={(e) => set({ description: e.target.value })}
            />
          </FormField>
          <FormField label={t('sprints.impedimentForm.owner')}>
            <EmployeePicker value={v.owner} onChange={(owner) => set({ owner })} />
          </FormField>
          <FormField label={t('sprints.impedimentForm.priority')}>
            <Select
              value={v.priority}
              onValueChange={(priority) => set({ priority })}
              options={(['low', 'medium', 'high'] as const).map((p) => ({
                value: p,
                label: t(`sprints.impedimentForm.priorities.${p}`),
              }))}
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ---- charts ------------------------------------------------------------------------------------------------------------------

function ChartCard({
  title,
  summary,
  children,
  testId,
}: {
  title: string;
  summary?: ReactNode;
  children: ReactNode;
  testId?: string;
}) {
  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-card p-4" data-testid={testId}>
      <h3 className="font-semibold">{title}</h3>
      {summary && <p className="text-sm text-muted-foreground">{summary}</p>}
      {children}
    </section>
  );
}

function BurndownPanel({ sprint }: { sprint: Sprint | undefined }) {
  const { t, i18n } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const lang = i18n.language;
  const burndown = useBurndown(sprint && sprint.status !== 'planned' ? sprint.id : undefined);
  const f = (v: number) => formatNumber(v, lang);
  const spec = useMemo(() => {
    if (!burndown.data) return null;
    const series = burndownSeries(burndown.data.points);
    return lineOption({
      categories: series.dates.map((d) => ({ key: d, label: formatDate(d, lang) })),
      series: [
        { name: t('charts.ideal'), values: series.ideal, color: colours.ideal, dashed: true, noSymbol: true },
        { name: t('charts.remaining'), values: series.remaining, color: colours.remaining },
      ],
      integer: true,
      format: f,
      categoryHeader: t('charts.date'),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [burndown.data, lang, t]);
  if (!sprint || sprint.status === 'planned') {
    return (
      <ChartCard title={t('charts.burndown')} testId="sprint-burndown">
        <p className="text-sm text-muted-foreground">{t('charts.noBurndown')}</p>
      </ChartCard>
    );
  }
  const n = formatNumber(sprint.number, lang);
  const points = burndown.data?.points ?? [];
  const lastKnown = [...points].reverse().find((p) => p.remaining >= 0);
  return (
    <ChartCard
      title={`${t('charts.burndown')} · ${t('charts.sprintN', { n })}`}
      testId="sprint-burndown"
      summary={
        burndown.data
          ? t('charts.burndownSummary', {
              committed: f(burndown.data.committed),
              remaining: f(lastKnown?.remaining ?? burndown.data.committed),
            })
          : undefined
      }
    >
      {burndown.isPending ? (
        <Skeleton className="h-64" />
      ) : burndown.error ? (
        <StatusBanner tone="danger" title={errorMessage(tc, burndown.error)} />
      ) : (
        spec && <Chart {...spec} label={t('charts.burndownLabel', { n })} />
      )}
    </ChartCard>
  );
}

function MetricsPanels({ metrics, releases }: { metrics: AgileMetrics; releases: Release[] }) {
  const { t, i18n } = useTranslation('agile');
  const lang = i18n.language;
  const f = (v: number) => formatNumber(v, lang);
  const velocity = useMemo(
    () =>
      barOption({
        categories: metrics.velocity.map((v) => ({
          key: String(v.number),
          label: t('charts.sprintN', { n: f(v.number) }),
        })),
        series: [
          {
            name: t('sprints.committed'),
            values: metrics.velocity.map((v) => v.committed),
            color: colours.committed,
          },
          {
            name: t('sprints.completed'),
            values: metrics.velocity.map((v) => v.completed),
            color: colours.completed,
          },
        ],
        integer: true,
        format: f,
        categoryHeader: t('sprints.number'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [metrics.velocity, lang, t],
  );
  const flow = useMemo(
    () =>
      lineOption({
        categories: metrics.cumulativeFlow.map((p) => ({ key: p.date, label: formatDate(p.date, lang) })),
        series: [
          {
            name: t('category.done'),
            values: metrics.cumulativeFlow.map((p) => p.done),
            color: colours.done,
            area: true,
            stack: 'flow',
            noSymbol: true,
          },
          {
            name: t('category.doing'),
            values: metrics.cumulativeFlow.map((p) => p.doing),
            color: colours.doing,
            area: true,
            stack: 'flow',
            noSymbol: true,
          },
          {
            name: t('category.todo'),
            values: metrics.cumulativeFlow.map((p) => p.toDo),
            color: colours.todo,
            area: true,
            stack: 'flow',
            noSymbol: true,
          },
        ],
        integer: true,
        format: f,
        categoryHeader: t('charts.date'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [metrics.cumulativeFlow, lang, t],
  );
  const cycle = useMemo(
    () =>
      barOption({
        categories: metrics.cycleTimes.map((c) => ({ key: c.code, label: c.code })),
        series: [
          { name: t('charts.days'), values: metrics.cycleTimes.map((c) => c.days), color: colours.remaining },
        ],
        showValues: false,
        format: f,
        valueName: t('charts.days'),
        categoryHeader: t('charts.story'),
        ...(metrics.averageCycleTime !== null
          ? {
              markLine: {
                value: metrics.averageCycleTime,
                label: f(Math.round(metrics.averageCycleTime * 10) / 10),
              },
            }
          : {}),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [metrics.cycleTimes, metrics.averageCycleTime, lang, t],
  );
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <ChartCard title={t('charts.velocity')} testId="sprint-velocity">
        {metrics.velocity.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('charts.noData')}</p>
        ) : (
          <Chart {...velocity} height={240} label={t('charts.velocityLabel')} />
        )}
      </ChartCard>

      <ChartCard
        title={t('charts.cycleTime')}
        testId="sprint-cycle-time"
        summary={
          metrics.averageCycleTime !== null
            ? t('charts.averageCycle', { value: f(Math.round(metrics.averageCycleTime * 10) / 10) })
            : undefined
        }
      >
        {metrics.cycleTimes.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('charts.noData')}</p>
        ) : (
          <Chart {...cycle} height={240} label={t('charts.cycleTimeLabel')} />
        )}
      </ChartCard>

      <ChartCard title={t('charts.flow')} testId="sprint-cumulative-flow">
        {metrics.cumulativeFlow.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('charts.noData')}</p>
        ) : (
          <Chart {...flow} height={240} label={t('charts.flowLabel')} />
        )}
      </ChartCard>

      <div className="flex flex-col gap-4" data-testid="release-burnup">
        {metrics.burnup.length === 0 ? (
          <ChartCard title={t('charts.burnup')}>
            <p className="text-sm text-muted-foreground">{t('charts.noData')}</p>
          </ChartCard>
        ) : (
          metrics.burnup.map((b) => {
            const release = releases.find((r) => r.id === b.releaseId);
            return (
              <BurnupChart
                key={b.releaseId}
                name={release ? pickText(lang, release.nameAr, release.nameEn) : t('backlog.noRelease')}
                points={b.points}
              />
            );
          })
        )}
      </div>
    </div>
  );
}

function BurnupChart({ name, points }: { name: string; points: AgileMetrics['burnup'][number]['points'] }) {
  const { t, i18n } = useTranslation('agile');
  const lang = i18n.language;
  const spec = useMemo(
    () =>
      lineOption({
        categories: points.map((p) => ({ key: p.date, label: p.label })),
        series: [
          {
            name: t('charts.scope'),
            values: points.map((p) => p.scope),
            color: colours.scope,
            dashed: true,
            step: true,
          },
          { name: t('charts.done'), values: points.map((p) => p.done), color: colours.done, area: true },
        ],
        integer: true,
        edgeToEdge: false,
        format: (v) => formatNumber(v, lang),
        categoryHeader: t('charts.date'),
      }),
    [points, lang, t],
  );
  return (
    <ChartCard title={`${t('charts.burnup')} · ${name}`}>
      {points.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('charts.noData')}</p>
      ) : (
        <Chart {...spec} height={240} label={t('charts.burnupLabel', { name })} />
      )}
    </ChartCard>
  );
}
