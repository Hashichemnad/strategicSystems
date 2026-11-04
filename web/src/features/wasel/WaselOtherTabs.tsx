import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  FormField,
  Input,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  fileUrl,
  formBody,
  useVendorComments,
  useVendorDeliverables,
  useVendorItems,
  useVendorMeetings,
  useVendorMilestones,
  useVendorProposals,
  useVendorSubmission,
  waselApi,
  waselKeys,
  type VendorProject,
} from './waselApi';
import { submissionTones } from './waselLogic';

const selectClass = 'h-10 w-full rounded-md border border-input bg-card px-3 text-sm';

function useRun() {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  async function run(action: () => Promise<unknown>, message: string): Promise<boolean> {
    setBusy(true);
    try {
      await action();
      toast({ title: message });
      return true;
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
      return false;
    } finally {
      setBusy(false);
    }
  }
  return { busy, run };
}

export function WaselDeliverablesTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const deliverables = useVendorDeliverables(project.id);
  const milestones = useVendorMilestones(project.id);
  const queryClient = useQueryClient();
  const { busy, run } = useRun();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [milestoneId, setMilestoneId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const canUpload = project.permissions.includes('wasel.deliverable.upload');

  async function upload() {
    if (!file) return;
    const ok = await run(
      async () =>
        unwrap(
          await waselApi.POST('/api/wasel/v1/projects/{id}/deliverables', {
            params: { path: { id: project.id } },
            ...formBody<{ file: string; title?: string; description?: string; milestoneId?: string }>({
              file,
              title,
              description,
              milestoneId,
            }),
          }),
        ),
      t('deliverables.uploaded'),
    );
    if (ok) {
      setTitle('');
      setDescription('');
      setFile(null);
      await queryClient.invalidateQueries({ queryKey: waselKeys.deliverables(project.id) });
    }
  }

  return (
    <div className="flex flex-col gap-6 pt-4" data-testid="wasel-deliverables">
      {canUpload && (
        <section className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <h2 className="font-semibold">{t('deliverables.new')}</h2>
          <div className="grid gap-3 md:grid-cols-2">
            <FormField label={t('deliverables.title')} required>
              <Input
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                data-testid="wasel-deliverable-title"
              />
            </FormField>
            <FormField label={t('deliverables.milestone')}>
              <select
                className={selectClass}
                value={milestoneId}
                onChange={(e) => setMilestoneId(e.target.value)}
                data-testid="wasel-deliverable-milestone"
              >
                <option value="">—</option>
                {(milestones.data ?? []).map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.code} · {pickText(i18n.language, m.titleAr, m.titleEn)}
                  </option>
                ))}
              </select>
            </FormField>
          </div>
          <FormField label={t('deliverables.description')}>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
          <input
            type="file"
            aria-label={t('deliverables.file')}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            data-testid="wasel-deliverable-file"
          />
          <div>
            <Button
              onClick={() => void upload()}
              loading={busy}
              disabled={!file || !title}
              data-testid="wasel-deliverable-upload"
            >
              {t('deliverables.upload')}
            </Button>
          </div>
        </section>
      )}
      {deliverables.isPending ? (
        <Skeleton className="h-24" />
      ) : deliverables.error ? (
        <StatusBanner tone="danger" title={errorMessage(tc, deliverables.error)} />
      ) : deliverables.data.length === 0 ? (
        <EmptyState title={t('deliverables.empty')} />
      ) : (
        <ul className="flex flex-col gap-3">
          {deliverables.data.map((d) => (
            <li
              key={d.id}
              className="flex flex-col gap-2 rounded-xl border bg-card p-4 text-sm"
              data-testid="wasel-deliverable"
            >
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold">{d.title}</span>
                <StatusChip
                  tone={d.status === 'accepted' ? 'success' : d.status === 'returned' ? 'warning' : 'info'}
                >
                  {t(`deliverableStatus.${d.status}`, { defaultValue: d.status })}
                </StatusChip>
                <span className="text-xs text-muted-foreground">
                  {formatDate(d.createdAt, i18n.language)}
                </span>
              </div>
              {d.description && <p className="whitespace-pre-line">{d.description}</p>}
              {d.pmComment && (
                <p>
                  <span className="text-muted-foreground">{t('deliverables.pmComment')}: </span>
                  {d.pmComment}
                </p>
              )}
              <ul className="flex flex-wrap gap-3">
                {d.files.map((f) => (
                  <li key={f.id}>
                    <a className="text-primary underline" href={fileUrl(project.id, f.id)} dir="ltr">
                      {f.name}
                    </a>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function WaselItemsTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const items = useVendorItems(project.id);
  const queryClient = useQueryClient();
  const { busy, run } = useRun();
  const [kind, setKind] = useState('issue');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [impact, setImpact] = useState('');
  const [probability, setProbability] = useState('3');
  const [impactLevel, setImpactLevel] = useState('3');
  const canPropose = project.permissions.includes('wasel.item.propose');

  async function propose() {
    const ok = await run(
      async () =>
        unwrap(
          await waselApi.POST('/api/wasel/v1/projects/{id}/items', {
            params: { path: { id: project.id } },
            body: {
              values: {
                kind,
                title,
                description: description || null,
                impact: impact || null,
                probability: kind === 'risk' ? Number(probability) : null,
                impactLevel: kind === 'risk' ? Number(impactLevel) : null,
                milestoneId: null,
              },
            },
          }),
        ),
      t('items.proposed'),
    );
    if (ok) {
      setTitle('');
      setDescription('');
      setImpact('');
      await queryClient.invalidateQueries({ queryKey: waselKeys.items(project.id) });
    }
  }

  const scale = ['1', '2', '3', '4', '5'];
  return (
    <div className="flex flex-col gap-6 pt-4" data-testid="wasel-items">
      {canPropose && (
        <section className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <h2 className="font-semibold">{t('items.new')}</h2>
          <p className="text-xs text-muted-foreground">{t('items.hint')}</p>
          <div className="grid gap-3 md:grid-cols-2">
            <FormField label={t('items.kind')}>
              <select
                className={selectClass}
                value={kind}
                onChange={(e) => setKind(e.target.value)}
                data-testid="wasel-item-kind"
              >
                {['issue', 'challenge', 'risk'].map((k) => (
                  <option key={k} value={k}>
                    {t(`itemKind.${k}`, { defaultValue: k })}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('items.title')} required>
              <Input
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                data-testid="wasel-item-title"
              />
            </FormField>
          </div>
          <FormField label={t('items.description')}>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
          <FormField label={t('items.impact')}>
            <Textarea value={impact} onChange={(e) => setImpact(e.target.value)} />
          </FormField>
          {kind === 'risk' && (
            <div className="grid gap-3 md:grid-cols-2">
              <FormField label={t('items.probability')}>
                <select
                  className={selectClass}
                  value={probability}
                  onChange={(e) => setProbability(e.target.value)}
                >
                  {scale.map((n) => (
                    <option key={n}>{n}</option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('items.impactLevel')}>
                <select
                  className={selectClass}
                  value={impactLevel}
                  onChange={(e) => setImpactLevel(e.target.value)}
                >
                  {scale.map((n) => (
                    <option key={n}>{n}</option>
                  ))}
                </select>
              </FormField>
            </div>
          )}
          <div>
            <Button
              onClick={() => void propose()}
              loading={busy}
              disabled={!title}
              data-testid="wasel-item-propose"
            >
              {t('items.submit')}
            </Button>
          </div>
        </section>
      )}
      {items.isPending ? (
        <Skeleton className="h-24" />
      ) : items.error ? (
        <StatusBanner tone="danger" title={errorMessage(tc, items.error)} />
      ) : items.data.length === 0 ? (
        <EmptyState title={t('items.empty')} />
      ) : (
        <ul className="flex flex-col gap-3">
          {items.data.map((item) => (
            <li
              key={item.id}
              className="flex flex-col gap-1 rounded-xl border bg-card p-4 text-sm"
              data-testid="wasel-proposed-item"
            >
              <div className="flex flex-wrap items-center gap-2">
                <StatusChip>{t(`itemKind.${item.kind}`, { defaultValue: item.kind })}</StatusChip>
                <span className="font-semibold">{item.title}</span>
                <StatusChip tone={submissionTones[item.status] ?? 'neutral'}>
                  {t(`itemStatus.${item.status}`, { defaultValue: item.status })}
                </StatusChip>
                <span className="text-xs text-muted-foreground">
                  {formatDate(item.createdAt, i18n.language)}
                </span>
              </div>
              {item.description && <p className="whitespace-pre-line">{item.description}</p>}
              {item.pmComment && (
                <p>
                  <span className="text-muted-foreground">{t('items.pmComment')}: </span>
                  {item.pmComment}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function WaselMeetingsTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const meetings = useVendorMeetings(project.id);
  const queryClient = useQueryClient();
  const { busy, run } = useRun();
  const canConfirm = project.permissions.includes('wasel.meeting.confirm');

  async function confirm(id: string) {
    const ok = await run(
      async () =>
        unwrap(
          await waselApi.POST('/api/wasel/v1/projects/{id}/meetings/{mid}/confirm', {
            params: { path: { id: project.id, mid: id } },
          }),
        ),
      t('meetings.confirmed'),
    );
    if (ok) await queryClient.invalidateQueries({ queryKey: waselKeys.meetings(project.id) });
  }

  if (meetings.isPending) return <Skeleton className="mt-4 h-24" />;
  if (meetings.error) return <StatusBanner tone="danger" title={errorMessage(tc, meetings.error)} />;
  if (meetings.data.length === 0) return <EmptyState className="mt-4" title={t('meetings.empty')} />;
  return (
    <ul className="flex flex-col gap-3 pt-4" data-testid="wasel-meetings">
      {meetings.data.map((m) => (
        <li
          key={m.id}
          className="flex flex-col gap-2 rounded-xl border bg-card p-4 text-sm"
          data-testid="wasel-meeting"
        >
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-semibold">{m.title ?? t('meetings.untitled')}</span>
            <span className="text-xs text-muted-foreground">
              {formatDateTime(m.meetingAt, i18n.language)}
            </span>
            {m.confirmedAt ? (
              <StatusChip tone="success">
                {t('meetings.confirmedOn', { date: formatDate(m.confirmedAt, i18n.language) })}
              </StatusChip>
            ) : (
              canConfirm && (
                <Button
                  size="sm"
                  className="ms-auto"
                  onClick={() => void confirm(m.id)}
                  disabled={busy}
                  data-testid="wasel-meeting-confirm"
                >
                  {t('meetings.confirm')}
                </Button>
              )
            )}
          </div>
          {m.notes && <p className="whitespace-pre-line">{m.notes}</p>}
        </li>
      ))}
    </ul>
  );
}

export function WaselCommentsTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const milestones = useVendorMilestones(project.id);
  const proposals = useVendorProposals(project.id);
  const submission = useVendorSubmission(project.id);
  const deliverables = useVendorDeliverables(project.id);
  const [subjectType, setSubjectType] = useState('milestone');
  const [subjectId, setSubjectId] = useState('');
  const [body, setBody] = useState('');
  const comments = useVendorComments(project.id, subjectType, subjectId);
  const queryClient = useQueryClient();
  const { busy, run } = useRun();
  const canWrite = project.permissions.includes('wasel.comment.write');

  const subjects: { id: string; label: string }[] =
    subjectType === 'milestone'
      ? (milestones.data ?? []).map((m) => ({
          id: m.id,
          label: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}`,
        }))
      : subjectType === 'proposal'
        ? (proposals.data?.proposals ?? []).map((p) => ({
            id: p.id,
            label: t('plan.current', { sequence: p.sequence }),
          }))
        : subjectType === 'submission'
          ? submission.data?.id
            ? [{ id: submission.data.id, label: submission.data.period ?? '' }]
            : []
          : (deliverables.data ?? []).map((d) => ({ id: d.id, label: d.title }));

  async function add() {
    const ok = await run(
      async () =>
        unwrap(
          await waselApi.POST('/api/wasel/v1/projects/{id}/comments', {
            params: { path: { id: project.id } },
            body: { subjectType, subjectId, body },
          }),
        ),
      t('comments.added'),
    );
    if (ok) {
      setBody('');
      await queryClient.invalidateQueries({
        queryKey: waselKeys.comments(project.id, subjectType, subjectId),
      });
    }
  }

  return (
    <div className="flex flex-col gap-4 pt-4" data-testid="wasel-comments">
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('comments.subjectType')}>
          <select
            className={selectClass}
            value={subjectType}
            onChange={(e) => {
              setSubjectType(e.target.value);
              setSubjectId('');
            }}
            data-testid="wasel-comment-type"
          >
            {['milestone', 'submission', 'proposal', 'deliverable'].map((s) => (
              <option key={s} value={s}>
                {t(`subject.${s}`, { defaultValue: s })}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('comments.subject')}>
          <select
            className={selectClass}
            value={subjectId}
            onChange={(e) => setSubjectId(e.target.value)}
            data-testid="wasel-comment-subject"
          >
            <option value="">—</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.label}
              </option>
            ))}
          </select>
        </FormField>
      </div>
      {!subjectId ? (
        <EmptyState title={t('comments.pick')} />
      ) : comments.isPending ? (
        <Skeleton className="h-20" />
      ) : comments.error ? (
        <StatusBanner tone="danger" title={errorMessage(tc, comments.error)} />
      ) : (
        <ul className="flex flex-col gap-2" data-testid="wasel-comment-thread">
          {comments.data.length === 0 && (
            <li className="text-sm text-muted-foreground">{t('comments.empty')}</li>
          )}
          {comments.data.map((c) => (
            <li
              key={c.id}
              className={`flex flex-col gap-1 rounded-lg border p-3 text-sm ${c.authorKind === 'vendor' ? 'bg-card' : 'bg-accent'}`}
              data-testid="wasel-comment"
            >
              <span className="text-xs text-muted-foreground">
                {c.authorName ?? t(`author.${c.authorKind}`, { defaultValue: c.authorKind })} ·{' '}
                {formatDateTime(c.createdAt, i18n.language)}
              </span>
              <p className="whitespace-pre-line">{c.body}</p>
            </li>
          ))}
        </ul>
      )}
      {subjectId && canWrite && (
        <div className="flex flex-col gap-2">
          <Textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            aria-label={t('comments.body')}
            data-testid="wasel-comment-body"
          />
          <div>
            <Button
              onClick={() => void add()}
              loading={busy}
              disabled={!body.trim()}
              data-testid="wasel-comment-add"
            >
              {t('comments.add')}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
