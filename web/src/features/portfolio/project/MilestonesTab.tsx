import { Pencil, Plus, TrendingUp, Upload, XCircle } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { contentUrl } from '@/features/files/api';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FileDrop,
  FormField,
  Input,
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
import { translateMessageKey } from '@/api/problem';
import {
  fromPercent,
  isDelayed,
  milestoneTree,
  projectApi,
  toPercent,
  useEvidence,
  useMilestones,
  useProjectMutation,
  weightTotals,
  type Milestone,
  type MilestoneValues,
  type ProjectFull,
} from '../projectApi';
import { FieldErrors } from './FieldErrors';

type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';
const statusTone: Record<string, Tone> = {
  'not-started': 'neutral',
  active: 'info',
  completed: 'success',
  cancelled: 'neutral',
};
const updateTone: Record<string, Tone> = {
  'waiting-update': 'neutral',
  draft: 'info',
  'pending-approval': 'warning',
  approved: 'success',
  returned: 'danger',
};

/** Milestones & phases (SPEC §1.4 C): high-level plan and detailed plan, weights to 100 %, monthly updates while the window is open. */
export function MilestonesTab({
  project,
  canUpdate,
  today,
}: {
  project: ProjectFull;
  canUpdate: boolean;
  today: string;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const plan = useMilestones(project.id);
  const [editing, setEditing] = useState<{ row?: Milestone; highLevel: boolean } | null>(null);
  const [updating, setUpdating] = useState<Milestone | null>(null);
  const [cancelling, setCancelling] = useState<Milestone | null>(null);
  const lang = i18n.language;
  if (plan.isPending) return <Skeleton className="h-64" />;
  if (!plan.data) return null;
  const rows = plan.data.milestones;
  const totals = weightTotals(rows);
  const canPlan = project.permissions.canPlanMilestones;
  const pct = (ratio: number) => `${formatNumber(toPercent(ratio) ?? 0, lang)} %`;
  const section = (highLevel: boolean) => {
    const own = rows.filter((m) => m.highLevel === highLevel);
    const total = highLevel ? totals.highLevel : totals.detailed;
    return (
      <section className="flex flex-col gap-3" data-testid={highLevel ? 'plan-high-level' : 'plan-detailed'}>
        <header className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="font-semibold">
              {highLevel ? t('project.milestones.highLevel') : t('project.milestones.detailed')}
            </h2>
            <p className="text-sm text-muted-foreground">
              {highLevel ? t('project.milestones.highLevelHint') : t('project.milestones.detailedHint')}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <span data-testid={highLevel ? 'weight-high-level' : 'weight-detailed'}>
              <StatusChip tone={Math.abs(total - 1) < 0.00005 ? 'success' : 'warning'}>
                {t('project.milestones.total', { value: pct(total) })}
              </StatusChip>
            </span>
            {canPlan && (
              <Button size="sm" onClick={() => setEditing({ highLevel })}>
                <Plus aria-hidden />
                {t('project.milestones.add')}
              </Button>
            )}
          </div>
        </header>
        {own.length === 0 ? (
          <EmptyState title={t('project.milestones.empty')} />
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('project.milestones.code')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.milestones.title')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.milestones.dates')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.milestones.weight')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.milestones.achieved')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.milestones.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('project.actionsColumn')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {milestoneTree(own).map(({ row: m, depth }) => (
                  <TableRow key={m.id} data-testid="milestone-row">
                    <TableCell>
                      <bdi className={depth ? 'ps-4' : 'font-medium'}>{m.code}</bdi>
                    </TableCell>
                    <TableCell
                      dir="auto"
                      className={depth ? 'ps-6' : m.isHeader ? 'font-semibold' : undefined}
                    >
                      {pickText(lang, m.titleAr, m.titleEn)}
                      {m.isDeliverable && (
                        <StatusChip tone="info" className="ms-2">
                          {t('project.milestones.deliverable')}
                        </StatusChip>
                      )}
                    </TableCell>
                    <TableCell className="text-sm">
                      {m.plannedStart ? formatDate(m.plannedStart, lang) : '—'} –{' '}
                      {m.plannedEnd ? formatDate(m.plannedEnd, lang) : '—'}
                      {m.actualEnd && (
                        <span className="block text-xs text-muted-foreground">
                          {t('project.milestones.actualEnd')}: {formatDate(m.actualEnd, lang)}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">{pct(m.weight)}</TableCell>
                    <TableCell className="tabular-nums">
                      {pct(m.maxAchieved)}
                      {m.pendingAchieve !== null && (
                        <span className="block text-xs text-muted-foreground">
                          {t('project.milestones.pending', { value: pct(m.pendingAchieve) })}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        <StatusChip tone={statusTone[m.status] ?? 'neutral'}>
                          {t(`project.milestones.statuses.${m.status as 'active'}`)}
                        </StatusChip>
                        {!m.isHeader && !m.highLevel && (
                          <StatusChip tone={updateTone[m.updateState] ?? 'neutral'}>
                            {t(`project.milestones.updateStates.${m.updateState as 'draft'}`)}
                          </StatusChip>
                        )}
                        {m.delayed && <StatusChip tone="danger">{t('project.progress.delayed')}</StatusChip>}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-1">
                        {canPlan && m.status !== 'cancelled' && (
                          <>
                            <Button
                              size="sm"
                              variant="ghost"
                              aria-label={t('project.milestones.edit')}
                              onClick={() => setEditing({ row: m, highLevel })}
                            >
                              <Pencil aria-hidden />
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              aria-label={t('project.milestones.cancel')}
                              onClick={() => setCancelling(m)}
                            >
                              <XCircle aria-hidden />
                            </Button>
                          </>
                        )}
                        {canUpdate &&
                          !m.isHeader &&
                          !m.highLevel &&
                          m.status !== 'cancelled' &&
                          m.updateState !== 'pending-approval' &&
                          m.maxAchieved < m.weight && (
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() => setUpdating(m)}
                              data-testid="milestone-update"
                            >
                              <TrendingUp aria-hidden />
                              {t('project.milestones.update')}
                            </Button>
                          )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>
    );
  };
  return (
    <div className="flex flex-col gap-8">
      {project.template.charter && section(true)}
      {section(false)}
      {editing && (
        <MilestoneDialog
          project={project}
          rows={rows}
          row={editing.row}
          highLevel={editing.highLevel}
          onClose={() => setEditing(null)}
        />
      )}
      {updating && (
        <ProgressDialog
          project={project}
          milestone={updating}
          today={today}
          onClose={() => setUpdating(null)}
        />
      )}
      {cancelling && (
        <CancelMilestoneDialog project={project} milestone={cancelling} onClose={() => setCancelling(null)} />
      )}
    </div>
  );
}

function MilestoneDialog({
  project,
  rows,
  row,
  highLevel,
  onClose,
}: {
  project: ProjectFull;
  rows: Milestone[];
  row: Milestone | undefined;
  highLevel: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const create = useProjectMutation(project.id, projectApi.createMilestone);
  const update = useProjectMutation(project.id, projectApi.updateMilestone);
  const [v, setV] = useState({
    titleAr: row?.titleAr ?? '',
    titleEn: row?.titleEn ?? '',
    isHeader: row?.isHeader ?? false,
    parentId: row?.parentId ?? '',
    plannedStart: row?.plannedStart ?? undefined,
    plannedEnd: row?.plannedEnd ?? undefined,
    weight: row ? String(toPercent(row.weight) ?? '') : '',
    cost: row?.cost !== null && row?.cost !== undefined ? String(row.cost) : '',
    isDeliverable: row?.isDeliverable ?? false,
    mainDeliverable: row?.mainDeliverable ?? '',
    acceptanceCriteria: row?.acceptanceCriteria ?? '',
    qualityMet: row?.qualityMet ?? null,
    isInnovative: row?.isInnovative ?? false,
    innovativeReason: row?.innovativeReason ?? '',
  });
  const phases = rows.filter(
    (m) => m.isHeader && m.highLevel === highLevel && m.status !== 'cancelled' && m.id !== row?.id,
  );
  const values: MilestoneValues = {
    titleAr: v.titleAr.trim() || null,
    titleEn: v.titleEn.trim() || null,
    parentId: v.isHeader || !v.parentId ? null : v.parentId,
    isHeader: v.isHeader,
    highLevel,
    plannedStart: v.plannedStart ?? null,
    plannedEnd: v.plannedEnd ?? null,
    weight: v.isHeader ? 0 : (fromPercent(v.weight) ?? 0),
    cost: v.cost.trim() === '' ? null : Number(v.cost),
    isDeliverable: v.isDeliverable,
    mainDeliverable: v.mainDeliverable.trim() || null,
    acceptanceCriteria: v.acceptanceCriteria.trim() || null,
    qualityMet: v.qualityMet,
    isInnovative: v.isInnovative,
    innovativeReason: v.innovativeReason.trim() || null,
    sort: row?.sort ?? 0,
  };
  const error = create.error ?? update.error;
  const save = () => {
    const done = {
      onSuccess: () => {
        toast({ title: t('project.saved') });
        onClose();
      },
    };
    if (row) update.mutate({ mid: row.id, values, version: row.version }, done);
    else create.mutate({ id: project.id, values }, done);
  };
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? t('project.milestones.edit') : t('project.milestones.add')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending || update.isPending} onClick={save}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="milestone-dialog">
          <FormField label={t('fields.nameAr')}>
            <Input dir="rtl" value={v.titleAr} onChange={(e) => set({ titleAr: e.target.value })} />
          </FormField>
          <FormField label={t('fields.nameEn')}>
            <Input dir="ltr" value={v.titleEn} onChange={(e) => set({ titleEn: e.target.value })} />
          </FormField>
          <div className="md:col-span-2">
            <Checkbox
              label={t('project.milestones.isPhase')}
              checked={v.isHeader}
              onChange={(e) => set({ isHeader: e.target.checked })}
            />
          </div>
          {!v.isHeader && phases.length > 0 && (
            <FormField label={t('project.milestones.phase')}>
              <Select
                value={v.parentId || '*'}
                onValueChange={(value) => set({ parentId: value === '*' ? '' : value })}
                options={[
                  { value: '*', label: t('project.milestones.noPhase') },
                  ...phases.map((p) => ({
                    value: p.id,
                    label: `${p.code} · ${pickText(i18n.language, p.titleAr, p.titleEn)}`,
                  })),
                ]}
              />
            </FormField>
          )}
          {!v.isHeader && (
            <FormField label={t('project.milestones.weightPercent')} required>
              <Input
                dir="ltr"
                inputMode="decimal"
                value={v.weight}
                onChange={(e) => set({ weight: e.target.value })}
                data-testid="milestone-weight"
              />
            </FormField>
          )}
          <FormField label={t('fields.plannedStart')} required={!v.isHeader}>
            <DatePicker value={v.plannedStart} onChange={(d) => set({ plannedStart: d })} />
          </FormField>
          <FormField label={t('fields.plannedEnd')} required={!v.isHeader}>
            <DatePicker value={v.plannedEnd} onChange={(d) => set({ plannedEnd: d })} />
          </FormField>
          <FormField label={t('project.milestones.cost')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={v.cost}
              onChange={(e) => set({ cost: e.target.value })}
            />
          </FormField>
          <div className="md:col-span-2 flex flex-wrap gap-4">
            <Checkbox
              label={t('project.milestones.isDeliverable')}
              checked={v.isDeliverable}
              onChange={(e) => set({ isDeliverable: e.target.checked })}
            />
            <Checkbox
              label={t('project.milestones.isInnovative')}
              checked={v.isInnovative}
              onChange={(e) => set({ isInnovative: e.target.checked })}
            />
          </div>
          {v.isDeliverable && (
            <>
              <FormField label={t('project.milestones.mainDeliverable')} required>
                <Textarea
                  value={v.mainDeliverable}
                  onChange={(e) => set({ mainDeliverable: e.target.value })}
                />
              </FormField>
              <FormField label={t('project.milestones.acceptance')} required>
                <Textarea
                  value={v.acceptanceCriteria}
                  onChange={(e) => set({ acceptanceCriteria: e.target.value })}
                />
              </FormField>
              <Checkbox
                label={t('project.milestones.qualityMet')}
                checked={v.qualityMet === true}
                onChange={(e) => set({ qualityMet: e.target.checked })}
              />
            </>
          )}
          {v.isInnovative && (
            <FormField label={t('project.milestones.innovativeReason')} required className="md:col-span-2">
              <Textarea
                value={v.innovativeReason}
                onChange={(e) => set({ innovativeReason: e.target.value })}
              />
            </FormField>
          )}
          {error && (
            <div className="md:col-span-2">
              <FieldErrors error={error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** The monthly values of one milestone (ADR 0071): achievement, actual dates, evidence at 100 %, causality / corrective actions when delayed. */
function ProgressDialog({
  project,
  milestone,
  today,
  onClose,
}: {
  project: ProjectFull;
  milestone: Milestone;
  today: string;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, projectApi.progress);
  const upload = useProjectMutation(project.id, projectApi.evidence);
  const evidence = useEvidence(milestone.id);
  const [file, setFile] = useState<File | null>(null);
  const [v, setV] = useState({
    achieve: String(toPercent(milestone.pendingAchieve ?? milestone.maxAchieved) ?? 0),
    actualStart: milestone.pendingActualStart ?? milestone.actualStart ?? undefined,
    actualEnd: milestone.pendingActualEnd ?? milestone.actualEnd ?? undefined,
    causality: milestone.causality ?? '',
    correctiveActions: milestone.correctiveActions ?? '',
    evidenceNote: milestone.evidenceNote ?? '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const achieve = fromPercent(v.achieve) ?? 0;
  const complete = milestone.weight > 0 && Math.abs(achieve - milestone.weight) < 0.00005;
  const delayed = isDelayed(achieve, milestone.weight, milestone.plannedEnd, today);
  const lang = i18n.language;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('project.milestones.update')} — ${milestone.code}`}
        description={pickText(lang, milestone.titleAr, milestone.titleEn)}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    mid: milestone.id,
                    version: milestone.version,
                    values: {
                      achieve,
                      actualStart: v.actualStart ?? null,
                      actualEnd: v.actualEnd ?? null,
                      causality: v.causality.trim() || null,
                      correctiveActions: v.correctiveActions.trim() || null,
                      evidenceNote: v.evidenceNote.trim() || null,
                    },
                  },
                  {
                    onSuccess: (result) => {
                      for (const w of result.warnings)
                        toast({ tone: 'error', title: translateMessageKey(tc, w) });
                      toast({ title: t('project.saved') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="progress-dialog">
          <FormField
            label={t('project.milestones.achievePercent')}
            hint={t('project.milestones.achieveHint', {
              weight: formatNumber(toPercent(milestone.weight) ?? 0, lang),
              approved: formatNumber(toPercent(milestone.maxAchieved) ?? 0, lang),
            })}
          >
            <Input
              dir="ltr"
              inputMode="decimal"
              value={v.achieve}
              onChange={(e) => set({ achieve: e.target.value })}
              data-testid="progress-achieve"
            />
          </FormField>
          <div className="flex items-end gap-2 text-sm">
            {delayed && <StatusChip tone="danger">{t('project.progress.delayed')}</StatusChip>}
            {complete && <StatusChip tone="success">{t('project.milestones.completeHint')}</StatusChip>}
          </div>
          <FormField label={t('project.milestones.actualStart')} required={achieve > 0}>
            <DatePicker value={v.actualStart} onChange={(d) => set({ actualStart: d })} />
          </FormField>
          <FormField label={t('project.milestones.actualEnd')} required={complete}>
            <DatePicker value={v.actualEnd} onChange={(d) => set({ actualEnd: d })} />
          </FormField>
          {(delayed || v.causality || v.correctiveActions) && (
            <>
              <FormField
                label={t('project.milestones.causality')}
                required={delayed}
                className="md:col-span-2"
              >
                <Textarea
                  value={v.causality}
                  onChange={(e) => set({ causality: e.target.value })}
                  data-testid="progress-causality"
                />
              </FormField>
              <FormField
                label={t('project.milestones.corrective')}
                required={delayed}
                className="md:col-span-2"
              >
                <Textarea
                  value={v.correctiveActions}
                  onChange={(e) => set({ correctiveActions: e.target.value })}
                  data-testid="progress-corrective"
                />
              </FormField>
            </>
          )}
          <section
            className="md:col-span-2 flex flex-col gap-2 rounded-md border p-3"
            data-testid="progress-evidence"
          >
            <h3 className="text-sm font-medium">
              {t('project.milestones.evidence')} {complete && <span className="text-danger">*</span>}
            </h3>
            <ul className="text-sm">
              {(evidence.data ?? []).map((f) => (
                <li key={f.id}>
                  <a className="text-primary hover:underline" href={contentUrl(f.id, undefined, true)}>
                    <bdi>{f.name}</bdi>
                  </a>
                </li>
              ))}
            </ul>
            <FileDrop
              label={t('project.milestones.evidenceFile')}
              file={file}
              onFile={setFile}
              accept=".pdf,.docx,.xlsx,.png,.jpg,.jpeg"
            />
            <Button
              size="sm"
              variant="outline"
              disabled={!file}
              loading={upload.isPending}
              onClick={() =>
                file &&
                upload.mutate(
                  { mid: milestone.id, file },
                  {
                    onSuccess: () => {
                      setFile(null);
                      void evidence.refetch();
                      toast({ title: t('toast.uploaded') });
                    },
                  },
                )
              }
            >
              <Upload aria-hidden />
              {t('project.milestones.uploadEvidence')}
            </Button>
            <FormField label={t('project.milestones.evidenceNote')}>
              <Input
                dir="auto"
                value={v.evidenceNote}
                onChange={(e) => set({ evidenceNote: e.target.value })}
              />
            </FormField>
          </section>
          {save.isError && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
          {upload.isError && (
            <div className="md:col-span-2">
              <StatusBanner tone="danger" title={translateMessageKey(tc, 'errors.unexpected')} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CancelMilestoneDialog({
  project,
  milestone,
  onClose,
}: {
  project: ProjectFull;
  milestone: Milestone;
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const cancel = useProjectMutation(project.id, projectApi.cancelMilestone);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('project.milestones.cancel')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              loading={cancel.isPending}
              onClick={() =>
                cancel.mutate(
                  { mid: milestone.id, version: milestone.version, reason },
                  { onSuccess: onClose },
                )
              }
            >
              {t('project.milestones.cancel')}
            </Button>
          </>
        }
      >
        <FormField label={t('project.reason')}>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {cancel.isError && <FieldErrors error={cancel.error} />}
      </DialogContent>
    </Dialog>
  );
}
