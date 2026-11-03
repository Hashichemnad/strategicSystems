import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Sparkles } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  PageHeader,
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
import { teamApi, useMeeting, useTeamMutation, type MinutesDraft } from './api';
import { RecommendationDialog } from './TeamPanels';

const route = getRouteApi('/app/teams/meetings/$meetingId');

/** A meeting (SPEC §4.4 C): attendance, minutes and decisions, the AI minutes writer (a draft the secretary accepts), submit minutes. */
export function MeetingPage() {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const { meetingId } = route.useParams();
  const meeting = useMeeting(meetingId);
  const aiMinutes = useAiFeature('teams-minutes');
  const [minutes, setMinutes] = useState('');
  const [decisions, setDecisions] = useState('');
  const [attendance, setAttendance] = useState<Record<string, string>>({});
  const [aiDrafted, setAiDrafted] = useState(false);
  const [writing, setWriting] = useState(false);
  const [draft, setDraft] = useState<MinutesDraft>();
  const [adding, setAdding] = useState<{ text: string; owner?: string; dueDate?: string | null }>();
  const [error, setError] = useState<string>();
  const save = useTeamMutation(teamApi.saveMinutes);
  const submit = useTeamMutation(teamApi.submitMinutes);

  useEffect(() => {
    if (!meeting.data) return;
    setMinutes(meeting.data.minutes ?? '');
    setDecisions(meeting.data.decisions ?? '');
    setAttendance(
      Object.fromEntries(meeting.data.attendance.map((a) => [a.memberId, a.status ?? 'present'])),
    );
  }, [meeting.data]);

  if (meeting.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (meeting.isError) return <StatusBanner tone="danger" title={errorMessage(tc, meeting.error)} />;
  const m = meeting.data;
  const locked = m.status !== 'scheduled' || !!m.minutesSubmittedAt;
  const editable = m.canMinute && !locked;
  const saveNow = (then?: () => void) =>
    save.mutate(
      {
        id: m.id,
        minutes: minutes || null,
        decisions: decisions || null,
        attendance: m.attendance.map((a) => ({
          memberId: a.memberId,
          status: attendance[a.memberId] ?? 'present',
          note: null,
        })),
        aiDrafted,
        version: m.version,
      },
      {
        onSuccess: (saved) => {
          toast({ title: t('meetings.saveMinutes') });
          if (then)
            submit.mutate(
              { id: saved.id, version: saved.version },
              { onSuccess: then, onError: (f) => setError(errorMessage(tc, f)) },
            );
        },
        onError: (f) => setError(errorMessage(tc, f)),
      },
    );

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/teams/$teamId"
        params={{ teamId: m.teamId }}
        search={{ tab: 'meetings' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('meetings.back')}
      </Link>
      <PageHeader
        title={m.title}
        description={`${m.number ?? ''} · ${formatDateTime(m.scheduledAt, i18n.language)} – ${formatDateTime(m.endsAt, i18n.language)}${m.location ? ` · ${m.location}` : ''}`}
        meta={
          <StatusChip tone={m.status === 'held' ? 'success' : m.status === 'cancelled' ? 'danger' : 'info'}>
            {t(`meetings.statuses.${m.status as 'held'}`)}
          </StatusChip>
        }
        actions={
          editable &&
          aiMinutes && (
            <Button variant="outline" onClick={() => setWriting(true)}>
              <Sparkles aria-hidden />
              {t('meetings.writeWithAi')}
            </Button>
          )
        }
      />
      {error && <StatusBanner tone="danger" title={error} />}
      {m.minutesSubmittedAt && (
        <StatusBanner
          tone="success"
          title={t('meetings.minutesSubmitted', {
            date: formatDateTime(m.minutesSubmittedAt, i18n.language),
            hours: formatNumber(m.minutesDelayHours ?? 0, i18n.language),
          })}
        />
      )}
      {m.agenda && (
        <section className="rounded-lg border bg-card p-4">
          <h2 className="mb-1 text-sm text-muted-foreground">{t('meetings.agenda')}</h2>
          <p className="whitespace-pre-line" dir="auto">
            {m.agenda}
          </p>
        </section>
      )}
      <div className="grid gap-4 lg:grid-cols-[1fr_2fr]">
        <section className="rounded-lg border bg-card p-4" data-testid="meeting-attendance">
          <h2 className="mb-3 font-semibold">{t('meetings.attendance')}</h2>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('card.member')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {m.attendance.map((a) => (
                <TableRow key={a.memberId}>
                  <TableCell>
                    {a.employee
                      ? pickText(i18n.language, a.employee.nameAr, a.employee.nameEn)
                      : a.externalName}
                  </TableCell>
                  <TableCell>
                    {editable ? (
                      <Select
                        aria-label={t('meetings.attendance')}
                        value={attendance[a.memberId] ?? 'present'}
                        onValueChange={(v) => setAttendance((x) => ({ ...x, [a.memberId]: v }))}
                        options={(['present', 'absent', 'excused'] as const).map((s) => ({
                          value: s,
                          label: t(`meetings.attendanceStatus.${s}`),
                        }))}
                      />
                    ) : (
                      t(`meetings.attendanceStatus.${(a.status ?? 'present') as 'present'}`)
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </section>
        <section className="flex flex-col gap-4 rounded-lg border bg-card p-4" data-testid="meeting-minutes">
          {aiDrafted && <StatusChip tone="info">{t('meetings.aiDraftLabel')}</StatusChip>}
          <FormField label={t('meetings.minutes')}>
            <Textarea
              rows={8}
              value={minutes}
              disabled={!editable}
              onChange={(e) => setMinutes(e.target.value)}
              dir="auto"
            />
          </FormField>
          <FormField label={t('meetings.decisions')}>
            <Textarea
              rows={4}
              value={decisions}
              disabled={!editable}
              onChange={(e) => setDecisions(e.target.value)}
              dir="auto"
            />
          </FormField>
          {draft && draft.recommendations.length > 0 && (
            <ul className="flex flex-col gap-2" data-testid="minutes-recommendations">
              {draft.recommendations.map((r, i) => (
                <li
                  key={i}
                  className="flex items-center justify-between gap-2 rounded-md border border-dashed p-2 text-sm"
                >
                  <span dir="auto">
                    {r.text}
                    {r.owner && (
                      <span className="text-muted-foreground">
                        {' '}
                        · {pickText(i18n.language, r.owner.nameAr, r.owner.nameEn)}
                      </span>
                    )}
                  </span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => setAdding({ text: r.text, owner: r.owner?.id, dueDate: r.dueDate })}
                  >
                    {t('meetings.addRecommendation')}
                  </Button>
                </li>
              ))}
            </ul>
          )}
          {editable && (
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                loading={save.isPending && !submit.isPending}
                onClick={() => saveNow()}
              >
                {t('meetings.saveMinutes')}
              </Button>
              <Button
                disabled={!minutes.trim()}
                loading={submit.isPending}
                onClick={() => saveNow(() => toast({ title: t('meetings.submitMinutes') }))}
              >
                {t('meetings.submitMinutes')}
              </Button>
            </div>
          )}
        </section>
      </div>
      {writing && (
        <MinutesWriterDialog
          meetingId={m.id}
          onClose={() => setWriting(false)}
          onDraft={(d) => {
            setMinutes(d.minutes);
            setDecisions(d.decisions.join('\n'));
            setAiDrafted(true);
            setDraft(d);
            setWriting(false);
          }}
        />
      )}
      {adding && (
        <RecommendationDialog
          team={{ id: m.teamId }}
          meetingId={m.id}
          initial={adding}
          onClose={() => setAdding(undefined)}
        />
      )}
    </div>
  );
}

function MinutesWriterDialog({
  meetingId,
  onClose,
  onDraft,
}: {
  meetingId: string;
  onClose: () => void;
  onDraft: (d: MinutesDraft) => void;
}) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string>();
  const write = useTeamMutation(teamApi.aiMinutes);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('meetings.writeWithAi')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!notes.trim()}
              loading={write.isPending}
              onClick={() =>
                write.mutate(
                  { id: meetingId, notes },
                  { onSuccess: onDraft, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              <Sparkles aria-hidden />
              {t('meetings.useDraft')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('meetings.notes')}>
          <Textarea rows={8} value={notes} onChange={(e) => setNotes(e.target.value)} dir="auto" />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}
