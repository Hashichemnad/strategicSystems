import { Check, Pencil, Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { cn } from '@/lib/cn';
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
  type SelectOption,
} from '@/ui';
import { useFeature } from '../featuresApi';
import { useMilestones, useProjectMutation, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  standardsApi,
  useProjectSearch,
  useRaid,
  useRiskMatrix,
  type RaidItem,
  type RaidLog,
  type RaidValues,
} from './standardsApi';
import { matrixGrid, riskBandTone, type RiskBand, type Tone } from './standardsLogic';

const raidTypes = ['issue', 'assumption', 'dependency', 'decision'] as const;
type RaidType = (typeof raidTypes)[number];
const priorities = ['high', 'medium', 'low'] as const;
const statuses = ['proposed', 'open', 'resolved', 'closed'] as const;

const statusTone: Record<string, Tone> = {
  proposed: 'warning',
  open: 'info',
  resolved: 'success',
  closed: 'neutral',
};
const priorityTone: Record<string, Tone> = { high: 'danger', medium: 'warning', low: 'neutral' };

const bandCell: Record<RiskBand, string> = {
  low: 'bg-success-soft text-success',
  medium: 'bg-info-soft text-info',
  high: 'bg-warning-soft text-warning',
  critical: 'bg-danger-soft text-danger',
};

const asStatus = (s: string) => ((statuses as readonly string[]).includes(s) ? (s as 'open') : 'open');
const asPriority = (p: string) =>
  (priorities as readonly string[]).includes(p) ? (p as 'medium') : 'medium';
const asBand = (b: string): RiskBand =>
  b === 'low' || b === 'medium' || b === 'high' || b === 'critical' ? b : 'low';

/** RAID log (docs/design/portfolio.md §35): risks (read), issues, assumptions, dependencies, decisions; proposals accepted by the PM. */
export function RaidTab({ project }: { project: ProjectFull }) {
  const { t } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const matrixOn = useFeature('riskMatrix');
  const raid = useRaid(project.id);
  const [editing, setEditing] = useState<{ type: RaidType; row?: RaidItem } | null>(null);
  return (
    <div className="flex flex-col gap-8" data-testid="raid-log">
      <header>
        <h2 className="font-semibold">{t('raid.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('raid.hint')}</p>
      </header>
      {matrixOn && <RiskMatrixPanel project={project} />}
      {raid.isPending ? (
        <Skeleton className="h-40" />
      ) : raid.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, raid.error)} />
      ) : (
        <>
          <RisksSection log={raid.data} />
          {raidTypes.map((type) => (
            <RaidSection
              key={type}
              project={project}
              type={type}
              log={raid.data}
              onAdd={() => setEditing({ type })}
              onEdit={(row) => setEditing({ type, row })}
            />
          ))}
          {editing && (
            <RaidDialog
              project={project}
              type={editing.type}
              row={editing.row}
              canEdit={raid.data.canEdit}
              onClose={() => setEditing(null)}
            />
          )}
        </>
      )}
    </div>
  );
}

