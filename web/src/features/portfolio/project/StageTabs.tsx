import { CalendarPlus, Send } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EmployeePicker } from '@/features/org/EmployeePicker';
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
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  projectApi,
  toPercent,
  useChangeRequests,
  useMonthly,
  useProjectMutation,
  type ChangeApplyValues,
  type ChangeRequest,
  type ChangeRequestValues,
  type MonthlyUpdate,
  type ProjectFull,
} from '../projectApi';
import { useFeature } from '../featuresApi';
import { ChangeImpactPanel, MonthlyHistoryExtras, MonthlyNarrativePanel } from '../standards/StageStandards';
import { FieldErrors } from './FieldErrors';
import { LessonsTab } from './ProjectItemTabs';
import type { PortfolioOptions } from '../api';

type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';
const monthlyTone: Record<string, Tone> = {
  open: 'info',
  submitted: 'warning',
  returned: 'danger',
  approved: 'success',
};

/** "YYYY-MM" or "YYYY-MM (n)" for a second window in the same month. */
export function periodLabel(m: Pick<MonthlyUpdate, 'period' | 'sequence'>) {
  return m.sequence > 1 ? `${m.period} (${m.sequence})` : m.period;
}

/** The monthly update window (W4, ADR 0071): current window, open now (PM Office), submit, history with the submitted snapshot. */
export function MonthlyTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('portfolio');
  const toast = useToast();
  const monthly = useMonthly(project.id);
  const open = useProjectMutation(project.id, projectApi.openWindow);
  const submit = useProjectMutation(project.id, projectApi.submitMonthly);
  const [comment, setComment] = useState('');
  const lang = i18n.language;
  if (monthly.isPending) return <Skeleton className="h-40" />;
  if (!monthly.data) return null;
  const { current, history, canOpen, canSubmit, pendingMilestones } = monthly.data;
  return (
    <div className="flex flex-col gap-6" data-testid="project-monthly">
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-6">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">{t('project.monthly.current')}</h2>
          {canOpen && (
            <Button
              variant="outline"
              loading={open.isPending}
              onClick={() =>
                open.mutate(
                  { id: project.id },
                  { onSuccess: () => toast({ title: t('project.monthly.opened') }) },
                )
              }
            >
              <CalendarPlus aria-hidden />
              {t('actions.openWindow')}
            </Button>
          )}
        </header>
        {!current ? (
          <p className="text-sm text-muted-foreground">{t('project.monthly.none')}</p>
        ) : (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <bdi className="font-medium">{periodLabel(current)}</bdi>
              <StatusChip tone={monthlyTone[current.status] ?? 'neutral'}>
                {t(`project.monthly.statuses.${current.status as 'open'}`)}
              </StatusChip>
              {current.overdue && <StatusChip tone="danger">{t('project.monthly.overdue')}</StatusChip>}
              {current.dueAt && (
                <span className="text-sm text-muted-foreground">
                  {t('project.monthly.due', { date: formatDateTime(current.dueAt, lang) })}
                </span>
              )}
            </div>
            <p className="text-sm">{t('project.monthly.pending', { count: pendingMilestones })}</p>
            <MonthlyNarrativePanel
              key={`${current.id}:${current.status}`}
              project={project}
              current={current}
            />
            {canSubmit && (
              <div className="flex flex-col gap-2">
                <FormField label={t('dialogs.submitComment')}>
                  <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
                </FormField>
                <div>
                  <Button
                    loading={submit.isPending}
                    onClick={() =>
                      submit.mutate(
                        { id: project.id, version: project.version, comment: comment.trim() || null },
                        {
                          onSuccess: () => {
                            setComment('');
                            toast({ title: t('toast.submitted') });
                          },
                        },
                      )
                    }
                  >
                    <Send aria-hidden />
                    {t('project.monthly.submit')}
                  </Button>
                </div>
              </div>
            )}
            {(open.error ?? submit.error) && <FieldErrors error={open.error ?? submit.error} />}
          </>
        )}
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="font-semibold">{t('project.monthly.history')}</h2>
        {history.length === 0 ? (
          <EmptyState title={t('project.monthly.noHistory')} />
        ) : (
          <ul className="flex flex-col gap-3">
            {history.map((m) => (
              <li key={m.id} className="rounded-lg border bg-card p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <bdi className="font-medium">{periodLabel(m)}</bdi>
                  <StatusChip tone={monthlyTone[m.status] ?? 'neutral'}>
                    {t(`project.monthly.statuses.${m.status as 'open'}`)}
                  </StatusChip>
                  {m.approvedAt && (
                    <span className="text-sm text-muted-foreground">
                      {formatDateTime(m.approvedAt, lang)}
                    </span>
                  )}
                </div>
                <Snapshot items={m.items} />
                <MonthlyHistoryExtras project={project} update={m} />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

interface SnapshotItem {
  code: string;
  titleAr: string | null;
  titleEn: string | null;
  achieve: number | null;
  approved: number;
  causality: string | null;
}

function Snapshot({ items }: { items: string }) {
  const { t, i18n } = useTranslation('portfolio');
  let rows: SnapshotItem[] = [];
  try {
    rows = JSON.parse(items) as SnapshotItem[];
  } catch {
    rows = [];
  }
  if (rows.length === 0) return null;
  return (
    <ul className="mt-2 flex flex-col gap-1 text-sm">
      {rows.map((r) => (
        <li key={r.code}>
          <bdi>{r.code}</bdi> · {pickText(i18n.language, r.titleAr, r.titleEn)} ·{' '}
          {t('project.monthly.snapshot', {
            from: formatNumber(toPercent(r.approved) ?? 0, i18n.language),
            to: formatNumber(toPercent(r.achieve) ?? 0, i18n.language),
          })}
          {r.causality && <span className="block text-muted-foreground">{r.causality}</span>}
        </li>
      ))}
    </ul>
  );
}

const methodologies = ['waterfall', 'agile', 'hybrid'] as const;
const methodologyCode = (m: string | null | undefined) => (m === 'agile' || m === 'hybrid' ? m : 'waterfall');
const isDetailsType = (type: string) => type === 'details' || type === 'postponement';

const changeTone: Record<string, Tone> = {
  draft: 'neutral',
  'under-review': 'info',
  approved: 'warning',
  rejected: 'danger',
  cancelled: 'neutral',
  completed: 'success',
};

/** Change requests (W7, Q-P9): create, submit, cancel, and the PM's "Apply changes" pre-filled from the approved request. */
export function ChangesTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('portfolio');
  const changes = useChangeRequests(project.id);
  const [editing, setEditing] = useState<{ row?: ChangeRequest } | null>(null);
  const [applying, setApplying] = useState<ChangeRequest | null>(null);
  const [cancelling, setCancelling] = useState<ChangeRequest | null>(null);
  const submit = useProjectMutation(project.id, projectApi.submitChange);
  const lang = i18n.language;
  const canCreate =
    project.permissions.canChange && project.status === 'active' && project.subState !== 'done';
  return (
    <section className="flex flex-col gap-4" data-testid="project-changes">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('project.tabs.changes')}</h2>
          <p className="text-sm text-muted-foreground">{t('project.change.hint')}</p>
        </div>
        {canCreate && <Button onClick={() => setEditing({})}>{t('project.change.new')}</Button>}
      </header>
      {submit.isError && <FieldErrors error={submit.error} />}
      {changes.isPending ? (
        <Skeleton className="h-32" />
      ) : (changes.data ?? []).length === 0 ? (
        <EmptyState title={t('project.change.empty')} />
      ) : (
        <ul className="flex flex-col gap-3">
          {(changes.data ?? []).map((c) => (
            <li
              key={c.id}
              className="flex flex-col gap-2 rounded-lg border bg-card p-4"
              data-testid="change-request"
            >
              <div className="flex flex-wrap items-center gap-2">
                <bdi className="font-medium">{c.number}</bdi>
                <StatusChip tone="info">{t(`project.change.types.${c.type as 'details'}`)}</StatusChip>
                <StatusChip tone={changeTone[c.status] ?? 'neutral'}>
                  {t(`project.change.statuses.${c.status as 'draft'}`)}
                </StatusChip>
              </div>
              <p className="whitespace-pre-line text-sm" dir="auto">
                {c.reason}
              </p>
              <ProposedSummary change={c} project={project} />
              <ChangeImpactPanel change={c} />
              {c.applied.length > 0 && (
                <div className="text-sm">
                  <h3 className="font-medium">{t('project.change.appliedLog')}</h3>
                  <ul className="list-disc ps-5" data-testid="change-log">
                    {c.applied.map((a, i) => (
                      <li key={i}>
                        {t(`project.change.fields.${a.field as 'budget'}`, { defaultValue: a.field })}:{' '}
                        <bdi>{a.old ?? '—'}</bdi> → <bdi>{a.new ?? '—'}</bdi>
                      </li>
                    ))}
                  </ul>
                  {c.implementedAt && (
                    <p className="text-muted-foreground">{formatDateTime(c.implementedAt, lang)}</p>
                  )}
                </div>
              )}
              <div className="flex flex-wrap gap-2">
                {c.canEdit && (
                  <>
                    <Button size="sm" variant="outline" onClick={() => setEditing({ row: c })}>
                      {t('actions.edit')}
                    </Button>
                    <Button
                      size="sm"
                      loading={submit.isPending}
                      onClick={() => submit.mutate({ cid: c.id, version: c.version })}
                    >
                      {t('actions.submit')}
                    </Button>
                  </>
                )}
                {c.canApply && (
                  <Button size="sm" onClick={() => setApplying(c)} data-testid="change-apply">
                    {t('project.change.apply')}
                  </Button>
                )}
                {(c.canEdit || c.canApply) && (
                  <Button size="sm" variant="outline" onClick={() => setCancelling(c)}>
                    {t('actions.cancel')}
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
      {editing && <ChangeDialog project={project} row={editing.row} onClose={() => setEditing(null)} />}
      {applying && <ApplyDialog project={project} change={applying} onClose={() => setApplying(null)} />}
      {cancelling && (
        <CancelChangeDialog project={project} change={cancelling} onClose={() => setCancelling(null)} />
      )}
    </section>
  );
}

function ProposedSummary({ change: c, project }: { change: ChangeRequest; project: ProjectFull }) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: ts } = useTranslation('pmStandards');
  const lang = i18n.language;
  const rows: [string, string][] = [];
  if (c.proposedNameAr || c.proposedNameEn)
    rows.push([t('project.change.fields.name'), pickText(lang, c.proposedNameAr, c.proposedNameEn)]);
  if (c.proposedStart) rows.push([t('fields.plannedStart'), formatDate(c.proposedStart, lang)]);
  if (c.proposedEnd) rows.push([t('fields.plannedEnd'), formatDate(c.proposedEnd, lang)]);
  if (c.proposedBudget !== null)
    rows.push([
      t('project.change.fields.budget'),
      `${formatNumber(project.budget ?? 0, lang)} → ${formatNumber(c.proposedBudget, lang)}`,
    ]);
  if (c.newPm) rows.push([t('project.change.fields.pm'), pickText(lang, c.newPm.nameAr, c.newPm.nameEn)]);
  if (c.newOwner)
    rows.push([t('project.change.fields.owner'), pickText(lang, c.newOwner.nameAr, c.newOwner.nameEn)]);
  if (c.mergeIntoProjectNumber) rows.push([t('project.change.fields.mergedInto'), c.mergeIntoProjectNumber]);
  if (c.proposedMethodology)
    rows.push([
      ts('change.methodology'),
      ts(`change.methodologies.${methodologyCode(c.proposedMethodology)}`),
    ]);
  if (c.areas.includes('plan')) rows.push([ts('change.areas'), ts('change.replan')]);
  if (rows.length === 0) return null;
  return (
    <dl className="grid gap-1 text-sm md:grid-cols-2">
      {rows.map(([k, v]) => (
        <div key={k}>
          <dt className="inline text-muted-foreground">{k}: </dt>
          <dd className="inline" dir="auto">
            {v}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function ChangeDialog({
  project,
  row,
  onClose,
}: {
  project: ProjectFull;
  row: ChangeRequest | undefined;
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const create = useProjectMutation(project.id, projectApi.createChange);
  const update = useProjectMutation(project.id, projectApi.updateChange);
  const [v, setV] = useState({
    type: row?.type ?? 'details',
    reason: row?.reason ?? '',
    impactOfNonResponse: row?.impactOfNonResponse ?? '',
    proposedNameAr: row?.proposedNameAr ?? '',
    proposedNameEn: row?.proposedNameEn ?? '',
    proposedScope: row?.proposedScope ?? '',
    proposedStart: row?.proposedStart ?? undefined,
    proposedEnd: row?.proposedEnd ?? undefined,
    proposedBudget:
      row?.proposedBudget !== null && row?.proposedBudget !== undefined ? String(row.proposedBudget) : '',
    costAffected: row?.costAffected ?? false,
    newPm: row?.newPm?.id ?? undefined,
    newOwner: row?.newOwner?.id ?? undefined,
    replan: row?.areas.includes('plan') ?? false,
    changeMethodology: Boolean(row?.proposedMethodology),
    proposedMethodology: row?.proposedMethodology ?? undefined,
  });
  const { t: ts } = useTranslation('pmStandards');
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const details = v.type === 'details';
  const otherAreas = (row?.areas ?? []).filter((a) => a !== 'plan' && a !== 'methodology');
  const methodology = details && v.changeMethodology ? (v.proposedMethodology ?? null) : null;
  const dates = details || v.type === 'postponement';
  const values: ChangeRequestValues = {
    type: v.type,
    areas: [...otherAreas, ...(details && v.replan ? ['plan'] : []), ...(methodology ? ['methodology'] : [])],
    reason: v.reason.trim() || null,
    impactOfNonResponse: v.impactOfNonResponse.trim() || null,
    proposedNameAr: details ? v.proposedNameAr.trim() || null : null,
    proposedNameEn: details ? v.proposedNameEn.trim() || null : null,
    proposedDescription: null,
    proposedScope: details ? v.proposedScope.trim() || null : null,
    proposedStart: dates ? (v.proposedStart ?? null) : null,
    proposedEnd: dates ? (v.proposedEnd ?? null) : null,
    proposedBudget: details && v.proposedBudget.trim() !== '' ? Number(v.proposedBudget) : null,
    costAffected: v.costAffected,
    newPmEmployeeId: v.type === 'ownership' ? (v.newPm ?? null) : null,
    newOwnerEmployeeId: v.type === 'ownership' ? (v.newOwner ?? null) : null,
    mergeIntoProjectId: row?.mergeIntoProjectId ?? null,
    requestedDate: null,
    proposedMethodology: methodology,
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? row.number : t('project.change.new')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={create.isPending || update.isPending}
              onClick={() =>
                row
                  ? update.mutate({ cid: row.id, version: row.version, values }, { onSuccess: onClose })
                  : create.mutate({ id: project.id, values }, { onSuccess: onClose })
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="change-dialog">
          <FormField label={t('project.change.type')} required>
            <Select
              value={v.type}
              onValueChange={(type) => set({ type })}
              options={['details', 'postponement', 'ownership', 'cancellation', 'merge'].map((x) => ({
                value: x,
                label: t(`project.change.types.${x as 'details'}`),
              }))}
            />
          </FormField>
          <div className="flex items-end">
            <Checkbox
              label={t('project.change.costAffected')}
              checked={v.costAffected}
              onChange={(e) => set({ costAffected: e.target.checked })}
            />
          </div>
          <FormField label={t('project.change.reason')} required className="md:col-span-2">
            <Textarea value={v.reason} onChange={(e) => set({ reason: e.target.value })} />
          </FormField>
          <FormField label={t('project.change.impactOfNonResponse')} className="md:col-span-2">
            <Textarea
              value={v.impactOfNonResponse}
              onChange={(e) => set({ impactOfNonResponse: e.target.value })}
            />
          </FormField>
          {details && (
            <>
              <FormField label={t('fields.nameAr')}>
                <Input
                  dir="rtl"
                  value={v.proposedNameAr}
                  onChange={(e) => set({ proposedNameAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('fields.nameEn')}>
                <Input
                  dir="ltr"
                  value={v.proposedNameEn}
                  onChange={(e) => set({ proposedNameEn: e.target.value })}
                />
              </FormField>
              <FormField label={t('fields.scopeOfWork')} className="md:col-span-2">
                <Textarea value={v.proposedScope} onChange={(e) => set({ proposedScope: e.target.value })} />
              </FormField>
              <FormField label={t('project.change.fields.budget')}>
                <Input
                  dir="ltr"
                  inputMode="decimal"
                  value={v.proposedBudget}
                  onChange={(e) => set({ proposedBudget: e.target.value })}
                  data-testid="change-budget"
                />
              </FormField>
              <div className="md:col-span-2 flex flex-col gap-2 rounded-md border p-3">
                <Checkbox
                  label={ts('change.replan')}
                  description={ts('change.replanHint')}
                  checked={v.replan}
                  onChange={(e) => set({ replan: e.target.checked })}
                  data-testid="change-area-plan"
                />
                <Checkbox
                  label={ts('change.changeMethodology')}
                  checked={v.changeMethodology}
                  onChange={(e) => set({ changeMethodology: e.target.checked })}
                  data-testid="change-area-methodology"
                />
                {v.changeMethodology && (
                  <FormField label={ts('change.methodology')} required>
                    <Select
                      value={v.proposedMethodology}
                      onValueChange={(m) => set({ proposedMethodology: m })}
                      options={methodologies
                        .filter((m) => m !== project.delivery.methodology)
                        .map((m) => ({ value: m, label: ts(`change.methodologies.${m}`) }))}
                    />
                  </FormField>
                )}
              </div>
            </>
          )}
          {dates && (
            <>
              <FormField label={t('fields.plannedStart')}>
                <DatePicker value={v.proposedStart} onChange={(d) => set({ proposedStart: d })} />
              </FormField>
              <FormField label={t('fields.plannedEnd')} required={v.type === 'postponement'}>
                <DatePicker value={v.proposedEnd} onChange={(d) => set({ proposedEnd: d })} />
              </FormField>
            </>
          )}
          {v.type === 'ownership' && (
            <>
              <FormField label={t('project.change.fields.pm')}>
                <EmployeePicker value={v.newPm} onChange={(id) => set({ newPm: id })} />
              </FormField>
              <FormField label={t('project.change.fields.owner')}>
                <EmployeePicker value={v.newOwner} onChange={(id) => set({ newOwner: id })} />
              </FormField>
            </>
          )}
          {(create.error ?? update.error) && (
            <div className="md:col-span-2">
              <FieldErrors error={create.error ?? update.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Q-P9: the PM confirms the values (pre-filled from the request, old → new) before anything changes. */
function ApplyDialog({
  project,
  change: c,
  onClose,
}: {
  project: ProjectFull;
  change: ChangeRequest;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const apply = useProjectMutation(project.id, projectApi.applyChange);
  const lang = i18n.language;
  const [v, setV] = useState({
    nameAr: c.proposedNameAr ?? project.nameAr ?? '',
    nameEn: c.proposedNameEn ?? project.nameEn ?? '',
    scope: c.proposedScope ?? project.scopeOfWork ?? '',
    plannedStart: c.proposedStart ?? project.plannedStart ?? undefined,
    plannedEnd: c.proposedEnd ?? project.plannedEnd ?? undefined,
    budget: String(c.proposedBudget ?? project.budget ?? ''),
    newPm: c.newPm?.id ?? undefined,
    newOwner: c.newOwner?.id ?? undefined,
    methodology: c.proposedMethodology ?? undefined,
    rebaseline: false,
  });
  const { t: ts } = useTranslation('pmStandards');
  const baselinesOn = useFeature('baselines');
  const set = (patch: Partial<typeof v>) => setV((x) => ({ ...x, ...patch }));
  const values: ChangeApplyValues = {
    nameAr: v.nameAr.trim() || null,
    nameEn: v.nameEn.trim() || null,
    description: null,
    scope: v.scope.trim() || null,
    plannedStart: v.plannedStart ?? null,
    plannedEnd: v.plannedEnd ?? null,
    budget: v.budget.trim() === '' ? null : Number(v.budget),
    newPmEmployeeId: v.newPm ?? null,
    newOwnerEmployeeId: v.newOwner ?? null,
    methodology: c.proposedMethodology ? (v.methodology ?? null) : null,
    rebaseline: baselinesOn && isDetailsType(c.type) && v.rebaseline,
  };
  const was = (label: string, old: string) => (
    <p className="text-xs text-muted-foreground">
      {t('project.change.current')}: <bdi>{old || '—'}</bdi>
      <span className="sr-only">{label}</span>
    </p>
  );
  const isDetails = isDetailsType(c.type);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('project.change.apply')} — ${c.number}`}
        description={t('project.change.applyHint')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={apply.isPending}
              onClick={() =>
                apply.mutate(
                  { cid: c.id, version: c.version, values },
                  {
                    onSuccess: () => {
                      toast({ title: t('project.change.applied') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('project.change.confirmApply')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="apply-dialog">
          {isDetails && (
            <>
              <div>
                <FormField label={t('fields.nameAr')}>
                  <Input dir="rtl" value={v.nameAr} onChange={(e) => set({ nameAr: e.target.value })} />
                </FormField>
                {was('nameAr', project.nameAr ?? '')}
              </div>
              <div>
                <FormField label={t('fields.nameEn')}>
                  <Input dir="ltr" value={v.nameEn} onChange={(e) => set({ nameEn: e.target.value })} />
                </FormField>
                {was('nameEn', project.nameEn ?? '')}
              </div>
              <div>
                <FormField label={t('fields.plannedStart')}>
                  <DatePicker value={v.plannedStart} onChange={(d) => set({ plannedStart: d })} />
                </FormField>
                {was('start', project.plannedStart ? formatDate(project.plannedStart, lang) : '')}
              </div>
              <div>
                <FormField label={t('fields.plannedEnd')}>
                  <DatePicker value={v.plannedEnd} onChange={(d) => set({ plannedEnd: d })} />
                </FormField>
                {was('end', project.plannedEnd ? formatDate(project.plannedEnd, lang) : '')}
              </div>
              <div>
                <FormField label={t('project.change.fields.budget')}>
                  <Input
                    dir="ltr"
                    inputMode="decimal"
                    value={v.budget}
                    onChange={(e) => set({ budget: e.target.value })}
                  />
                </FormField>
                {was('budget', project.budget !== null ? formatNumber(project.budget, lang) : '')}
              </div>
              {c.proposedMethodology && (
                <div>
                  <FormField label={ts('change.methodology')}>
                    <Select
                      value={v.methodology}
                      onValueChange={(m) => set({ methodology: m })}
                      options={methodologies.map((m) => ({
                        value: m,
                        label: ts(`change.methodologies.${m}`),
                      }))}
                    />
                  </FormField>
                  {was(
                    'methodology',
                    ts(`change.methodologies.${methodologyCode(project.delivery.methodology)}`),
                  )}
                </div>
              )}
              {c.areas.includes('plan') && (
                <div className="md:col-span-2">
                  <StatusBanner tone="info" title={ts('change.replanOpen')} />
                </div>
              )}
              {baselinesOn && (
                <div className="md:col-span-2">
                  <Checkbox
                    label={ts('change.rebaseline')}
                    description={ts('change.rebaselineHint')}
                    checked={v.rebaseline}
                    onChange={(e) => set({ rebaseline: e.target.checked })}
                    data-testid="apply-rebaseline"
                  />
                </div>
              )}
            </>
          )}
          {c.type === 'ownership' && (
            <>
              <div>
                <FormField label={t('project.change.fields.pm')}>
                  <EmployeePicker value={v.newPm} onChange={(id) => set({ newPm: id })} />
                </FormField>
                {was('pm', project.pm ? pickText(lang, project.pm.nameAr, project.pm.nameEn) : '')}
              </div>
              <div>
                <FormField label={t('project.change.fields.owner')}>
                  <EmployeePicker value={v.newOwner} onChange={(id) => set({ newOwner: id })} />
                </FormField>
                {was(
                  'owner',
                  project.owner ? pickText(lang, project.owner.nameAr, project.owner.nameEn) : '',
                )}
              </div>
            </>
          )}
          {(c.type === 'cancellation' || c.type === 'merge') && (
            <div className="md:col-span-2">
              <StatusBanner tone="warning" title={t('project.change.cancelsProject')} />
            </div>
          )}
          {apply.isError && (
            <div className="md:col-span-2">
              <FieldErrors error={apply.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CancelChangeDialog({
  project,
  change,
  onClose,
}: {
  project: ProjectFull;
  change: ChangeRequest;
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const cancel = useProjectMutation(project.id, projectApi.cancelChange);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('actions.cancel')} — ${change.number}`}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={reason.trim() === ''}
              loading={cancel.isPending}
              onClick={() =>
                cancel.mutate({ cid: change.id, version: change.version, reason }, { onSuccess: onClose })
              }
            >
              {t('actions.cancel')}
            </Button>
          </>
        }
      >
        <FormField label={t('project.reason')} required>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {cancel.isError && <FieldErrors error={cancel.error} />}
      </DialogContent>
    </Dialog>
  );
}

/** Closure form (W5): summary, main deliverables and the lessons learned; submit for approval. */
export function ClosureTab({
  project,
  options,
  onSubmit,
}: {
  project: ProjectFull;
  options: PortfolioOptions;
  onSubmit: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const toast = useToast();
  const save = useProjectMutation(project.id, projectApi.closure);
  const [summary, setSummary] = useState(project.summaryOfAchievement ?? '');
  const [deliverables, setDeliverables] = useState(project.mainDeliverables ?? '');
  const editable = project.permissions.canEdit && project.subState === 'closure-draft';
  return (
    <div className="flex flex-col gap-6">
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-6" data-testid="project-closure">
        <h2 className="font-semibold">{t('project.closure.title')}</h2>
        <FormField label={t('project.closure.summary')} required>
          <Textarea disabled={!editable} value={summary} onChange={(e) => setSummary(e.target.value)} />
        </FormField>
        <FormField label={t('project.closure.deliverables')} required>
          <Textarea
            disabled={!editable}
            value={deliverables}
            onChange={(e) => setDeliverables(e.target.value)}
          />
        </FormField>
        {editable && (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: project.id,
                    version: project.version,
                    summaryOfAchievement: summary.trim() || null,
                    mainDeliverables: deliverables.trim() || null,
                  },
                  { onSuccess: () => toast({ title: t('project.saved') }) },
                )
              }
            >
              {t('actions.saveDraft')}
            </Button>
            <Button onClick={onSubmit}>{t('project.closure.submit')}</Button>
          </div>
        )}
        {save.isError && <FieldErrors error={save.error} />}
      </section>
      <LessonsTab project={project} options={options} />
    </div>
  );
}

/** Impact (W6): cost performance, community / employee / vendor satisfaction; PM Office approves → Completed. */
export function ImpactTab({ project, onSubmit }: { project: ProjectFull; onSubmit: () => void }) {
  const { t } = useTranslation('portfolio');
  const toast = useToast();
  const save = useProjectMutation(project.id, projectApi.impact);
  const [v, setV] = useState({
    cost: project.costPerformance !== null ? String(project.costPerformance) : '',
    community: project.communitySatisfaction !== null ? String(project.communitySatisfaction) : '',
    employee: project.employeeSatisfaction !== null ? String(project.employeeSatisfaction) : '',
    vendor: project.vendorSatisfaction !== null ? String(project.vendorSatisfaction) : '',
    previous: project.previousOperationalCost !== null ? String(project.previousOperationalCost) : '',
    notes: project.impactNotes ?? '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const editable = project.permissions.canEdit && project.subState === 'impact-draft';
  const scale = ['1', '2', '3', '4', '5'].map((s) => ({
    value: s,
    label: t(`project.impact.scale.${s as '1'}`),
  }));
  const n = (s: string) => (s.trim() === '' ? null : Number(s));
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-6" data-testid="project-impact">
      <h2 className="font-semibold">{t('project.impact.title')}</h2>
      <p className="text-sm text-muted-foreground">{t('project.impact.hint')}</p>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('project.impact.cost')} hint={t('project.impact.costHint')} required>
          <Input
            dir="ltr"
            disabled={!editable}
            inputMode="decimal"
            value={v.cost}
            onChange={(e) => set({ cost: e.target.value })}
            data-testid="impact-cost"
          />
        </FormField>
        <FormField label={t('project.impact.previousCost')}>
          <Input
            dir="ltr"
            disabled={!editable}
            inputMode="decimal"
            value={v.previous}
            onChange={(e) => set({ previous: e.target.value })}
          />
        </FormField>
        {(['community', 'employee', 'vendor'] as const).map((k) => (
          <FormField key={k} label={t(`project.impact.${k}`)} required>
            <Select
              disabled={!editable}
              value={v[k] || undefined}
              onValueChange={(value) => set({ [k]: value })}
              options={scale}
            />
          </FormField>
        ))}
        <FormField label={t('project.impact.notes')} className="md:col-span-2">
          <Textarea disabled={!editable} value={v.notes} onChange={(e) => set({ notes: e.target.value })} />
        </FormField>
      </div>
      {editable && (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            loading={save.isPending}
            onClick={() =>
              save.mutate(
                {
                  id: project.id,
                  version: project.version,
                  costPerformance: n(v.cost),
                  communitySatisfaction: n(v.community),
                  employeeSatisfaction: n(v.employee),
                  vendorSatisfaction: n(v.vendor),
                  previousOperationalCost: n(v.previous),
                  impactNotes: v.notes.trim() || null,
                },
                { onSuccess: () => toast({ title: t('project.saved') }) },
              )
            }
          >
            {t('actions.saveDraft')}
          </Button>
          <Button onClick={onSubmit}>{t('project.impact.submit')}</Button>
        </div>
      )}
      {save.isError && <FieldErrors error={save.error} />}
    </section>
  );
}
