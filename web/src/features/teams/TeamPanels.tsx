import { Link } from '@tanstack/react-router';
import { CalendarPlus, ClipboardCheck, ListTodo, Plus, UserMinus, UserPlus, UsersRound } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { NewTaskDialog } from '@/features/tasks/NewTaskDialog';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
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
  RichTextEditor,
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
  useToast,
} from '@/ui';
import {
  teamApi,
  useEvaluations,
  useMeetings,
  useMembers,
  useRecommendations,
  useReports,
  useTeamMutation,
  useTeamOptions,
  type Recommendation,
  type Team,
  type TeamMember,
  type TeamReport,
} from './api';

function usePerson() {
  const { i18n } = useTranslation();
  return (
    p: { nameAr?: string | null; nameEn?: string | null } | null | undefined,
    fallback?: string | null,
  ) => (p ? pickText(i18n.language, p.nameAr, p.nameEn) : (fallback ?? '—'));
}

/** Members with roles; history (left / replaced rows) on demand; the Teams administrator adds, replaces and ends memberships. */
export function MembersPanel({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const person = usePerson();
  const [history, setHistory] = useState(false);
  const members = useMembers(team.id, history);
  const [adding, setAdding] = useState(false);
  const [ending, setEnding] = useState<TeamMember>();
  const canManage =
    team.permissions.canManage &&
    ['active', 'inactive', 'pending_evaluation', 'evaluation_waiting_approval'].includes(team.status);
  return (
    <section className="flex flex-col gap-4" data-testid="team-members">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <Checkbox
          checked={history}
          onChange={(ev) => setHistory(ev.target.checked)}
          label={t('members.history')}
        />
        {canManage && (
          <Button onClick={() => setAdding(true)}>
            <UserPlus aria-hidden />
            {t('members.add')}
          </Button>
        )}
      </header>
      {!members.data ? (
        <Skeleton className="h-32" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('card.member')}</TableHeaderCell>
              <TableHeaderCell>{t('form.role')}</TableHeaderCell>
              <TableHeaderCell>{t('form.responsibilities')}</TableHeaderCell>
              <TableHeaderCell>{t('members.from')}</TableHeaderCell>
              <TableHeaderCell>{t('members.to')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              {canManage && <TableHeaderCell />}
            </TableRow>
          </TableHead>
          <TableBody>
            {members.data.map((m) => (
              <TableRow key={m.id}>
                <TableCell>
                  {person(m.employee, m.externalName)}
                  {!m.employee && (
                    <span className="ms-2 text-xs text-muted-foreground">
                      ({t('form.external')}
                      {m.externalOrganisation ? ` · ${m.externalOrganisation}` : ''})
                    </span>
                  )}
                </TableCell>
                <TableCell>
                  {pickText(i18n.language, m.roleNameAr, m.roleNameEn)}
                  {m.isLeader && (
                    <StatusChip tone="info" className="ms-2">
                      {t('fields.leader')}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell>{m.responsibilities ?? '—'}</TableCell>
                <TableCell>{m.fromDate ? formatDate(m.fromDate, i18n.language) : '—'}</TableCell>
                <TableCell>{m.toDate ? formatDate(m.toDate, i18n.language) : '—'}</TableCell>
                <TableCell>
                  <StatusChip tone={m.status === 'active' ? 'success' : 'neutral'}>
                    {t(`members.statuses.${m.status as 'active'}`)}
                  </StatusChip>
                  {m.leaveReason && <p className="mt-1 text-xs text-muted-foreground">{m.leaveReason}</p>}
                </TableCell>
                {canManage && (
                  <TableCell>
                    {m.status === 'active' && (
                      <Button size="sm" variant="ghost" onClick={() => setEnding(m)}>
                        <UserMinus aria-hidden />
                        {t('members.end')}
                      </Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {adding && <MemberDialog team={team} onClose={() => setAdding(false)} />}
      {ending && <MemberDialog team={team} ending={ending} onClose={() => setEnding(undefined)} />}
    </section>
  );
}

function MemberDialog({ team, ending, onClose }: { team: Team; ending?: TeamMember; onClose: () => void }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const options = useTeamOptions();
  const [employee, setEmployee] = useState<string>();
  const [role, setRole] = useState(ending?.roleCode ?? 'member');
  const [responsibilities, setResponsibilities] = useState('');
  const [reason, setReason] = useState('');
  const [on, setOn] = useState<string>();
  const [replace, setReplace] = useState(false);
  const [error, setError] = useState<string>();
  const member = (id: string | undefined) => ({
    id: null,
    employeeId: id ?? null,
    externalName: null,
    externalOrganisation: null,
    externalEmail: null,
    externalPhone: null,
    roleCode: role,
    responsibilities: responsibilities || null,
    requiredSkills: null,
    allocationPercent: null,
    fromDate: on ?? null,
  });
  const save = useTeamMutation(async () =>
    ending
      ? teamApi.endMember({
          id: team.id,
          memberId: ending.id,
          on: on ?? null,
          reason,
          successor: replace && employee ? member(employee) : null,
          version: ending.version,
        })
      : teamApi.addMember({ id: team.id, member: member(employee) }),
  );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={ending ? t('members.end') : t('members.add')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={ending ? !reason.trim() || (replace && !employee) : !employee}
              onClick={() =>
                save.mutate(undefined, { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) })
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        {ending && (
          <>
            <FormField label={t('members.leaveReason')} required>
              <Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
            <FormField label={t('members.to')}>
              <DatePicker value={on} onChange={setOn} />
            </FormField>
            <Checkbox
              checked={replace}
              onChange={(ev) => setReplace(ev.target.checked)}
              label={t('members.replace')}
            />
          </>
        )}
        {(!ending || replace) && (
          <>
            <FormField label={ending ? t('members.successor') : t('card.member')}>
              <EmployeePicker value={employee} onChange={setEmployee} />
            </FormField>
            <FormField label={t('form.role')}>
              <Select
                value={role}
                onValueChange={setRole}
                options={(options.data?.roles ?? []).map((r) => ({
                  value: r.code,
                  label: pickText(i18n.language, r.nameAr, r.nameEn),
                }))}
              />
            </FormField>
            <FormField label={t('form.responsibilities')}>
              <Input value={responsibilities} onChange={(e) => setResponsibilities(e.target.value)} />
            </FormField>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** Meetings with their minutes status and delay; the leader schedules. */
export function MeetingsPanel({ team, onOpen }: { team: Team; onOpen: (id: string) => void }) {
  const { t, i18n } = useTranslation('teams');
  const meetings = useMeetings(team.id);
  const [creating, setCreating] = useState(false);
  return (
    <section className="flex flex-col gap-4" data-testid="team-meetings">
      {team.permissions.canOperate && (
        <div>
          <Button onClick={() => setCreating(true)}>
            <CalendarPlus aria-hidden />
            {t('meetings.new')}
          </Button>
        </div>
      )}
      {!meetings.data ? (
        <Skeleton className="h-32" />
      ) : meetings.data.length === 0 ? (
        <EmptyState icon={CalendarPlus} title={t('meetings.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
              <TableHeaderCell>{t('meetings.title')}</TableHeaderCell>
              <TableHeaderCell>{t('meetings.scheduledAt')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('meetings.delay')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {meetings.data.map((m) => (
              <TableRow key={m.id} className="cursor-pointer" onClick={() => onOpen(m.id)}>
                <TableCell>
                  <Link
                    to="/teams/meetings/$meetingId"
                    params={{ meetingId: m.id }}
                    className="text-primary hover:underline"
                  >
                    {m.number}
                  </Link>
                </TableCell>
                <TableCell>{m.title}</TableCell>
                <TableCell>{formatDateTime(m.scheduledAt, i18n.language)}</TableCell>
                <TableCell>
                  <StatusChip
                    tone={m.status === 'held' ? 'success' : m.status === 'cancelled' ? 'danger' : 'info'}
                  >
                    {t(`meetings.statuses.${m.status as 'held'}`)}
                  </StatusChip>
                </TableCell>
                <TableCell className="tabular-nums">
                  {m.minutesDelayHours === null
                    ? '—'
                    : t('meetings.delayHours', { hours: formatNumber(m.minutesDelayHours, i18n.language) })}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {creating && <MeetingDialog team={team} onClose={() => setCreating(false)} onCreated={onOpen} />}
    </section>
  );
}

function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function MeetingDialog({
  team,
  onClose,
  onCreated,
}: {
  team: Team;
  onClose: () => void;
  onCreated: (id: string) => void;
}) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const now = new Date();
  const [title, setTitle] = useState('');
  const [start, setStart] = useState(toLocalInput(new Date(now.getTime() - 2 * 3600_000)));
  const [end, setEnd] = useState(toLocalInput(new Date(now.getTime() - 3600_000)));
  const [location, setLocation] = useState('');
  const [agenda, setAgenda] = useState('');
  const [error, setError] = useState<string>();
  const create = useTeamMutation(teamApi.createMeeting);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('meetings.new')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!title.trim()}
              loading={create.isPending}
              onClick={() =>
                create.mutate(
                  {
                    teamId: team.id,
                    title,
                    scheduledAt: new Date(start).toISOString(),
                    endsAt: new Date(end).toISOString(),
                    location: location || null,
                    agenda: agenda || null,
                  },
                  {
                    onSuccess: (m) => {
                      onClose();
                      onCreated(m.id);
                    },
                    onError: (f) => setError(errorMessage(tc, f)),
                  },
                )
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('meetings.title')} required>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} />
        </FormField>
        <div className="grid gap-3 sm:grid-cols-2">
          <FormField label={t('meetings.scheduledAt')}>
            <Input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} />
          </FormField>
          <FormField label={t('meetings.endsAt')}>
            <Input type="datetime-local" value={end} onChange={(e) => setEnd(e.target.value)} />
          </FormField>
        </div>
        <FormField label={t('meetings.location')}>
          <Input value={location} onChange={(e) => setLocation(e.target.value)} />
        </FormField>
        <FormField label={t('meetings.agenda')}>
          <RichTextEditor
            aria-label={t('meetings.agenda')}
            value={agenda}
            onChange={(v) => setAgenda(v ?? '')}
            minHeight="5rem"
          />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

/** The recommendations / decisions register with its closure %; owners update their own; "Create task" links the task to the entry. */
export function RecommendationsPanel({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const person = usePerson();
  const items = useRecommendations(team.id);
  const [creating, setCreating] = useState(false);
  const [updating, setUpdating] = useState<Recommendation>();
  const [tasking, setTasking] = useState<Recommendation>();
  const counted = (items.data ?? []).filter((r) => r.kind === 'recommendation' && r.status !== 'rejected');
  const closed = counted.filter((r) => r.status === 'closed').length;
  return (
    <section className="flex flex-col gap-4" data-testid="team-recommendations">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          {t('recommendations.closure', {
            closed,
            total: counted.length,
            percent: counted.length
              ? `${formatNumber((100 * closed) / counted.length, i18n.language)} %`
              : '—',
          })}
        </p>
        {team.permissions.canOperate && (
          <Button onClick={() => setCreating(true)}>
            <Plus aria-hidden />
            {t('recommendations.new')}
          </Button>
        )}
      </header>
      {!items.data ? (
        <Skeleton className="h-32" />
      ) : items.data.length === 0 ? (
        <EmptyState icon={ClipboardCheck} title={t('recommendations.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
              <TableHeaderCell>{t('recommendations.text')}</TableHeaderCell>
              <TableHeaderCell>{t('recommendations.owner')}</TableHeaderCell>
              <TableHeaderCell>{t('recommendations.dueDate')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {items.data.map((r) => (
              <TableRow key={r.id}>
                <TableCell className="whitespace-nowrap">{r.number}</TableCell>
                <TableCell>
                  <span className="text-xs text-muted-foreground">
                    {t(`recommendations.kinds.${r.kind as 'decision'}`)}
                  </span>
                  <p dir="auto">{r.text}</p>
                </TableCell>
                <TableCell>{person(r.owner)}</TableCell>
                <TableCell>
                  {r.dueDate ? formatDate(r.dueDate, i18n.language) : '—'}
                  {r.overdue && (
                    <StatusChip tone="danger" className="ms-2">
                      {t('recommendations.overdue')}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell>
                  <StatusChip
                    tone={r.status === 'closed' ? 'success' : r.status === 'rejected' ? 'danger' : 'info'}
                  >
                    {t(`recommendations.statuses.${r.status as 'open'}`)}
                  </StatusChip>
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  {r.canUpdateStatus && (
                    <Button size="sm" variant="ghost" onClick={() => setUpdating(r)}>
                      {t('recommendations.setStatus')}
                    </Button>
                  )}
                  {r.canEdit && (
                    <Button size="sm" variant="ghost" onClick={() => setTasking(r)}>
                      <ListTodo aria-hidden />
                      {t('actions.createTask')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {creating && <RecommendationDialog team={team} onClose={() => setCreating(false)} />}
      {updating && <RecommendationStatusDialog item={updating} onClose={() => setUpdating(undefined)} />}
      <NewTaskDialog
        open={!!tasking}
        onOpenChange={(open) => !open && setTasking(undefined)}
        link={tasking ? { type: 'teams.recommendation', id: tasking.id } : undefined}
        initial={
          tasking
            ? {
                title: tasking.text.slice(0, 280),
                assigneeEmployeeId: tasking.owner?.id ?? null,
                dueDate: tasking.dueDate,
              }
            : undefined
        }
      />
    </section>
  );
}

export function RecommendationDialog({
  team,
  meetingId,
  initial,
  onClose,
}: {
  team: Team | { id: string };
  meetingId?: string;
  initial?: { text: string; owner?: string | undefined; dueDate?: string | null | undefined };
  onClose: () => void;
}) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [text, setText] = useState(initial?.text ?? '');
  const [kind, setKind] = useState('recommendation');
  const [owner, setOwner] = useState<string | undefined>(initial?.owner);
  const [due, setDue] = useState<string | undefined>(initial?.dueDate ?? undefined);
  const [error, setError] = useState<string>();
  const create = useTeamMutation(teamApi.createRecommendation);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('recommendations.new')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!text.trim()}
              loading={create.isPending}
              onClick={() =>
                create.mutate(
                  {
                    teamId: team.id,
                    meetingId: meetingId ?? null,
                    kind,
                    text,
                    ownerEmployeeId: owner ?? null,
                    dueDate: due ?? null,
                  },
                  { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('recommendations.kind')}>
          <Select
            value={kind}
            onValueChange={setKind}
            options={(['recommendation', 'decision'] as const).map((k) => ({
              value: k,
              label: t(`recommendations.kinds.${k}`),
            }))}
          />
        </FormField>
        <FormField label={t('recommendations.text')} required>
          <Textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} />
        </FormField>
        <FormField label={t('recommendations.owner')}>
          <EmployeePicker value={owner} onChange={setOwner} />
        </FormField>
        <FormField label={t('recommendations.dueDate')}>
          <DatePicker value={due} onChange={setDue} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

function RecommendationStatusDialog({ item, onClose }: { item: Recommendation; onClose: () => void }) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [status, setStatus] = useState(item.status === 'open' ? 'closed' : item.status);
  const [note, setNote] = useState('');
  const [error, setError] = useState<string>();
  const save = useTeamMutation(teamApi.setRecommendationStatus);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('recommendations.setStatus')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: item.id, status, note: note || null, version: item.version },
                  { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('fields.status')}>
          <Select
            value={status}
            onValueChange={setStatus}
            options={(['open', 'in_progress', 'closed', 'rejected'] as const).map((s) => ({
              value: s,
              label: t(`recommendations.statuses.${s}`),
            }))}
          />
        </FormField>
        <FormField label={t('recommendations.note')} required={status === 'rejected'}>
          <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

/** Periodic reports per reporting cadence; the leader submits the due one. */
export function ReportsPanel({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const reports = useReports(team.id);
  const [submitting, setSubmitting] = useState<TeamReport>();
  return (
    <section className="flex flex-col gap-4" data-testid="team-reports">
      {!reports.data ? (
        <Skeleton className="h-24" />
      ) : reports.data.length === 0 ? (
        <EmptyState icon={ClipboardCheck} title={t('reports.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('reports.period')}</TableHeaderCell>
              <TableHeaderCell>{t('reports.due')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('reports.summary')}</TableHeaderCell>
              <TableHeaderCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {reports.data.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  {formatDate(r.periodStart, i18n.language)} – {formatDate(r.periodEnd, i18n.language)}
                </TableCell>
                <TableCell>{formatDate(r.dueDate, i18n.language)}</TableCell>
                <TableCell>
                  <StatusChip tone={r.status === 'submitted' ? 'success' : r.overdue ? 'danger' : 'warning'}>
                    {t(`reports.statuses.${r.status as 'due'}`)}
                  </StatusChip>
                </TableCell>
                <TableCell className="max-w-md truncate">{r.summary ?? '—'}</TableCell>
                <TableCell>
                  {r.canSubmit && (
                    <Button size="sm" onClick={() => setSubmitting(r)}>
                      {t('reports.submit')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {submitting && <ReportDialog report={submitting} onClose={() => setSubmitting(undefined)} />}
    </section>
  );
}

function ReportDialog({ report, onClose }: { report: TeamReport; onClose: () => void }) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [summary, setSummary] = useState('');
  const [error, setError] = useState<string>();
  const save = useTeamMutation(teamApi.submitReport);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('reports.submit')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!summary.trim()}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: report.id, summary, version: report.version },
                  { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('reports.summary')} required>
          <Textarea rows={5} value={summary} onChange={(e) => setSummary(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

/** Evaluation cycles of the team (history kept per cycle); scores only for results viewers (ADR 0042). */
export function EvaluationsPanel({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const toast = useToast();
  const person = usePerson();
  const cycles = useEvaluations(team.id);
  const canOpen = team.actions.includes('open-evaluation');
  const open = useTeamMutation(teamApi.openEvaluation);
  return (
    <section className="flex flex-col gap-4" data-testid="team-evaluations">
      {!team.permissions.canViewResults && <StatusBanner tone="info" title={t('evaluations.hidden')} />}
      {canOpen && (
        <div>
          <Button
            loading={open.isPending}
            onClick={() =>
              open.mutate(
                { teamId: team.id, year: team.permanent ? new Date().getFullYear() : null },
                { onError: () => toast({ tone: 'error', title: t('errors.cannotOpenEvaluation') }) },
              )
            }
          >
            <UsersRound aria-hidden />
            {t('evaluations.open')}
          </Button>
        </div>
      )}
      {!cycles.data ? (
        <Skeleton className="h-24" />
      ) : cycles.data.length === 0 ? (
        <EmptyState icon={UsersRound} title={t('evaluations.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('evaluations.cycle')}</TableHeaderCell>
              <TableHeaderCell>{t('evaluations.period')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('evaluations.evaluator')}</TableHeaderCell>
              {team.permissions.canViewResults && <TableHeaderCell>{t('evaluations.score')}</TableHeaderCell>}
              {team.permissions.canViewResults && <TableHeaderCell>{t('rewards.title')}</TableHeaderCell>}
            </TableRow>
          </TableHead>
          <TableBody>
            {cycles.data.map((e) => (
              <TableRow key={e.id}>
                <TableCell>
                  <Link
                    to="/teams/evaluations/$evaluationId"
                    params={{ evaluationId: e.id }}
                    className="text-primary hover:underline"
                  >
                    {e.cycle === 'completion' ? t('evaluations.completion') : e.cycle}
                  </Link>
                  <p className="text-xs text-muted-foreground">
                    {t(`evaluations.triggers.${e.trigger as 'manual'}`)}
                  </p>
                </TableCell>
                <TableCell>
                  {formatDate(e.periodStart, i18n.language)} – {formatDate(e.periodEnd, i18n.language)}
                </TableCell>
                <TableCell>
                  <StatusChip
                    tone={e.status === 'approved' ? 'success' : e.status === 'cancelled' ? 'neutral' : 'info'}
                  >
                    {t(`evaluations.statuses.${e.status as 'open'}`)}
                  </StatusChip>
                </TableCell>
                <TableCell>{person(e.evaluator)}</TableCell>
                {team.permissions.canViewResults && (
                  <TableCell className="tabular-nums">
                    {e.score === null ? '—' : formatNumber(e.score, i18n.language)}
                  </TableCell>
                )}
                {team.permissions.canViewResults && (
                  <TableCell>
                    {e.rewardStatus ? t(`rewards.statuses.${e.rewardStatus as 'draft'}`) : '—'}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
