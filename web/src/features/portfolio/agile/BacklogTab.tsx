import { Lock, Pencil, Plus, Sparkles, XCircle } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import { fromPercent, toPercent, useMilestones, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  agileApi,
  priorities,
  useAgile,
  useAgileMutation,
  type AgileView,
  type Epic,
  type Priority,
  type Release,
  type Story,
} from './agileApi';
import { AiPlanDialog } from './AiPlanDialog';
import { isFullWeight, sumRatios } from './boardLogic';

export const priorityTone: Record<string, Tone> = {
  must: 'danger',
  should: 'warning',
  could: 'info',
  wont: 'neutral',
};
const releaseTone: Record<string, Tone> = { planned: 'info', released: 'success', cancelled: 'neutral' };
const epicTone: Record<string, Tone> = { open: 'info', done: 'success', cancelled: 'neutral' };

/** MoSCoW priority as a chip. */
export function PriorityChip({ priority }: { priority: string }) {
  const { t } = useTranslation('agile');
  return (
    <StatusChip tone={priorityTone[priority] ?? 'neutral'}>
      {t(`priority.${priority as Priority}`)}
    </StatusChip>
  );
}

/** Approved progress with the done-but-not-yet-approved part beside it (ratios, pending includes approved). */
export function ProgressBar({
  approved,
  pending,
  label,
}: {
  approved: number;
  pending: number;
  label: string;
}) {
  const a = Math.max(0, Math.min(1, approved));
  const p = Math.max(a, Math.min(1, pending));
  return (
    <div
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(a * 100)}
      className="flex h-2 w-full overflow-hidden rounded-full bg-muted"
    >
      <div className="h-full bg-success" style={{ width: `${a * 100}%` }} />
      <div className="h-full bg-warning/70" style={{ width: `${(p - a) * 100}%` }} />
    </div>
  );
}

