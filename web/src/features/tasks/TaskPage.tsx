import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, CheckSquare, Lock, Plus, Sparkles, Trash2, Users } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkBadge } from '@/features/links/LinkBadge';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import {
  formatDuration,
  taskActions,
  useAddComment,
  useChecklist,
  useCreateSubtask,
  useDependencies,
  useFollowUps,
  useSummariseTask,
  useTask,
  useTaskComments,
  useTasks,
  useTaskTimeline,
  type TaskAction,
  type TaskDetail,
  defaultTasksSearch,
} from './api';
import { ConfidentialChip, IndicatorChip, PriorityChip, TaskStatusChip, useStatusLabel } from './TaskChips';
import { TaskActionDialog } from './TaskActionDialog';
import { TaskTable } from './TaskViews';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/tasks/$taskId');

/** Task page (SPEC §11.7): header, stage stepper, current status with the user's actions, and the tabs. */
export function TaskPage() {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('audit');
  const { t: tf } = useTranslation('files');
  const { taskId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const task = useTask(taskId);
  const statusLabel = useStatusLabel();
  const [action, setAction] = useState<TaskAction>();

  if (task.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (task.isError) return <StatusBanner tone="danger" title={errorMessage(tc, task.error)} />;
  const data = task.data;
  const title = pickText(i18n.language, data.titleAr, data.titleEn);
  const stages = (
    data.template === 'simple'
      ? ['to_do', 'in_progress', 'on_hold', 'done']
      : ['assigned', 'in_progress', 'on_hold', 'submitted', 'approved']
  ).map((key) => ({ key, label: statusLabel(data.template, key) }));
  const fieldLabels: Record<string, string> = {
    TitleAr: t('fields.titleAr'),
    TitleEn: t('fields.titleEn'),
    Description: t('fields.description'),
    Status: t('fields.status'),
    Priority: t('fields.priority'),
    DueDate: t('fields.dueDate'),
    PlannedStart: t('fields.plannedStart'),
    AssigneeEmployeeId: t('fields.assignee'),
    FeedbackPoint: t('fields.feedbackPoint'),
    FeedbackComment: t('fields.feedbackComment'),
    CompletionComment: t('fields.completionComment'),
    RejectionReason: t('fields.rejectionReason'),
    CancelReason: t('fields.cancelReason'),
    PercentComplete: t('fields.percentComplete'),
    Confidential: t('fields.confidentialShort'),
  };

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/tasks"
        search={{ ...defaultTasksSearch, tab: 'assigned' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader
        title={title}
        description={data.number ?? undefined}
        meta={
          <>
            <OriginBadge type="tasks.task" id={data.id} />
            <TaskStatusChip template={data.template} status={data.status} />
            <PriorityChip priority={data.priority} />
            <IndicatorChip indicator={data.indicator} />
            {data.confidential && <ConfidentialChip />}
          </>
        }
      />
      <dl className="grid gap-3 rounded-lg border bg-card p-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <Fact label={t('fields.assignee')}>
          {pickText(i18n.language, data.assignee.nameAr, data.assignee.nameEn)}
        </Fact>
        <Fact label={t('fields.creator')}>
          {pickText(i18n.language, data.creator.nameAr, data.creator.nameEn)}
          {data.enteredBy && (
            <span className="block text-xs text-muted-foreground">
              {t('fields.enteredBy')}: {pickText(i18n.language, data.enteredBy.nameAr, data.enteredBy.nameEn)}
            </span>
          )}
        </Fact>
        <Fact label={t('fields.dueDate')}>
          {data.dueDate ? formatDate(data.dueDate, i18n.language) : '—'}
          {data.dueAt && (
            <span className="block text-xs text-muted-foreground">
              {formatDateTime(data.dueAt, i18n.language)}
            </span>
          )}
        </Fact>
        <Fact label={t('fields.actualHours')}>{formatDuration(data.actualSeconds)}</Fact>
      </dl>
      {data.status !== 'draft' && data.status !== 'cancelled' && data.status !== 'rejected' && (
        <StageStepper stages={stages} current={data.status} />
      )}
      {data.confidential && <StatusBanner tone="warning" title={t('detail.confidentialBanner')} />}
      <CurrentStatusBanner
        resourceType="tasks.task"
        resourceId={data.id}
        actionLabel={(code) => t(`actions.${code}` as 'actions.accept')}
        onAction={(code) => {
          if ((taskActions as readonly string[]).includes(code)) setAction(code as TaskAction);
        }}
      />
      {data.group && (
        <StatusBanner
          tone="info"
          title={t('detail.partOfGroup', { finished: data.group.finished, total: data.group.total })}
          actions={
            <Button variant="outline" size="sm" asChild>
              <Link to="/tasks/groups/$groupId" params={{ groupId: data.group.id }}>
                <Users aria-hidden />
                {t('detail.openGroup')}
              </Link>
            </Button>
          }
        />
      )}
      <Tabs value={tab} onValueChange={(value) => void navigate({ search: { tab: value as 'overview' } })}>
        <TabsList>
          <TabsTrigger value="overview">{t('detail.tabs.overview')}</TabsTrigger>
          <TabsTrigger value="timeline">{t('detail.tabs.timeline')}</TabsTrigger>
          <TabsTrigger value="comments">{t('detail.tabs.comments')}</TabsTrigger>
          <TabsTrigger value="documents">{tf('tab')}</TabsTrigger>
          <TabsTrigger value="followUps">{t('detail.tabs.followUps')}</TabsTrigger>
          <TabsTrigger value="history">{ta('history.tab')}</TabsTrigger>
        </TabsList>
        <TabsContent value="overview">
          <Overview task={data} />
          <ExternalReferencesSection type="tasks.task" id={data.id} />
        </TabsContent>
        <TabsContent value="timeline">
          <TimelineTab taskId={data.id} template={data.template} />
        </TabsContent>
        <TabsContent value="comments">
          <CommentsTab task={data} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel ownerType="tasks.task" ownerId={data.id} canEdit={data.capabilities.canUpload} />
        </TabsContent>
        <TabsContent value="followUps">
          <FollowUpsTab task={data} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="task" id={data.id} fieldLabels={fieldLabels} />
        </TabsContent>
      </Tabs>
      {action && (
        <TaskActionDialog
          task={data}
          action={action}
          feedbackScaleMax={data.feedbackScaleMax}
          onClose={() => setAction(undefined)}
        />
      )}
    </div>
  );
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-medium">{children}</dd>
    </div>
  );
}

function Section({
  title,
  children,
  actions,
}: {
  title: string;
  children: React.ReactNode;
  actions?: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{title}</h2>
        {actions}
      </header>
      {children}
    </section>
  );
}

function Overview({ task }: { task: TaskDetail }) {
  const { t, i18n } = useTranslation('tasks');
  const checklist = useChecklist(task.id);
  const dependencies = useDependencies(task.id);
  const [checkText, setCheckText] = useState('');
  const [subtask, setSubtask] = useState(false);
  const [predecessorSearch, setPredecessorSearch] = useState('');
  const candidates = useTasks(
    { view: 'all', search: predecessorSearch, pageSize: 5, openOnly: true },
    predecessorSearch.length > 1,
  );
  const openPredecessors = task.dependencies.filter((d) => !d.closed).length;
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="flex flex-col gap-4 lg:col-span-2">
        <Section title={t('fields.description')}>
          <p className="whitespace-pre-wrap text-sm">{task.description ?? '—'}</p>
          {task.completionComment && (
            <p className="rounded-md bg-muted p-3 text-sm">
              <span className="font-medium">{t('fields.completionComment')}: </span>
              {task.completionComment}
            </p>
          )}
          {task.feedbackPoint !== null && (
            <p className="rounded-md bg-emerald-50 p-3 text-sm dark:bg-emerald-950/40">
              <span className="font-medium">{t('fields.feedbackPoint')}: </span>
              {task.feedbackPoint} / {task.feedbackScaleMax}
              {task.feedbackComment ? ` — ${task.feedbackComment}` : ''}
            </p>
          )}
        </Section>
        <Section title={t('fields.checklist')}>
          {task.checklist.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('detail.checklistEmpty')}</p>
          ) : (
            <ul className="flex flex-col gap-2" data-testid="checklist">
              {task.checklist.map((item) => (
                <li key={item.id} className="flex items-center justify-between gap-2">
                  <Checkbox
                    label={
                      <span className={item.done ? 'text-muted-foreground line-through' : ''}>
                        {item.text}
                      </span>
                    }
                    checked={item.done}
                    disabled={!task.capabilities.canManageChecklist}
                    onChange={(e) => checklist.update.mutate({ itemId: item.id, done: e.target.checked })}
                  />
                  {task.capabilities.canManageChecklist && (
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={t('detail.remove')}
                      onClick={() => checklist.remove.mutate(item.id)}
                    >
                      <Trash2 aria-hidden />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
          {task.capabilities.canManageChecklist && (
            <div className="flex gap-2">
              <Input
                value={checkText}
                maxLength={500}
                aria-label={t('newTask.checklistPlaceholder')}
                placeholder={t('newTask.checklistPlaceholder')}
                onChange={(e) => setCheckText(e.target.value)}
              />
              <Button
                variant="outline"
                disabled={!checkText.trim()}
                onClick={() => checklist.add.mutate(checkText.trim(), { onSuccess: () => setCheckText('') })}
              >
                <CheckSquare aria-hidden />
                {t('detail.addChecklist')}
              </Button>
            </div>
          )}
        </Section>
        <Section
          title={t('detail.subtasks')}
          actions={
            task.capabilities.canCreateSubtask && (
              <Button size="sm" variant="outline" onClick={() => setSubtask(true)}>
                <Plus aria-hidden />
                {t('actions.create-subtask')}
              </Button>
            )
          }
        >
          {task.subtasks.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('detail.noSubtasks')}</p>
          ) : (
            <TaskTable tasks={task.subtasks} showCreator={false} />
          )}
        </Section>
      </div>
      <div className="flex flex-col gap-4">
        <Section title={t('detail.linkedRecord')}>
          {task.link ? (
            <LinkBadge view={task.link} />
          ) : (
            <p className="text-sm text-muted-foreground">{t('detail.noLink')}</p>
          )}
          {task.parent && (
            <Link
              to="/tasks/$taskId"
              params={{ taskId: task.parent.id }}
              search={{ tab: 'overview' }}
              className="text-sm text-primary hover:underline"
            >
              {t('detail.openParent')}:{' '}
              {[task.parent.number, pickText(i18n.language, task.parent.titleAr, task.parent.titleEn)]
                .filter(Boolean)
                .join(' · ')}
            </Link>
          )}
        </Section>
        <Section title={t('detail.dependencies')}>
          {openPredecessors > 0 && (
            <StatusBanner tone="warning" title={t('detail.waitingFor', { count: openPredecessors })} />
          )}
          {task.dependencies.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('detail.noDependencies')}</p>
          ) : (
            <ul className="flex flex-col gap-2 text-sm">
              {task.dependencies.map((d) => (
                <li key={d.id} className="flex items-center justify-between gap-2">
                  {d.number ? (
                    <Link
                      to="/tasks/$taskId"
                      params={{ taskId: d.id }}
                      search={{ tab: 'overview' }}
                      className="text-primary hover:underline"
                    >
                      {[d.number, pickText(i18n.language, d.titleAr, d.titleEn)].join(' · ')}
                    </Link>
                  ) : (
                    <span className="flex items-center gap-1 text-muted-foreground">
                      <Lock className="size-3" aria-hidden />
                      {t('detail.restrictedTask')}
                    </span>
                  )}
                  <span className="flex items-center gap-1">
                    <TaskStatusChip template={d.template} status={d.status} />
                    {task.capabilities.canManageDependencies && (
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={t('detail.remove')}
                        onClick={() => dependencies.remove.mutate(d.id)}
                      >
                        <Trash2 aria-hidden />
                      </Button>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {task.capabilities.canManageDependencies && (
            <div className="flex flex-col gap-2">
              <Input
                value={predecessorSearch}
                placeholder={t('detail.dependencyPlaceholder')}
                aria-label={t('detail.addDependency')}
                onChange={(e) => setPredecessorSearch(e.target.value)}
              />
              <ul className="flex flex-col gap-1">
                {candidates.data?.items
                  .filter((c) => c.id !== task.id && !task.dependencies.some((d) => d.id === c.id))
                  .map((c) => (
                    <li key={c.id}>
                      <button
                        type="button"
                        className="w-full rounded px-2 py-1 text-start text-sm hover:bg-muted"
                        onClick={() =>
                          dependencies.add.mutate(c.id, { onSuccess: () => setPredecessorSearch('') })
                        }
                      >
                        {[c.number, pickText(i18n.language, c.titleAr, c.titleEn)].join(' · ')}
                      </button>
                    </li>
                  ))}
              </ul>
            </div>
          )}
        </Section>
        <Section title={t('fields.status')}>
          <dl className="grid gap-2 text-sm">
            {(
              [
                ['type', task.typeCode],
                ['source', task.sourceCode],
                ['plannedStart', task.plannedStart ? formatDate(task.plannedStart, i18n.language) : null],
                ['assignedAt', task.assignedAt ? formatDateTime(task.assignedAt, i18n.language) : null],
                ['acceptedAt', task.acceptedAt ? formatDateTime(task.acceptedAt, i18n.language) : null],
                ['completedAt', task.completedAt ? formatDateTime(task.completedAt, i18n.language) : null],
                ['approvedAt', task.approvedAt ? formatDateTime(task.approvedAt, i18n.language) : null],
                [
                  'timeToAccept',
                  task.timeToAcceptSeconds !== null ? formatDuration(task.timeToAcceptSeconds) : null,
                ],
                [
                  'timeToComplete',
                  task.timeToCompleteSeconds !== null ? formatDuration(task.timeToCompleteSeconds) : null,
                ],
                ['rejectionReason', task.rejectionReason],
                ['cancelReason', task.cancelReason],
              ] as const
            )
              .filter(([, value]) => value)
              .map(([key, value]) => (
                <div key={key} className="flex justify-between gap-2">
                  <dt className="text-muted-foreground">{t(`fields.${key}`)}</dt>
                  <dd className="text-end">{value}</dd>
                </div>
              ))}
            {task.recurrence && (
              <div className="flex justify-between gap-2">
                <dt className="text-muted-foreground">{t('fields.recurrence')}</dt>
                <dd className="text-end">
                  {t('recurrence.summary', {
                    frequency: t(`recurrence.${task.recurrence.frequency}` as 'recurrence.daily'),
                    interval: task.recurrence.interval,
                    next: task.recurrence.nextOccurrenceOn
                      ? formatDate(task.recurrence.nextOccurrenceOn, i18n.language)
                      : '—',
                  })}
                </dd>
              </div>
            )}
          </dl>
        </Section>
      </div>
      {subtask && <SubtaskDialog parentId={task.id} onClose={() => setSubtask(false)} />}
    </div>
  );
}

function SubtaskDialog({ parentId, onClose }: { parentId: string; onClose: () => void }) {
  const { t } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const create = useCreateSubtask();
  const [title, setTitle] = useState('');
  const [assignee, setAssignee] = useState<string>();
  const [due, setDue] = useState<string>();
  const [error, setError] = useState<string>();
  const { i18n } = useTranslation();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('actions.create-subtask')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!title.trim() || !assignee}
              loading={create.isPending}
              onClick={() =>
                create.mutate(
                  {
                    parentId,
                    titleAr: i18n.language === 'ar' ? title.trim() : null,
                    titleEn: i18n.language === 'ar' ? null : title.trim(),
                    description: null,
                    assigneeEmployeeId: assignee ?? null,
                    priority: null,
                    plannedStart: null,
                    dueDate: due ?? null,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.created') });
                      onClose();
                    },
                    onError: (failure) => setError(errorMessage(tc, failure)),
                  },
                )
              }
            >
              {tc('actions.create')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {error && <StatusBanner tone="danger" title={error} />}
          <FormField label={t('fields.title')} required>
            <Input value={title} maxLength={300} onChange={(e) => setTitle(e.target.value)} />
          </FormField>
          <FormField label={t('fields.assignee')} required>
            <EmployeePicker value={assignee} onChange={setAssignee} />
          </FormField>
          <FormField label={t('fields.dueDate')}>
            <DatePicker value={due} onChange={setDue} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function TimelineTab({ taskId, template }: { taskId: string; template: string }) {
  const { t, i18n } = useTranslation('tasks');
  const timeline = useTaskTimeline(taskId);
  const statusLabel = useStatusLabel();
  if (!timeline.data) return <Skeleton className="h-48" />;
  const { events, timesheet } = timeline.data;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Section title={t('detail.tabs.timeline')}>
        <ol className="relative flex flex-col gap-4 border-s ps-6" data-testid="task-timeline">
          {events.map((event) => (
            <li key={event.id} className="relative">
              <span
                className="absolute -start-[1.95rem] top-1 size-3 rounded-full border-2 border-card bg-primary"
                aria-hidden
              />
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">
                  {t(`timeline.${event.event}` as 'timeline.created', { defaultValue: event.event })}
                </span>
                {event.toStatus && event.toStatus !== event.fromStatus && (
                  <StatusChip tone="neutral">{statusLabel(template, event.toStatus)}</StatusChip>
                )}
                {event.secondsSincePrevious !== null && (
                  <span className="text-xs text-muted-foreground">
                    {t('detail.since', { duration: formatDuration(event.secondsSincePrevious) })}
                  </span>
                )}
              </div>
              <div className="text-xs text-muted-foreground">
                {formatDateTime(event.occurredAt, i18n.language)} ·{' '}
                {event.actor
                  ? pickText(i18n.language, event.actor.nameAr, event.actor.nameEn)
                  : t('timeline.system')}
              </div>
              {event.comment && <p className="mt-1 rounded bg-muted px-2 py-1 text-sm">{event.comment}</p>}
            </li>
          ))}
        </ol>
      </Section>
      <Section title={t('detail.timesheet')}>
        {timesheet.periods.length === 0 ? (
          <p className="text-sm text-muted-foreground">—</p>
        ) : (
          <table className="w-full text-sm" data-testid="timesheet">
            <thead>
              <tr className="text-muted-foreground">
                <th className="py-1 text-start font-normal">{t('detail.period')}</th>
                <th className="py-1 text-start font-normal">{t('detail.start')}</th>
                <th className="py-1 text-start font-normal">{t('detail.end')}</th>
                <th className="py-1 text-end font-normal">{t('detail.duration')}</th>
              </tr>
            </thead>
            <tbody>
              {timesheet.periods.map((period) => (
                <tr key={period.id} className="border-t align-top">
                  <td className="py-2" dir="ltr">
                    {period.number}
                  </td>
                  <td className="py-2">{formatDateTime(period.startedAt, i18n.language)}</td>
                  <td className="py-2">
                    {period.endedAt ? (
                      formatDateTime(period.endedAt, i18n.language)
                    ) : (
                      <StatusChip tone="info">{t('detail.running')}</StatusChip>
                    )}
                    {period.holdReason && (
                      <span className="block text-xs text-muted-foreground">
                        {t('detail.holdReason')}: {period.holdReason}
                      </span>
                    )}
                  </td>
                  <td className="py-2 text-end tabular-nums">{formatDuration(period.durationSeconds)}</td>
                </tr>
              ))}
              <tr className="border-t font-medium">
                <td className="py-2" colSpan={3}>
                  {t('detail.total')}
                </td>
                <td className="py-2 text-end tabular-nums">{formatDuration(timesheet.totalSeconds)}</td>
              </tr>
            </tbody>
          </table>
        )}
      </Section>
    </div>
  );
}

function CommentsTab({ task }: { task: TaskDetail }) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const comments = useTaskComments(task.id);
  const add = useAddComment(task.id);
  const summarise = useSummariseTask(task.id);
  const aiSummary = useAiFeature('task-summary');
  const [text, setText] = useState('');
  const [mentions, setMentions] = useState<string[]>([]);
  const [error, setError] = useState<string>();
  const participants = [
    task.creator,
    task.assignee,
    ...task.followUps.map((f) => ({ employeeId: f.employeeId, nameAr: f.nameAr, nameEn: f.nameEn })),
  ].filter((p, i, all) => all.findIndex((x) => x.employeeId === p.employeeId) === i);
  return (
    <div className="flex flex-col gap-4">
      {aiSummary && (comments.data?.length ?? 0) > 0 && (
        <div className="flex flex-col gap-2">
          <Button
            variant="outline"
            size="sm"
            className="self-start"
            loading={summarise.isPending}
            onClick={() => summarise.mutate('summary')}
          >
            <Sparkles aria-hidden />
            {t('detail.summarise')}
          </Button>
          {summarise.data && (
            <div className="rounded-lg border-2 border-dashed border-amber-400/70 p-3 text-sm">
              <StatusChip tone="warning">{t('detail.summary')}</StatusChip>
              <p className="mt-2 whitespace-pre-wrap">{summarise.data.text}</p>
            </div>
          )}
        </div>
      )}
      {comments.data?.length === 0 && <EmptyState title={t('detail.commentsEmpty')} />}
      <ul className="flex flex-col gap-3" data-testid="comments">
        {comments.data?.map((comment) => (
          <li key={comment.id} className="rounded-lg border bg-card p-3">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="font-medium">
                {comment.author ? pickText(i18n.language, comment.author.nameAr, comment.author.nameEn) : '—'}
              </span>
              <span className="text-xs text-muted-foreground">
                {formatDateTime(comment.createdAt, i18n.language)}
              </span>
              {comment.kind === 'group-message' && (
                <StatusChip tone="info">{t('group.messageAll')}</StatusChip>
              )}
            </div>
            <p className="mt-1 whitespace-pre-wrap text-sm">{comment.text}</p>
            {comment.mentions.length > 0 && (
              <p className="mt-1 text-xs text-muted-foreground">
                @ {comment.mentions.map((m) => pickText(i18n.language, m.nameAr, m.nameEn)).join('، ')}
              </p>
            )}
          </li>
        ))}
      </ul>
      {task.capabilities.canComment && (
        <div className="flex flex-col gap-2 rounded-lg border bg-card p-3">
          {error && <StatusBanner tone="danger" title={error} />}
          <Textarea
            rows={3}
            maxLength={4000}
            value={text}
            placeholder={t('detail.commentPlaceholder')}
            aria-label={t('detail.tabs.comments')}
            onChange={(e) => setText(e.target.value)}
          />
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span className="text-muted-foreground">{t('detail.mention')}:</span>
            {participants.map((p) => (
              <button
                key={p.employeeId}
                type="button"
                aria-pressed={mentions.includes(p.employeeId)}
                className={`rounded-full border px-2 py-0.5 text-xs ${mentions.includes(p.employeeId) ? 'bg-primary text-primary-foreground' : 'hover:bg-muted'}`}
                onClick={() =>
                  setMentions((list) =>
                    list.includes(p.employeeId)
                      ? list.filter((x) => x !== p.employeeId)
                      : [...list, p.employeeId],
                  )
                }
              >
                @{pickText(i18n.language, p.nameAr, p.nameEn)}
              </button>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">{t('detail.attach')}</p>
          <Button
            className="self-end"
            disabled={!text.trim()}
            loading={add.isPending}
            onClick={() =>
              add.mutate(
                { text: text.trim(), mentions, fileIds: [] },
                {
                  onSuccess: () => {
                    setText('');
                    setMentions([]);
                    setError(undefined);
                  },
                  onError: (failure) => setError(errorMessage(tc, failure)),
                },
              )
            }
          >
            {t('detail.send')}
          </Button>
        </div>
      )}
    </div>
  );
}

function FollowUpsTab({ task }: { task: TaskDetail }) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const followUps = useFollowUps(task.id);
  const toast = useToast();
  const [employee, setEmployee] = useState<string>();
  const [canUpload, setCanUpload] = useState(true);
  return (
    <div className="flex flex-col gap-4">
      {task.followUps.length === 0 ? (
        <EmptyState title={t('detail.followUpsEmpty')} />
      ) : (
        <ul className="flex flex-col divide-y rounded-lg border bg-card" data-testid="follow-ups">
          {task.followUps.map((f) => (
            <li key={f.employeeId} className="flex items-center justify-between gap-2 p-3 text-sm">
              <span>
                {pickText(i18n.language, f.nameAr, f.nameEn)}{' '}
                <StatusChip tone="neutral">
                  {f.canUpload ? t('detail.canUpload') : t('detail.commentOnly')}
                </StatusChip>
              </span>
              {task.capabilities.canManageFollowUps && (
                <Button variant="ghost" size="sm" onClick={() => followUps.remove.mutate(f.employeeId)}>
                  {t('detail.remove')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {task.capabilities.canManageFollowUps && (
        <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
          <FormField label={t('detail.addFollowUp')}>
            <EmployeePicker value={employee} onChange={setEmployee} />
          </FormField>
          <Checkbox
            label={t('detail.canUpload')}
            checked={canUpload}
            onChange={(e) => setCanUpload(e.target.checked)}
          />
          <Button
            className="self-end"
            disabled={!employee}
            loading={followUps.add.isPending}
            onClick={() =>
              followUps.add.mutate(
                { employeeId: employee ?? '', canUpload },
                {
                  onSuccess: () => {
                    setEmployee(undefined);
                    toast({ title: t('toast.followUpAdded') });
                  },
                  onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                },
              )
            }
          >
            <Plus aria-hidden />
            {t('detail.addFollowUp')}
          </Button>
        </div>
      )}
    </div>
  );
}
