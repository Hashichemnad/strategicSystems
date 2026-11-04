import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, RefreshCw, Sparkles } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  Input,
  Skeleton,
  StatusBanner,
  StatusChip,
  useToast,
} from '@/ui';
import { fromPercent, projectApi, projectKeys, toPercent, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { agileApi, agileKeys, type AgileView, type PlanDraft } from './agileApi';
import { isFullWeight, sumRatios } from './boardLogic';

interface RowState {
  key: string;
  selected: boolean;
  done: boolean;
  error: unknown;
}

interface MilestoneRow extends RowState {
  phase: string;
  titleAr: string;
  titleEn: string;
  weight: string;
  plannedStart: string | undefined;
  plannedEnd: string | undefined;
}

interface StoryRow extends RowState {
  title: string;
  asA: string | null;
  iWant: string | null;
  soThat: string | null;
  points: string;
}

interface EpicRow extends RowState {
  titleAr: string;
  titleEn: string;
  weight: string;
  stories: StoryRow[];
  /** Set once the epic is created, so a retry adds only its remaining stories. */
  createdId: string | null;
}

const fresh = { selected: true, done: false, error: null };

function toRows(draft: PlanDraft) {
  const pct = (ratio: number) => String(toPercent(ratio) ?? 0);
  const milestones: MilestoneRow[] = draft.milestones.map((m, i) => ({
    key: `m${i}`,
    ...fresh,
    phase: m.phase ?? '',
    titleAr: m.titleAr ?? '',
    titleEn: m.titleEn ?? '',
    weight: pct(m.weight),
    plannedStart: m.plannedStart,
    plannedEnd: m.plannedEnd,
  }));
  const epics: EpicRow[] = draft.epics.map((e, i) => ({
    key: `e${i}`,
    ...fresh,
    titleAr: e.titleAr ?? '',
    titleEn: e.titleEn ?? '',
    weight: pct(e.weight),
    createdId: null,
    stories: e.stories.map((s, j) => ({
      key: `e${i}s${j}`,
      ...fresh,
      title: s.title,
      asA: s.asA,
      iWant: s.iWant,
      soThat: s.soThat,
      points: String(s.points),
    })),
  }));
  return { milestones, epics };
}

/** AI plan generator (design §40): a DRAFT the user edits and ticks; rows are created through the normal endpoints on "Add selected". */
export function AiPlanDialog({
  project,
  open,
  onOpenChange,
}: {
  project: ProjectFull;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const lang = i18n.language;
  const [methodology, setMethodology] = useState<string | null>(null);
  const [milestones, setMilestones] = useState<MilestoneRow[]>([]);
  const [epics, setEpics] = useState<EpicRow[]>([]);
  const [adding, setAdding] = useState(false);
  const [partial, setPartial] = useState(false);
  // Phase headers created in this dialog (phase name → milestone id), reused when a failed leaf is retried.
  const phaseIds = useRef(new Map<string, string>());
  const draft = useMutation({
    mutationFn: agileApi.draftPlan,
    onSuccess: (plan) => {
      const rows = toRows(plan);
      setMethodology(plan.methodology);
      setMilestones(rows.milestones);
      setEpics(rows.epics);
      setPartial(false);
      phaseIds.current = new Map();
    },
  });
  const { mutate: generate } = draft;
  // One draft per opening (a ref, so React's development double effects do not ask twice).
  const requested = useRef(false);
  useEffect(() => {
    if (!open) {
      requested.current = false;
      return;
    }
    if (requested.current) return;
    requested.current = true;
    generate({ id: project.id });
  }, [open, generate, project.id]);

  const agile = methodology === 'agile';
  const total = agile
    ? sumRatios(epics.filter((e) => e.selected && !e.done).map((e) => fromPercent(e.weight) ?? 0))
    : sumRatios(milestones.filter((m) => m.selected && !m.done).map((m) => fromPercent(m.weight) ?? 0));
  const selectedCount = agile
    ? epics.filter((e) => e.selected && !e.done).length +
      epics.reduce((n, e) => n + e.stories.filter((s) => s.selected && !s.done).length, 0)
    : milestones.filter((m) => m.selected && !m.done).length;

  const patchMilestone = (key: string, patch: Partial<MilestoneRow>) =>
    setMilestones((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const patchEpic = (key: string, patch: Partial<EpicRow>) =>
    setEpics((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const patchStory = (epicKey: string, key: string, patch: Partial<StoryRow>) =>
    setEpics((rows) =>
      rows.map((r) =>
        r.key === epicKey
          ? { ...r, stories: r.stories.map((s) => (s.key === key ? { ...s, ...patch } : s)) }
          : r,
      ),
    );

  const refresh = async (view: AgileView | null) => {
    if (view) queryClient.setQueryData(agileKeys.view(project.id), view);
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: projectKeys.project(project.id),
        predicate: (query) => !view || query.queryKey[3] !== 'agile',
      }),
      queryClient.invalidateQueries({ queryKey: ['portfolio', 'projects'] }),
    ]);
  };

  const addMilestones = async () => {
    let added = 0;
    let failed = false;
    const rows = milestones.filter((m) => m.selected && !m.done);
    for (const phase of [...new Set(rows.map((m) => m.phase.trim()).filter(Boolean))]) {
      if (phaseIds.current.has(phase)) continue;
      const children = rows.filter((m) => m.phase.trim() === phase);
      const starts = children.flatMap((m) => (m.plannedStart ? [m.plannedStart] : [])).sort();
      const ends = children.flatMap((m) => (m.plannedEnd ? [m.plannedEnd] : [])).sort();
      try {
        const header = await projectApi.createMilestone({
          id: project.id,
          values: {
            titleAr: phase,
            titleEn: phase,
            parentId: null,
            isHeader: true,
            highLevel: false,
            plannedStart: starts[0] ?? null,
            plannedEnd: ends[ends.length - 1] ?? null,
            weight: 0,
            cost: null,
            isDeliverable: false,
            mainDeliverable: null,
            acceptanceCriteria: null,
            qualityMet: null,
            isInnovative: false,
            innovativeReason: null,
            sort: 0,
            deliveredInSprints: false,
          },
        });
        phaseIds.current.set(phase, header.id);
        added++;
      } catch (error) {
        failed = true;
        for (const child of children) patchMilestone(child.key, { error });
      }
    }
    for (const [index, m] of rows.entries()) {
      const phase = m.phase.trim();
      if (phase && !phaseIds.current.has(phase)) continue;
      try {
        await projectApi.createMilestone({
          id: project.id,
          values: {
            titleAr: m.titleAr.trim() || null,
            titleEn: m.titleEn.trim() || null,
            parentId: phase ? (phaseIds.current.get(phase) ?? null) : null,
            isHeader: false,
            highLevel: false,
            plannedStart: m.plannedStart ?? null,
            plannedEnd: m.plannedEnd ?? null,
            weight: fromPercent(m.weight) ?? 0,
            cost: null,
            isDeliverable: false,
            mainDeliverable: null,
            acceptanceCriteria: null,
            qualityMet: null,
            isInnovative: false,
            innovativeReason: null,
            sort: index + 1,
            deliveredInSprints: false,
          },
        });
        patchMilestone(m.key, { done: true, error: null });
        added++;
      } catch (error) {
        failed = true;
        patchMilestone(m.key, { error });
      }
    }
    await refresh(null);
    return { added, failed };
  };

  const addEpics = async () => {
    let added = 0;
    let failed = false;
    let view: AgileView | null = null;
    for (const e of epics) {
      if (!e.selected) continue;
      let epicId = e.createdId;
      if (!e.done) {
        try {
          const known = new Set(
            (view ?? queryClient.getQueryData<AgileView>(agileKeys.view(project.id)))?.epics.map(
              (x) => x.id,
            ) ?? [],
          );
          view = await agileApi.saveEpic({
            id: project.id,
            values: {
              titleAr: e.titleAr.trim() || null,
              titleEn: e.titleEn.trim() || null,
              weight: fromPercent(e.weight) ?? 0,
              releaseId: null,
              milestoneId: null,
              acceptanceCriteria: null,
              sort: known.size + 1,
            },
          });
          const created =
            view.epics.find(
              (x) =>
                !known.has(x.id) &&
                x.titleEn === (e.titleEn.trim() || null) &&
                x.titleAr === (e.titleAr.trim() || null),
            ) ?? view.epics.find((x) => !known.has(x.id));
          epicId = created?.id ?? null;
          patchEpic(e.key, { done: true, error: null, createdId: epicId });
          added++;
        } catch (error) {
          failed = true;
          patchEpic(e.key, { error });
          continue;
        }
      }
      if (!epicId) continue;
      for (const s of e.stories) {
        if (!s.selected || s.done) continue;
        try {
          view = await agileApi.saveStory({
            id: project.id,
            values: {
              epicId,
              title: s.title.trim() || null,
              asA: s.asA,
              iWant: s.iWant,
              soThat: s.soThat,
              acceptanceCriteria: null,
              storyPoints: Number(s.points) || 0,
              priority: 'should',
              assigneeEmployeeId: null,
              sprintId: null,
            },
          });
          patchStory(e.key, s.key, { done: true, error: null });
          added++;
        } catch (error) {
          failed = true;
          patchStory(e.key, s.key, { error });
        }
      }
    }
    await refresh(view);
    return { added, failed };
  };

  const addSelected = async () => {
    setAdding(true);
    try {
      const result = agile ? await addEpics() : await addMilestones();
      setPartial(result.failed);
      if (result.added > 0) toast({ title: t('ai.added', { value: formatNumber(result.added, lang) }) });
      if (!result.failed) onOpenChange(false);
    } finally {
      setAdding(false);
    }
  };

  const empty = draft.data && milestones.length === 0 && epics.length === 0;
  return (
    <Dialog open={open} onOpenChange={(o) => !adding && onOpenChange(o)}>
      <DialogContent
        title={t('ai.title')}
        description={t('ai.hint')}
        className="max-w-5xl"
        footer={
          <>
            <Button
              variant="outline"
              onClick={() => generate({ id: project.id })}
              disabled={draft.isPending || adding}
              data-testid="ai-plan-regenerate"
            >
              <RefreshCw aria-hidden />
              {t('ai.regenerate')}
            </Button>
            <DialogClose asChild>
              <Button variant="outline" disabled={adding}>
                {tc('actions.cancel')}
              </Button>
            </DialogClose>
            <Button
              onClick={() => void addSelected()}
              loading={adding}
              disabled={draft.isPending || selectedCount === 0}
              data-testid="ai-plan-add"
            >
              {t('ai.add')}
            </Button>
          </>
        }
      >
        <div className="flex max-h-[65vh] flex-col gap-4 overflow-y-auto" data-testid="ai-plan-dialog">
          {draft.isPending && (
            <div className="flex flex-col gap-2" aria-busy="true">
              <p className="text-sm text-muted-foreground">{t('ai.generating')}</p>
              <Skeleton className="h-40" />
            </div>
          )}
          {draft.error && <FieldErrors error={draft.error} />}
          {draft.data && (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <StatusChip tone="info">
                  <Sparkles className="me-1 inline size-3.5" aria-hidden />
                  {t('ai.draftLabel')}
                </StatusChip>
                {!empty && (
                  <span data-testid="ai-plan-total">
                    <StatusChip tone={isFullWeight(total) ? 'success' : 'warning'}>
                      {t('ai.total', { value: formatNumber(toPercent(total) ?? 0, lang) })}
                    </StatusChip>
                  </span>
                )}
              </div>
              {partial && <StatusBanner tone="warning" title={t('ai.partial')} />}
              {empty && <p className="text-sm text-muted-foreground">{t('ai.empty')}</p>}
              {!agile && milestones.length > 0 && (
                <MilestoneRows rows={milestones} onChange={patchMilestone} disabled={adding} />
              )}
              {agile && epics.length > 0 && (
                <EpicRows epics={epics} onEpic={patchEpic} onStory={patchStory} disabled={adding} />
              )}
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function RowStatus({ row }: { row: RowState }) {
  const { t } = useTranslation('agile');
  if (row.done)
    return (
      <CheckCircle2
        className="size-4 text-success"
        aria-label={t('common.saved')}
        data-testid="ai-row-done"
      />
    );
  return null;
}

function MilestoneRows({
  rows,
  onChange,
  disabled,
}: {
  rows: MilestoneRow[];
  onChange: (key: string, patch: Partial<MilestoneRow>) => void;
  disabled: boolean;
}) {
  const { t } = useTranslation('agile');
  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('ai.milestones')}</h3>
      <ul className="flex flex-col gap-2">
        {rows.map((m) => {
          const name = m.titleEn || m.titleAr;
          const locked = disabled || m.done;
          return (
            <li key={m.key} className="flex flex-col gap-2 rounded-md border p-3" data-testid="ai-plan-row">
              <div className="grid items-end gap-2 md:grid-cols-[auto_1fr_1fr_1fr_6rem_9rem_9rem_auto]">
                <input
                  type="checkbox"
                  className="mb-3 size-4 accent-primary"
                  checked={m.selected}
                  disabled={locked}
                  onChange={(e) => onChange(m.key, { selected: e.target.checked })}
                  aria-label={t('ai.selectRow', { name })}
                />
                <LabeledInput
                  label={t('ai.phase')}
                  value={m.phase}
                  disabled={locked}
                  onChange={(phase) => onChange(m.key, { phase })}
                />
                <LabeledInput
                  label={t('common.titleAr')}
                  dir="rtl"
                  value={m.titleAr}
                  disabled={locked}
                  onChange={(titleAr) => onChange(m.key, { titleAr })}
                />
                <LabeledInput
                  label={t('common.titleEn')}
                  dir="ltr"
                  value={m.titleEn}
                  disabled={locked}
                  onChange={(titleEn) => onChange(m.key, { titleEn })}
                />
                <LabeledInput
                  label={t('backlog.weightPercent')}
                  dir="ltr"
                  value={m.weight}
                  disabled={locked}
                  onChange={(weight) => onChange(m.key, { weight })}
                />
                <label className="flex flex-col gap-1 text-xs">
                  {t('ai.plannedStart')}
                  <DatePicker
                    value={m.plannedStart}
                    disabled={locked}
                    onChange={(plannedStart) => onChange(m.key, { plannedStart })}
                  />
                </label>
                <label className="flex flex-col gap-1 text-xs">
                  {t('ai.plannedEnd')}
                  <DatePicker
                    value={m.plannedEnd}
                    disabled={locked}
                    onChange={(plannedEnd) => onChange(m.key, { plannedEnd })}
                  />
                </label>
                <span className="mb-3">
                  <RowStatus row={m} />
                </span>
              </div>
              {m.error ? <FieldErrors error={m.error} /> : null}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function EpicRows({
  epics,
  onEpic,
  onStory,
  disabled,
}: {
  epics: EpicRow[];
  onEpic: (key: string, patch: Partial<EpicRow>) => void;
  onStory: (epicKey: string, key: string, patch: Partial<StoryRow>) => void;
  disabled: boolean;
}) {
  const { t } = useTranslation('agile');
  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('ai.epics')}</h3>
      <ul className="flex flex-col gap-3">
        {epics.map((e) => {
          const name = e.titleEn || e.titleAr;
          const locked = disabled || e.done;
          return (
            <li key={e.key} className="flex flex-col gap-2 rounded-md border p-3" data-testid="ai-plan-row">
              <div className="grid items-end gap-2 md:grid-cols-[auto_1fr_1fr_6rem_auto]">
                <input
                  type="checkbox"
                  className="mb-3 size-4 accent-primary"
                  checked={e.selected}
                  disabled={locked}
                  onChange={(ev) => onEpic(e.key, { selected: ev.target.checked })}
                  aria-label={t('ai.selectRow', { name })}
                />
                <LabeledInput
                  label={t('common.titleAr')}
                  dir="rtl"
                  value={e.titleAr}
                  disabled={locked}
                  onChange={(titleAr) => onEpic(e.key, { titleAr })}
                />
                <LabeledInput
                  label={t('common.titleEn')}
                  dir="ltr"
                  value={e.titleEn}
                  disabled={locked}
                  onChange={(titleEn) => onEpic(e.key, { titleEn })}
                />
                <LabeledInput
                  label={t('backlog.weightPercent')}
                  dir="ltr"
                  value={e.weight}
                  disabled={locked}
                  onChange={(weight) => onEpic(e.key, { weight })}
                />
                <span className="mb-3">
                  <RowStatus row={e} />
                </span>
              </div>
              {e.error ? <FieldErrors error={e.error} /> : null}
              {e.stories.length > 0 && (
                <ul className="flex flex-col gap-2 border-s-2 ps-4">
                  {e.stories.map((s) => {
                    const storyLocked = disabled || s.done || !e.selected;
                    return (
                      <li key={s.key} className="flex flex-col gap-1" data-testid="ai-plan-story">
                        <div className="grid items-end gap-2 md:grid-cols-[auto_1fr_6rem_auto]">
                          <input
                            type="checkbox"
                            className="mb-3 size-4 accent-primary"
                            checked={s.selected && e.selected}
                            disabled={storyLocked}
                            onChange={(ev) => onStory(e.key, s.key, { selected: ev.target.checked })}
                            aria-label={t('ai.selectRow', { name: s.title })}
                          />
                          <LabeledInput
                            label={t('ai.storyTitle')}
                            value={s.title}
                            disabled={storyLocked}
                            onChange={(title) => onStory(e.key, s.key, { title })}
                          />
                          <LabeledInput
                            label={t('ai.points')}
                            dir="ltr"
                            value={s.points}
                            disabled={storyLocked}
                            onChange={(points) => onStory(e.key, s.key, { points })}
                          />
                          <span className="mb-3">
                            <RowStatus row={s} />
                          </span>
                        </div>
                        {(s.asA || s.iWant || s.soThat) && (
                          <p className="text-xs text-muted-foreground" dir="auto">
                            {[s.asA, s.iWant, s.soThat].filter(Boolean).join(' · ')}
                          </p>
                        )}
                        {s.error ? <FieldErrors error={s.error} /> : null}
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function LabeledInput({
  label,
  value,
  onChange,
  disabled,
  dir = 'auto',
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  dir?: 'rtl' | 'ltr' | 'auto';
}) {
  return (
    <label className="flex flex-col gap-1 text-xs">
      {label}
      <Input dir={dir} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}