/** Backlog (design §29–§31): releases, epics with weights (= 100 %), user stories per epic. */
export function BacklogTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('agile');
  const view = useAgile(project.id);
  const [release, setRelease] = useState<{ row?: Release } | null>(null);
  const [epic, setEpic] = useState<{ row?: Epic } | null>(null);
  const [story, setStory] = useState<{ row?: Story; epicId: string } | null>(null);
  const [cancelling, setCancelling] = useState<
    { kind: 'epic'; row: Epic } | { kind: 'story'; row: Story } | null
  >(null);
  const [ai, setAi] = useState(false);
  const lang = i18n.language;
  if (view.isPending) return <Skeleton className="h-64" />;
  if (view.error) return <FieldErrors error={view.error} />;
  const data = view.data;
  const perms = data.permissions;
  const pct = (ratio: number) => t('common.percent', { value: formatNumber(toPercent(ratio) ?? 0, lang) });
  const hybrid = data.methodology === 'hybrid';
  const activeEpics = data.epics.filter((e) => e.status !== 'cancelled');
  const releaseName = (id: string | null) => {
    const r = data.releases.find((x) => x.id === id);
    return r ? pickText(lang, r.nameAr, r.nameEn) : t('backlog.noRelease');
  };
  const sprintName = (id: string | null) => {
    const s = data.sprints.find((x) => x.id === id);
    return s ? `#${formatNumber(s.number, lang)} ${s.name ?? ''}`.trim() : t('backlog.noSprint');
  };
  const columnLabel = (code: string) => {
    const c = data.board.columns.find((x) => x.code === code);
    return c ? pickText(lang, c.labelAr, c.labelEn) : code;
  };
  // Agile: one total over the active epics. Hybrid: each sprint-delivered milestone's epics total 100 % of it.
  const totals = hybrid
    ? [...new Set(activeEpics.map((e) => e.milestoneId ?? ''))].map((key) => ({
        key,
        total: sumRatios(activeEpics.filter((e) => (e.milestoneId ?? '') === key).map((e) => e.weight)),
      }))
    : [{ key: '', total: sumRatios(activeEpics.map((e) => e.weight)) }];
  const canEditStory = perms.canCreateStory || perms.canPlan;

  return (
    <div className="flex flex-col gap-8" data-testid="agile-backlog">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <StatusChip tone="success">
            {t('backlog.approvedProgress')}: {pct(data.approvedProgress)}
          </StatusChip>
          <StatusChip tone="warning">
            {t('backlog.pendingProgress')}: {pct(data.pendingProgress)}
          </StatusChip>
        </div>
        {perms.canPlan && (
          <Button size="sm" variant="outline" onClick={() => setAi(true)} data-testid="ai-plan-open">
            <Sparkles aria-hidden />
            {t('backlog.aiPlan')}
          </Button>
        )}
      </div>

      <section className="flex flex-col gap-3" data-testid="releases">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="font-semibold">{t('backlog.releases')}</h2>
            <p className="text-sm text-muted-foreground">{t('backlog.releasesHint')}</p>
          </div>
          {perms.canPlan && (
            <Button size="sm" onClick={() => setRelease({})} data-testid="release-add">
              <Plus aria-hidden />
              {t('backlog.addRelease')}
            </Button>
          )}
        </header>
        {data.releases.length === 0 ? (
          <EmptyState title={t('backlog.noReleases')} />
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('common.nameEn')}</TableHeaderCell>
                  <TableHeaderCell>{t('backlog.goal')}</TableHeaderCell>
                  <TableHeaderCell>{t('backlog.targetDate')}</TableHeaderCell>
                  <TableHeaderCell>{t('backlog.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('backlog.progress')}</TableHeaderCell>
                  {perms.canPlan && <TableHeaderCell>{t('common.actions')}</TableHeaderCell>}
                </TableRow>
              </TableHead>
              <TableBody>
                {[...data.releases]
                  .sort((a, b) => a.sort - b.sort)
                  .map((r) => (
                    <TableRow key={r.id} data-testid="release-row">
                      <TableCell dir="auto" className="font-medium">
                        {pickText(lang, r.nameAr, r.nameEn)}
                      </TableCell>
                      <TableCell dir="auto" className="max-w-xs text-sm">
                        {r.goal ?? t('common.none')}
                      </TableCell>
                      <TableCell className="text-sm">
                        {r.targetDate ? formatDate(r.targetDate, lang) : t('common.none')}
                      </TableCell>
                      <TableCell>
                        <StatusChip tone={releaseTone[r.status] ?? 'neutral'}>
                          {t(`status.release.${r.status as 'planned'}`)}
                        </StatusChip>
                      </TableCell>
                      <TableCell className="tabular-nums text-sm">
                        {formatNumber(r.donePoints, lang)} /{' '}
                        {t('common.pointsValue', { value: formatNumber(r.points, lang) })}
                      </TableCell>
                      {perms.canPlan && (
                        <TableCell>
                          {r.status !== 'cancelled' && (
                            <Button
                              size="sm"
                              variant="ghost"
                              aria-label={t('backlog.editRelease')}
                              onClick={() => setRelease({ row: r })}
                            >
                              <Pencil aria-hidden />
                            </Button>
                          )}
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      <section className="flex flex-col gap-3" data-testid="epics">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="font-semibold">{t('backlog.epics')}</h2>
            <p className="text-sm text-muted-foreground">{t('backlog.epicsHint')}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {totals.map((x) => (
              <span key={x.key} data-testid="epic-weight-total">
                <StatusChip tone={isFullWeight(x.total) ? 'success' : 'warning'}>
                  {t('backlog.weightTotal', { value: formatNumber(toPercent(x.total) ?? 0, lang) })}
                </StatusChip>
              </span>
            ))}
            {perms.canPlan && (
              <Button size="sm" onClick={() => setEpic({})} data-testid="epic-add">
                <Plus aria-hidden />
                {t('backlog.addEpic')}
              </Button>
            )}
          </div>
        </header>
        {data.epics.length === 0 ? (
          <EmptyState title={t('backlog.noEpics')} />
        ) : (
          [...data.epics]
            .sort((a, b) => a.sort - b.sort || a.code.localeCompare(b.code, undefined, { numeric: true }))
            .map((e) => {
              const stories = data.stories
                .filter((s) => s.epicId === e.id)
                .sort(
                  (a, b) => a.rank - b.rank || a.code.localeCompare(b.code, undefined, { numeric: true }),
                );
              const name = pickText(lang, e.titleAr, e.titleEn);
              const live = e.status !== 'cancelled';
              return (
                <article
                  key={e.id}
                  className="flex flex-col gap-3 rounded-lg border bg-card p-4"
                  data-testid="epic-card"
                >
                  <header className="flex flex-wrap items-start justify-between gap-2">
                    <div className="flex min-w-0 flex-col gap-1">
                      <h3 className="flex flex-wrap items-center gap-2 font-semibold">
                        <bdi className="text-xs text-muted-foreground">{e.code}</bdi>
                        <span dir="auto">{name}</span>
                        <StatusChip tone={epicTone[e.status] ?? 'neutral'}>
                          {t(`status.epic.${e.status as 'open'}`)}
                        </StatusChip>
                      </h3>
                      <p className="text-xs text-muted-foreground">
                        {t('backlog.weight')}: <span className="tabular-nums">{pct(e.weight)}</span> ·{' '}
                        {t('backlog.release')}: {releaseName(e.releaseId)} ·{' '}
                        {t('common.pointsValue', { value: formatNumber(e.points, lang) })}
                      </p>
                    </div>
                    <div className="flex flex-wrap gap-1">
                      {live && canEditStory && perms.canCreateStory && (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setStory({ epicId: e.id })}
                          data-testid="story-add"
                        >
                          <Plus aria-hidden />
                          {t('backlog.addStory')}
                        </Button>
                      )}
                      {live && perms.canPlan && (
                        <>
                          <Button
                            size="sm"
                            variant="ghost"
                            aria-label={t('backlog.editEpic')}
                            onClick={() => setEpic({ row: e })}
                          >
                            <Pencil aria-hidden />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            aria-label={t('backlog.cancelEpic')}
                            onClick={() => setCancelling({ kind: 'epic', row: e })}
                          >
                            <XCircle aria-hidden />
                          </Button>
                        </>
                      )}
                    </div>
                  </header>
                  <div className="flex flex-col gap-1">
                    <ProgressBar
                      approved={e.approvedFraction}
                      pending={e.pendingFraction}
                      label={t('backlog.epicProgress', { name })}
                    />
                    <p className="flex flex-wrap gap-3 text-xs text-muted-foreground tabular-nums">
                      <span>
                        {t('backlog.approved', {
                          value: formatNumber(toPercent(e.approvedFraction) ?? 0, lang),
                        })}
                      </span>
                      <span>
                        {t('backlog.pending', {
                          value: formatNumber(toPercent(e.pendingFraction) ?? 0, lang),
                        })}
                      </span>
                    </p>
                  </div>
                  {stories.length === 0 ? (
                    <p className="text-sm text-muted-foreground">{t('backlog.noStories')}</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <Table>
                        <TableHead>
                          <TableRow>
                            <TableHeaderCell>{t('common.code')}</TableHeaderCell>
                            <TableHeaderCell>{t('backlog.storyTitle')}</TableHeaderCell>
                            <TableHeaderCell>{t('common.points')}</TableHeaderCell>
                            <TableHeaderCell>{t('backlog.priority')}</TableHeaderCell>
                            <TableHeaderCell>{t('backlog.assignee')}</TableHeaderCell>
                            <TableHeaderCell>{t('backlog.sprint')}</TableHeaderCell>
                            <TableHeaderCell>{t('backlog.column')}</TableHeaderCell>
                            {canEditStory && <TableHeaderCell>{t('common.actions')}</TableHeaderCell>}
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {stories.map((s) => (
                            <TableRow
                              key={s.id}
                              data-testid="story-row"
                              className={s.status === 'cancelled' ? 'opacity-60' : undefined}
                            >
                              <TableCell>
                                <bdi className="text-xs">{s.code}</bdi>
                              </TableCell>
                              <TableCell dir="auto">
                                {s.title}
                                {s.approvedDone && (
                                  <Lock
                                    className="ms-1 inline size-3.5 text-muted-foreground"
                                    aria-label={t('backlog.approvedDone')}
                                  />
                                )}
                              </TableCell>
                              <TableCell className="tabular-nums">
                                {formatNumber(s.storyPoints, lang)}
                              </TableCell>
                              <TableCell>
                                <PriorityChip priority={s.priority} />
                              </TableCell>
                              <TableCell className="text-sm">
                                {s.assignee
                                  ? pickText(lang, s.assignee.nameAr, s.assignee.nameEn)
                                  : t('common.none')}
                              </TableCell>
                              <TableCell className="text-sm">{sprintName(s.sprintId)}</TableCell>
                              <TableCell className="text-sm">
                                {s.status === 'cancelled'
                                  ? t('status.epic.cancelled')
                                  : columnLabel(s.columnCode)}
                              </TableCell>
                              {canEditStory && (
                                <TableCell>
                                  {s.status !== 'cancelled' && !s.approvedDone && (
                                    <div className="flex gap-1">
                                      <Button
                                        size="sm"
                                        variant="ghost"
                                        aria-label={`${t('backlog.editStory')} ${s.code}`}
                                        onClick={() => setStory({ row: s, epicId: s.epicId })}
                                      >
                                        <Pencil aria-hidden />
                                      </Button>
                                      <Button
                                        size="sm"
                                        variant="ghost"
                                        aria-label={`${t('backlog.cancelStory')} ${s.code}`}
                                        onClick={() => setCancelling({ kind: 'story', row: s })}
                                      >
                                        <XCircle aria-hidden />
                                      </Button>
                                    </div>
                                  )}
                                </TableCell>
                              )}
                            </TableRow>
                          ))}
                        </TableBody>
                      </Table>
                    </div>
                  )}
                </article>
              );
            })
        )}
      </section>

      {release && <ReleaseDialog project={project} row={release.row} onClose={() => setRelease(null)} />}
      {epic && <EpicDialog project={project} view={data} row={epic.row} onClose={() => setEpic(null)} />}
      {story && (
        <StoryDialog
          project={project}
          view={data}
          row={story.row}
          epicId={story.epicId}
          onClose={() => setStory(null)}
        />
      )}
      {cancelling && (
        <CancelDialog project={project} target={cancelling} onClose={() => setCancelling(null)} />
      )}
      {ai && <AiPlanDialog project={project} open={ai} onOpenChange={setAi} />}
    </div>
  );
}

function SaveFooter({ loading, onSave }: { loading: boolean; onSave: () => void }) {
  const { t: tc } = useTranslation();
  return (
    <>
      <DialogClose asChild>
        <Button variant="outline">{tc('actions.cancel')}</Button>
      </DialogClose>
      <Button loading={loading} onClick={onSave} data-testid="agile-dialog-save">
        {tc('actions.save')}
      </Button>
    </>
  );
}

function ReleaseDialog({
  project,
  row,
  onClose,
}: {
  project: ProjectFull;
  row?: Release | undefined;
  onClose: () => void;
}) {
  const { t } = useTranslation('agile');
  const toast = useToast();
  const save = useAgileMutation(project.id, agileApi.saveRelease);
  const [v, setV] = useState({
    nameAr: row?.nameAr ?? '',
    nameEn: row?.nameEn ?? '',
    goal: row?.goal ?? '',
    targetDate: row?.targetDate ?? undefined,
    status: row?.status ?? 'planned',
    sort: row ? String(row.sort) : '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const submit = () =>
    save.mutate(
      {
        id: project.id,
        ...(row ? { itemId: row.id, version: row.version } : {}),
        values: {
          nameAr: v.nameAr.trim() || null,
          nameEn: v.nameEn.trim() || null,
          goal: v.goal.trim() || null,
          targetDate: v.targetDate ?? null,
          status: row ? v.status : null,
          sort: Number(v.sort) || 0,
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
        title={row ? t('backlog.editRelease') : t('backlog.addRelease')}
        className="max-w-xl"
        footer={<SaveFooter loading={save.isPending} onSave={submit} />}
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="release-dialog">
          {save.error && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
          <FormField label={t('common.nameAr')}>
            <Input dir="rtl" value={v.nameAr} onChange={(e) => set({ nameAr: e.target.value })} />
          </FormField>
          <FormField label={t('common.nameEn')}>
            <Input dir="ltr" value={v.nameEn} onChange={(e) => set({ nameEn: e.target.value })} />
          </FormField>
          <FormField label={t('backlog.goal')} className="md:col-span-2">
            <Textarea dir="auto" value={v.goal} onChange={(e) => set({ goal: e.target.value })} />
          </FormField>
          <FormField label={t('backlog.targetDate')}>
            <DatePicker value={v.targetDate} onChange={(d) => set({ targetDate: d })} />
          </FormField>
          {row && (
            <FormField label={t('backlog.status')}>
              <Select
                value={v.status}
                onValueChange={(status) => set({ status })}
                options={(['planned', 'released'] as const).map((s) => ({
                  value: s,
                  label: t(`status.release.${s}`),
                }))}
              />
            </FormField>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function EpicDialog({
  project,
  view,
  row,
  onClose,
}: {
  project: ProjectFull;
  view: AgileView;
  row?: Epic | undefined;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const toast = useToast();
  const lang = i18n.language;
  const hybrid = view.methodology === 'hybrid';
  const milestones = useMilestones(project.id);
  const save = useAgileMutation(project.id, agileApi.saveEpic);
  const [v, setV] = useState({
    titleAr: row?.titleAr ?? '',
    titleEn: row?.titleEn ?? '',
    weight: row ? String(toPercent(row.weight) ?? '') : '',
    releaseId: row?.releaseId ?? '',
    milestoneId: row?.milestoneId ?? '',
    acceptanceCriteria: row?.acceptanceCriteria ?? '',
    status: row?.status ?? 'open',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const leafMilestones = (milestones.data?.milestones ?? []).filter(
    (m) => !m.isHeader && !m.highLevel && m.status !== 'cancelled',
  );
  const submit = () =>
    save.mutate(
      {
        id: project.id,
        ...(row ? { itemId: row.id, version: row.version } : {}),
        values: {
          titleAr: v.titleAr.trim() || null,
          titleEn: v.titleEn.trim() || null,
          weight: fromPercent(v.weight) ?? 0,
          releaseId: v.releaseId || null,
          milestoneId: hybrid ? v.milestoneId || null : null,
          acceptanceCriteria: v.acceptanceCriteria.trim() || null,
          sort: row?.sort ?? view.epics.length + 1,
          status: row ? v.status : null,
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
        title={row ? t('backlog.editEpic') : t('backlog.addEpic')}
        className="max-w-2xl"
        footer={<SaveFooter loading={save.isPending} onSave={submit} />}
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="epic-dialog">
          {save.error && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
          <FormField label={t('common.titleAr')}>
            <Input dir="rtl" value={v.titleAr} onChange={(e) => set({ titleAr: e.target.value })} />
          </FormField>
          <FormField label={t('common.titleEn')}>
            <Input dir="ltr" value={v.titleEn} onChange={(e) => set({ titleEn: e.target.value })} />
          </FormField>
          <FormField label={t('backlog.weightPercent')} required>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={v.weight}
              onChange={(e) => set({ weight: e.target.value })}
              data-testid="epic-weight"
            />
          </FormField>
          <FormField label={t('backlog.release')}>
            <Select
              value={v.releaseId || '*'}
              onValueChange={(value) => set({ releaseId: value === '*' ? '' : value })}
              options={[
                { value: '*', label: t('backlog.noRelease') },
                ...view.releases
                  .filter((r) => r.status !== 'cancelled')
                  .map((r) => ({ value: r.id, label: pickText(lang, r.nameAr, r.nameEn) })),
              ]}
            />
          </FormField>
          {hybrid && (
            <FormField label={t('backlog.milestone')}>
              <Select
                value={v.milestoneId || '*'}
                onValueChange={(value) => set({ milestoneId: value === '*' ? '' : value })}
                options={[
                  { value: '*', label: t('backlog.noMilestone') },
                  ...leafMilestones.map((m) => ({
                    value: m.id,
                    label: `${m.code} · ${pickText(lang, m.titleAr, m.titleEn)}`,
                  })),
                ]}
              />
            </FormField>
          )}
          {row && (
            <FormField label={t('backlog.status')}>
              <Select
                value={v.status}
                onValueChange={(status) => set({ status })}
                options={(['open', 'done'] as const).map((s) => ({ value: s, label: t(`status.epic.${s}`) }))}
              />
            </FormField>
          )}
          <FormField label={t('common.acceptanceCriteria')} className="md:col-span-2">
            <Textarea
              dir="auto"
              value={v.acceptanceCriteria}
              onChange={(e) => set({ acceptanceCriteria: e.target.value })}
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function StoryDialog({
  project,
  view,
  row,
  epicId,
  onClose,
}: {
  project: ProjectFull;
  view: AgileView;
  row?: Story | undefined;
  epicId: string;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const toast = useToast();
  const lang = i18n.language;
  const save = useAgileMutation(project.id, agileApi.saveStory);
  const [v, setV] = useState({
    epicId: row?.epicId ?? epicId,
    title: row?.title ?? '',
    asA: row?.asA ?? '',
    iWant: row?.iWant ?? '',
    soThat: row?.soThat ?? '',
    acceptanceCriteria: row?.acceptanceCriteria ?? '',
    storyPoints: row ? String(row.storyPoints) : '',
    priority: row?.priority ?? 'should',
    assigneeEmployeeId: row?.assignee?.id ?? undefined,
    sprintId: row?.sprintId ?? '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const openSprints = view.sprints.filter(
    (s) => s.status === 'planned' || s.status === 'active' || s.id === row?.sprintId,
  );
  const submit = () =>
    save.mutate(
      {
        id: project.id,
        ...(row ? { itemId: row.id, version: row.version } : {}),
        values: {
          epicId: v.epicId,
          title: v.title.trim() || null,
          asA: v.asA.trim() || null,
          iWant: v.iWant.trim() || null,
          soThat: v.soThat.trim() || null,
          acceptanceCriteria: v.acceptanceCriteria.trim() || null,
          storyPoints: Number(v.storyPoints) || 0,
          priority: v.priority,
          assigneeEmployeeId: v.assigneeEmployeeId ?? null,
          sprintId: v.sprintId || null,
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
        title={row ? `${t('backlog.editStory')} ${row.code}` : t('backlog.addStory')}
        className="max-w-2xl"
        footer={<SaveFooter loading={save.isPending} onSave={submit} />}
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="story-dialog">
          {save.error && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
          <FormField label={t('backlog.storyTitle')} required className="md:col-span-2">
            <Input
              dir="auto"
              value={v.title}
              onChange={(e) => set({ title: e.target.value })}
              data-testid="story-title"
            />
          </FormField>
          <FormField label={t('backlog.asA')}>
            <Input dir="auto" value={v.asA} onChange={(e) => set({ asA: e.target.value })} />
          </FormField>
          <FormField label={t('backlog.iWant')}>
            <Input dir="auto" value={v.iWant} onChange={(e) => set({ iWant: e.target.value })} />
          </FormField>
          <FormField label={t('backlog.soThat')} className="md:col-span-2">
            <Input dir="auto" value={v.soThat} onChange={(e) => set({ soThat: e.target.value })} />
          </FormField>
          <FormField label={t('common.acceptanceCriteria')} className="md:col-span-2">
            <Textarea
              dir="auto"
              value={v.acceptanceCriteria}
              onChange={(e) => set({ acceptanceCriteria: e.target.value })}
            />
          </FormField>
          <FormField label={t('backlog.epic')}>
            <Select
              value={v.epicId}
              onValueChange={(value) => set({ epicId: value })}
              options={view.epics
                .filter((e) => e.status !== 'cancelled' || e.id === v.epicId)
                .map((e) => ({ value: e.id, label: `${e.code} · ${pickText(lang, e.titleAr, e.titleEn)}` }))}
            />
          </FormField>
          <FormField label={t('backlog.storyPoints')} required>
            <Input
              dir="ltr"
              type="number"
              min={0}
              max={100}
              value={v.storyPoints}
              onChange={(e) => set({ storyPoints: e.target.value })}
              data-testid="story-points"
            />
          </FormField>
          <FormField label={t('backlog.priority')}>
            <Select
              value={v.priority}
              onValueChange={(priority) => set({ priority })}
              options={priorities.map((p) => ({ value: p, label: t(`priority.${p}`) }))}
            />
          </FormField>
          <FormField label={t('backlog.sprint')}>
            <Select
              value={v.sprintId || '*'}
              onValueChange={(value) => set({ sprintId: value === '*' ? '' : value })}
              options={[
                { value: '*', label: t('backlog.noSprint') },
                ...openSprints.map((s) => ({
                  value: s.id,
                  label: `#${formatNumber(s.number, lang)} ${s.name ?? ''}`.trim(),
                })),
              ]}
            />
          </FormField>
          <FormField label={t('backlog.assignee')} className="md:col-span-2">
            <EmployeePicker value={v.assigneeEmployeeId} onChange={(id) => set({ assigneeEmployeeId: id })} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CancelDialog({
  project,
  target,
  onClose,
}: {
  project: ProjectFull;
  target: { kind: 'epic'; row: Epic } | { kind: 'story'; row: Story };
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const cancelEpic = useAgileMutation(project.id, agileApi.cancelEpic);
  const cancelStory = useAgileMutation(project.id, agileApi.cancelStory);
  const error = cancelEpic.error ?? cancelStory.error;
  const name =
    target.kind === 'epic'
      ? `${target.row.code} ${pickText(i18n.language, target.row.titleAr, target.row.titleEn)}`
      : `${target.row.code} ${target.row.title ?? ''}`;
  const done = {
    onSuccess: () => {
      toast({ title: t('common.saved') });
      onClose();
    },
  };
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('common.confirmCancel')}
        description={t('common.confirmCancelText', { name: name.trim() })}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              loading={cancelEpic.isPending || cancelStory.isPending}
              data-testid="agile-cancel-confirm"
              onClick={() =>
                target.kind === 'epic'
                  ? cancelEpic.mutate({ id: project.id, itemId: target.row.id }, done)
                  : cancelStory.mutate({ sid: target.row.id }, done)
              }
            >
              {target.kind === 'epic' ? t('backlog.cancelEpic') : t('backlog.cancelStory')}
            </Button>
          </>
        }
      >
        {error && <FieldErrors error={error} />}
      </DialogContent>
    </Dialog>
  );
}