function RisksSection({ log }: { log: RaidLog }) {
  const { t, i18n } = useTranslation('pmStandards');
  const lang = i18n.language;
  return (
    <section className="flex flex-col gap-3" data-testid="raid-risks">
      <header>
        <h3 className="font-semibold">{t('raid.sections.risk')}</h3>
        <p className="text-sm text-muted-foreground">{t('raid.risksHint')}</p>
      </header>
      {log.risks.length === 0 ? (
        <EmptyState title={t('raid.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('raid.fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('matrix.pxi')}</TableHeaderCell>
                <TableHeaderCell>{t('matrix.residual')}</TableHeaderCell>
                <TableHeaderCell>{t('raid.fields.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('matrix.reviewDate')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {log.risks.map((r) => (
                <TableRow key={r.id}>
                  <TableCell dir="auto">{r.title ?? '—'}</TableCell>
                  <TableCell className="tabular-nums">
                    {formatNumber(r.probability, lang)} × {formatNumber(r.impact, lang)}
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={riskBandTone[asBand(r.residualBand)]}>
                      {t(`bands.${asBand(r.residualBand)}`)}
                    </StatusChip>
                  </TableCell>
                  <TableCell>{r.owner ? pickText(lang, r.owner.nameAr, r.owner.nameEn) : '—'}</TableCell>
                  <TableCell>{r.reviewDate ? formatDate(r.reviewDate, lang) : '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}

function RaidSection({
  project,
  type,
  log,
  onAdd,
  onEdit,
}: {
  project: ProjectFull;
  type: RaidType;
  log: RaidLog;
  onAdd: () => void;
  onEdit: (row: RaidItem) => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const accept = useProjectMutation(project.id, standardsApi.acceptRaid);
  const milestones = useMilestones(project.id);
  const lang = i18n.language;
  const rows = log.items.filter((i) => i.type === type);
  const milestoneLabel = (id: string | null) => {
    const m = (milestones.data?.milestones ?? []).find((x) => x.id === id);
    return m ? `${m.code} · ${pickText(lang, m.titleAr, m.titleEn)}` : null;
  };
  return (
    <section className="flex flex-col gap-3" data-testid={`raid-${type}`}>
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">{t(`raid.sections.${type}`)}</h3>
        {(log.canEdit || log.canPropose) && project.status === 'active' && (
          <Button size="sm" onClick={onAdd} data-testid={`raid-add-${type}`}>
            <Plus aria-hidden />
            {log.canEdit ? t(`raid.add.${type}`) : t('raid.propose')}
          </Button>
        )}
      </header>
      {accept.isError && <FieldErrors error={accept.error} />}
      {rows.length === 0 ? (
        <EmptyState title={t('raid.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('raid.fields.code')}</TableHeaderCell>
                <TableHeaderCell>{t('raid.fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('raid.fields.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('raid.fields.priority')}</TableHeaderCell>
                <TableHeaderCell>
                  {type === 'decision' ? t('raid.fields.decidedOn') : t('raid.fields.dueDate')}
                </TableHeaderCell>
                <TableHeaderCell>{t('raid.fields.status')}</TableHeaderCell>
                {type === 'dependency' && <TableHeaderCell>{t('raid.fields.dependsOn')}</TableHeaderCell>}
                {type === 'decision' && <TableHeaderCell>{t('raid.fields.decidedBy')}</TableHeaderCell>}
                <TableHeaderCell>{t('actionsColumn')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((r) => (
                <TableRow key={r.id} data-testid="raid-row">
                  <TableCell>
                    <bdi className="font-medium">{r.code}</bdi>
                  </TableCell>
                  <TableCell dir="auto">
                    {r.title ?? '—'}
                    {r.description && (
                      <span className="block whitespace-pre-line text-xs text-muted-foreground">
                        {r.description}
                      </span>
                    )}
                    {r.milestoneId && milestoneLabel(r.milestoneId) && (
                      <span className="block text-xs text-muted-foreground">
                        {t('raid.fields.milestone')}: <bdi>{milestoneLabel(r.milestoneId)}</bdi>
                      </span>
                    )}
                  </TableCell>
                  <TableCell>{r.owner ? pickText(lang, r.owner.nameAr, r.owner.nameEn) : '—'}</TableCell>
                  <TableCell>
                    <StatusChip tone={priorityTone[r.priority] ?? 'neutral'}>
                      {t(`raid.priorities.${asPriority(r.priority)}`)}
                    </StatusChip>
                  </TableCell>
                  <TableCell>
                    {type === 'decision'
                      ? r.decidedOn
                        ? formatDate(r.decidedOn, lang)
                        : '—'
                      : r.dueDate
                        ? formatDate(r.dueDate, lang)
                        : '—'}
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={statusTone[r.status] ?? 'neutral'}>
                      {t(`raid.statuses.${asStatus(r.status)}`)}
                    </StatusChip>
                  </TableCell>
                  {type === 'dependency' && (
                    <TableCell>
                      {r.dependsOnProjectNumber ? (
                        <bdi>{r.dependsOnProjectNumber}</bdi>
                      ) : (
                        t('raid.thisProject')
                      )}
                    </TableCell>
                  )}
                  {type === 'decision' && <TableCell dir="auto">{r.decidedBy ?? '—'}</TableCell>}
                  <TableCell>
                    <div className="flex gap-1">
                      {log.canEdit && (
                        <Button
                          size="sm"
                          variant="ghost"
                          aria-label={tc('actions.edit')}
                          onClick={() => onEdit(r)}
                        >
                          <Pencil aria-hidden />
                        </Button>
                      )}
                      {log.canEdit && r.status === 'proposed' && (
                        <Button
                          size="sm"
                          variant="outline"
                          loading={accept.isPending}
                          data-testid="raid-accept"
                          onClick={() =>
                            accept.mutate(
                              { id: project.id, itemId: r.id },
                              { onSuccess: () => toast({ title: t('raid.accepted') }) },
                            )
                          }
                        >
                          <Check aria-hidden />
                          {t('raid.accept')}
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
}

function RaidDialog({
  project,
  type,
  row,
  canEdit,
  onClose,
}: {
  project: ProjectFull;
  type: RaidType;
  row: RaidItem | undefined;
  canEdit: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.saveRaid);
  const lang = i18n.language;
  const [v, setV] = useState({
    title: row?.title ?? '',
    description: row?.description ?? '',
    owner: row?.owner?.id ?? undefined,
    priority: row?.priority ?? 'medium',
    dueDate: row?.dueDate ?? undefined,
    status: row?.status ?? (canEdit ? 'open' : 'proposed'),
    resolution: row?.resolution ?? '',
    milestoneId: row?.milestoneId ?? '',
    dependsOnProjectId: row?.dependsOnProjectId ?? '',
    dependsOnMilestoneId: row?.dependsOnMilestoneId ?? '',
    decidedOn: row?.decidedOn ?? undefined,
    decidedBy: row?.decidedBy ?? '',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const own = useMilestones(project.id);
  const other = useMilestones(v.dependsOnProjectId || project.id);
  const milestoneOptions = (rows: typeof own.data): SelectOption[] => [
    { value: '*', label: t('raid.none') },
    ...(rows?.milestones ?? [])
      .filter((m) => !m.isHeader && m.status !== 'cancelled')
      .map((m) => ({ value: m.id, label: `${m.code} · ${pickText(lang, m.titleAr, m.titleEn)}` })),
  ];
  const values: RaidValues = {
    type,
    title: v.title.trim() || null,
    description: v.description.trim() || null,
    ownerEmployeeId: v.owner ?? null,
    priority: v.priority,
    dueDate: type === 'decision' ? null : (v.dueDate ?? null),
    status: row ? v.status : null,
    resolution: v.resolution.trim() || null,
    milestoneId: v.milestoneId || null,
    dependsOnProjectId: type === 'dependency' ? v.dependsOnProjectId || null : null,
    dependsOnMilestoneId: type === 'dependency' ? v.dependsOnMilestoneId || null : null,
    decidedOn: type === 'decision' ? (v.decidedOn ?? null) : null,
    decidedBy: type === 'decision' ? v.decidedBy.trim() || null : null,
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? `${row.code} — ${t(`raid.types.${type}`)}` : t(`raid.add.${type}`)}
        description={!canEdit ? t('raid.proposeHint') : undefined}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.title.trim() === ''}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: project.id, itemId: row?.id, values, version: row?.version },
                  {
                    onSuccess: () => {
                      toast({ title: t('saved') });
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
        <div className="grid gap-3 md:grid-cols-2" data-testid="raid-dialog">
          <FormField label={t('raid.fields.title')} required className="md:col-span-2">
            <Input dir="auto" value={v.title} onChange={(e) => set({ title: e.target.value })} />
          </FormField>
          <FormField label={t('raid.fields.description')} className="md:col-span-2">
            <Textarea value={v.description} onChange={(e) => set({ description: e.target.value })} />
          </FormField>
          <FormField label={t('raid.fields.owner')}>
            <EmployeePicker value={v.owner} onChange={(id) => set({ owner: id })} />
          </FormField>
          <FormField label={t('raid.fields.priority')}>
            <Select
              value={v.priority}
              onValueChange={(priority) => set({ priority })}
              options={priorities.map((p) => ({ value: p, label: t(`raid.priorities.${p}`) }))}
            />
          </FormField>
          {type === 'decision' ? (
            <>
              <FormField label={t('raid.fields.decidedOn')}>
                <DatePicker value={v.decidedOn} onChange={(d) => set({ decidedOn: d })} />
              </FormField>
              <FormField label={t('raid.fields.decidedBy')}>
                <Input dir="auto" value={v.decidedBy} onChange={(e) => set({ decidedBy: e.target.value })} />
              </FormField>
            </>
          ) : (
            <FormField label={t('raid.fields.dueDate')}>
              <DatePicker value={v.dueDate} onChange={(d) => set({ dueDate: d })} />
            </FormField>
          )}
          {row && canEdit && (
            <FormField label={t('raid.fields.status')}>
              <Select
                value={v.status}
                onValueChange={(status) => set({ status })}
                options={statuses.map((s) => ({ value: s, label: t(`raid.statuses.${s}`) }))}
              />
            </FormField>
          )}
          <FormField label={t('raid.fields.milestone')}>
            <Select
              value={v.milestoneId || '*'}
              onValueChange={(id) => set({ milestoneId: id === '*' ? '' : id })}
              options={milestoneOptions(own.data)}
            />
          </FormField>
          {type === 'dependency' && (
            <>
              <ProjectPicker
                excludeId={project.id}
                value={v.dependsOnProjectId}
                initialLabel={row?.dependsOnProjectNumber ?? null}
                onChange={(id) => set({ dependsOnProjectId: id, dependsOnMilestoneId: '' })}
              />
              <FormField label={t('raid.fields.dependsOnMilestone')}>
                <Select
                  value={v.dependsOnMilestoneId || '*'}
                  onValueChange={(id) => set({ dependsOnMilestoneId: id === '*' ? '' : id })}
                  options={milestoneOptions(other.data)}
                />
              </FormField>
            </>
          )}
          {(type === 'issue' || row) && (
            <FormField label={t('raid.fields.resolution')} className="md:col-span-2">
              <Textarea value={v.resolution} onChange={(e) => set({ resolution: e.target.value })} />
            </FormField>
          )}
          {save.isError && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Another project the user may read (empty = a dependency inside this project). */
function ProjectPicker({
  excludeId,
  value,
  initialLabel,
  onChange,
}: {
  excludeId: string;
  value: string;
  initialLabel: string | null;
  onChange: (id: string) => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => setSearch(text.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [text]);
  const projects = useProjectSearch(search);
  const options: SelectOption[] = [
    { value: '*', label: t('raid.thisProject') },
    ...(value && initialLabel && !(projects.data?.items ?? []).some((p) => p.id === value)
      ? [{ value, label: initialLabel }]
      : []),
    ...(projects.data?.items ?? [])
      .filter((p) => p.id !== excludeId)
      .map((p) => ({ value: p.id, label: `${p.number} · ${pickText(i18n.language, p.nameAr, p.nameEn)}` })),
  ];
  return (
    <div className="flex flex-col gap-2 md:col-span-2">
      <FormField label={t('raid.fields.searchProject')}>
        <Input dir="auto" type="search" value={text} onChange={(e) => setText(e.target.value)} />
      </FormField>
      <FormField label={t('raid.fields.dependsOnProject')}>
        <Select
          value={value || '*'}
          onValueChange={(id) => onChange(id === '*' ? '' : id)}
          options={options}
        />
      </FormField>
    </div>
  );
}

/** 5 × 5 heat-map: probability rows (5 at the top) × impact columns (1 at the start edge; the grid mirrors in RTL). */
function RiskMatrixPanel({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const matrix = useRiskMatrix(project.id);
  const [selected, setSelected] = useState<{ probability: number; impact: number } | null>(null);
  const lang = i18n.language;
  if (matrix.isPending) return <Skeleton className="h-64" />;
  if (matrix.isError) return <StatusBanner tone="danger" title={errorMessage(tc, matrix.error)} />;
  const grid = matrixGrid(matrix.data.cells);
  const cell = selected
    ? grid
        .find((r) => r.probability === selected.probability)
        ?.cells.find((c) => c.impact === selected.impact)
    : undefined;
  const risks = cell ? matrix.data.risks.filter((r) => cell.riskIds.includes(r.id)) : [];
  return (
    <section className="flex flex-col gap-3" data-testid="risk-matrix">
      <header>
        <h3 className="font-semibold">{t('matrix.title')}</h3>
        <p className="text-sm text-muted-foreground">
          {t('matrix.hint', {
            basis: t(matrix.data.basis === 'residual' ? 'matrix.residual' : 'matrix.inherent'),
          })}
        </p>
      </header>
      <div className="flex gap-2">
        <div className="flex items-center">
          <span className="text-xs font-medium text-muted-foreground [writing-mode:vertical-rl] rotate-180">
            {t('matrix.probability')}
          </span>
        </div>
        <div className="grid max-w-xl flex-1 grid-cols-[auto_repeat(5,minmax(0,1fr))] gap-1">
          {grid.map((row) => (
            <div key={row.probability} className="contents">
              <span className="flex items-center justify-center px-1 text-xs tabular-nums text-muted-foreground">
                {formatNumber(row.probability, lang)}
              </span>
              {row.cells.map((c) => {
                const active = selected?.probability === c.probability && selected.impact === c.impact;
                return (
                  <button
                    key={c.impact}
                    type="button"
                    data-testid={`matrix-cell-${c.probability}-${c.impact}`}
                    aria-pressed={active}
                    onClick={() =>
                      setSelected(active ? null : { probability: c.probability, impact: c.impact })
                    }
                    className={cn(
                      'flex aspect-square min-h-10 flex-col items-center justify-center rounded-md border text-sm font-semibold tabular-nums focus-visible:outline-2 focus-visible:outline-primary',
                      bandCell[c.band],
                      active && 'ring-2 ring-primary',
                      // Empty cells are told apart by a dashed border, not by fading the text (WCAG 1.4.3).
                      c.count === 0 && 'border-dashed',
                    )}
                  >
                    {formatNumber(c.count, lang)}
                    <span className="hidden text-[10px] font-normal sm:block">{t(`bands.${c.band}`)}</span>
                    {/* The visible count and band start the accessible name; the full description follows (WCAG 2.5.3). */}
                    <span className="sr-only">
                      {' — '}
                      {t('matrix.cellLabel', {
                        probability: c.probability,
                        impact: c.impact,
                        band: t(`bands.${c.band}`),
                        count: c.count,
                      })}
                    </span>
                  </button>
                );
              })}
            </div>
          ))}
          <span />
          {[1, 2, 3, 4, 5].map((i) => (
            <span key={i} className="text-center text-xs tabular-nums text-muted-foreground">
              {formatNumber(i, lang)}
            </span>
          ))}
          <span />
          <span className="col-span-5 text-center text-xs font-medium text-muted-foreground">
            {t('matrix.impact')}
          </span>
        </div>
      </div>
      {cell && (
        <div className="rounded-lg border bg-card p-3 text-sm" data-testid="matrix-cell-risks">
          <h4 className="font-medium">
            {t('matrix.cellTitle', { probability: cell.probability, impact: cell.impact })}
          </h4>
          {risks.length === 0 ? (
            <p className="text-muted-foreground">{t('matrix.noRisks')}</p>
          ) : (
            <ul className="mt-1 flex flex-col gap-1">
              {risks.map((r) => (
                <li key={r.id} dir="auto">
                  {r.title ?? '—'}
                  <span className="ms-2 text-muted-foreground">
                    {r.owner ? pickText(lang, r.owner.nameAr, r.owner.nameEn) : ''}
                    {r.reviewDate ? ` · ${t('matrix.reviewDate')}: ${formatDate(r.reviewDate, lang)}` : ''}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}
