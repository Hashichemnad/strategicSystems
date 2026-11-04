import { History, ListTodo, Plus, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useUnitTree } from '@/features/org/api';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
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
} from '@/ui';
import {
  actionTone,
  riskApi,
  useActions,
  useActionUpdates,
  useRiskMutation,
  useRiskOptions,
  type ActionValues,
  type Risk,
  type TreatmentAction,
} from './api';
import { ProgressBar, Section, useNames, useNum, useRiskLabels, useRiskT } from './components';

const NONE = '-';

export function ActionsTab({ risk }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const actions = useActions(risk.id);
  const toast = useToast();
  const cancel = useRiskMutation(riskApi.cancelAction);
  const [editing, setEditing] = useState<{ action?: TreatmentAction }>();
  const [progress, setProgress] = useState<TreatmentAction>();
  const [convert, setConvert] = useState<TreatmentAction>();
  const [updates, setUpdates] = useState<TreatmentAction>();
  const [tasks, setTasks] = useState<TreatmentAction>();
  const [cancelling, setCancelling] = useState<TreatmentAction>();
  return (
    <Section
      title={t('treatment.title')}
      testId="risk-actions"
      actions={
        risk.can.manageChildren && (
          <Button size="sm" onClick={() => setEditing({})} data-testid="risk-action-add">
            <Plus aria-hidden />
            {t('treatment.add')}
          </Button>
        )
      }
    >
      {!actions.data ? (
        <Skeleton className="h-24" />
      ) : actions.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('treatment.empty')}</p>
      ) : (
        <ActionTable
          rows={actions.data}
          onEdit={(a) => setEditing({ action: a })}
          onProgress={setProgress}
          onConvert={setConvert}
          onUpdates={setUpdates}
          onTasks={setTasks}
          onCancel={setCancelling}
        />
      )}
      {editing && (
        <ActionDialog riskId={risk.id} action={editing.action} onClose={() => setEditing(undefined)} />
      )}
      {progress && <ProgressDialog action={progress} onClose={() => setProgress(undefined)} />}
      {convert && <ConvertDialog action={convert} onClose={() => setConvert(undefined)} />}
      {updates && <UpdatesDialog action={updates} onClose={() => setUpdates(undefined)} />}
      {tasks && (
        <Dialog open onOpenChange={(open) => !open && setTasks(undefined)}>
          <DialogContent title={t('treatment.tasks', { number: tasks.number })} className="max-w-3xl">
            <LinkedTasksPanel type="risk.action" id={tasks.id} />
          </DialogContent>
        </Dialog>
      )}
      <ConfirmDialog
        open={!!cancelling}
        onOpenChange={(open) => !open && setCancelling(undefined)}
        title={t('treatment.cancel')}
        description={t('treatment.cancelHint')}
        confirmLabel={t('treatment.cancel')}
        destructive
        loading={cancel.isPending}
        onConfirm={() =>
          cancelling &&
          cancel.mutate(cancelling.id, {
            onSuccess: () => setCancelling(undefined),
            onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
          })
        }
      />
    </Section>
  );
}

export function ActionTable({
  rows,
  onEdit,
  onProgress,
  onConvert,
  onUpdates,
  onTasks,
  onCancel,
  showRisk,
  highlight,
}: {
  rows: readonly TreatmentAction[];
  onEdit?: (a: TreatmentAction) => void;
  onProgress: (a: TreatmentAction) => void;
  onConvert?: (a: TreatmentAction) => void;
  onUpdates: (a: TreatmentAction) => void;
  onTasks?: (a: TreatmentAction) => void;
  onCancel?: (a: TreatmentAction) => void;
  showRisk?: boolean;
  highlight?: string;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const names = useNames();
  const open = (a: TreatmentAction) => a.status === 'not_started' || a.status === 'in_progress';
  return (
    <Table data-testid="risk-actions-table">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
          <TableHeaderCell>{t('treatment.titleField')}</TableHeaderCell>
          {showRisk && <TableHeaderCell>{t('fields.risk')}</TableHeaderCell>}
          <TableHeaderCell>{t('fields.owner')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.plannedStart')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.plannedEnd')}</TableHeaderCell>
          <TableHeaderCell>{t('treatment.progress')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          <TableHeaderCell>
            <span className="sr-only">{tc('actions.more')}</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((a) => (
          <TableRow
            key={a.id}
            data-testid="risk-action-row"
            data-highlighted={highlight === a.id ? 'true' : undefined}
            className={highlight === a.id ? 'bg-warning-soft' : undefined}
          >
            <TableCell>
              <bdi className="font-medium">{a.number}</bdi>
            </TableCell>
            <TableCell>
              <span className="flex flex-col">
                <span>{names.text(a.values.titleAr, a.values.titleEn)}</span>
                {a.responsibleUnits.length > 0 && (
                  <span className="text-xs text-muted-foreground">
                    {a.responsibleUnits.map(names.unit).join('، ')}
                  </span>
                )}
                {a.draftUpdates > 0 && (
                  <StatusChip tone="info" className="mt-1 self-start">
                    {t('treatment.draftUpdates', { count: a.draftUpdates })}
                  </StatusChip>
                )}
              </span>
            </TableCell>
            {showRisk && (
              <TableCell>
                <a href={`/risk/risks/${a.riskId}?tab=actions`} className="text-primary hover:underline">
                  <bdi>{a.riskNumber}</bdi> · {names.text(a.riskTitleAr, a.riskTitleEn)}
                </a>
              </TableCell>
            )}
            <TableCell>{names.person(a.owner)}</TableCell>
            <TableCell>{a.values.plannedStart ? formatDate(a.values.plannedStart, language) : '—'}</TableCell>
            <TableCell className={a.overdue ? 'text-danger' : undefined}>
              {a.values.plannedEnd ? formatDate(a.values.plannedEnd, language) : '—'}
              {a.overdue && (
                <StatusChip tone="danger" className="ms-1">
                  {t('treatment.overdue')}
                </StatusChip>
              )}
            </TableCell>
            <TableCell>
              <ProgressBar ratio={a.completionPct} label={t('treatment.progress')} />
            </TableCell>
            <TableCell>
              <StatusChip tone={actionTone[a.status] ?? 'neutral'}>
                {k(`actionStatus.${a.status}`)}
              </StatusChip>
            </TableCell>
            <TableCell>
              <span className="flex flex-wrap gap-1">
                {a.canProgress && open(a) && (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => onProgress(a)}
                    data-testid="risk-action-progress"
                  >
                    {t('treatment.updateProgress')}
                  </Button>
                )}
                {a.canConvert && a.status === 'completed' && !a.convertedControlId && onConvert && (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => onConvert(a)}
                    data-testid="risk-action-convert"
                  >
                    {t('treatment.convert')}
                  </Button>
                )}
                {a.canEdit && open(a) && onEdit && (
                  <Button size="sm" variant="ghost" onClick={() => onEdit(a)}>
                    {tc('actions.edit')}
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => onUpdates(a)}
                  aria-label={t('treatment.updates')}
                >
                  <History aria-hidden />
                </Button>
                {onTasks && (
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => onTasks(a)}
                    aria-label={t('treatment.tasks', { number: a.number })}
                  >
                    <ListTodo aria-hidden />
                  </Button>
                )}
                {a.canEdit && open(a) && onCancel && (
                  <Button size="sm" variant="ghost" onClick={() => onCancel(a)}>
                    {t('treatment.cancel')}
                  </Button>
                )}
              </span>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export function ActionDialog({
  riskId,
  action,
  prefill,
  onClose,
}: {
  riskId: string;
  action?: TreatmentAction;
  prefill?: Partial<ActionValues>;
  onClose: () => void;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const names = useNames();
  const save = useRiskMutation(riskApi.saveAction);
  const unitNames: Record<string, string> = Object.fromEntries(
    (action?.responsibleUnits ?? []).map((u) => [u.id, names.unit(u)]),
  );
  const [v, setV] = useState<ActionValues>({
    titleAr: null,
    titleEn: null,
    ownerEmployeeId: null,
    responsibleUnitIds: [],
    plannedStart: null,
    plannedEnd: null,
    cost: null,
    ...action?.values,
    ...prefill,
  });
  const set = <K extends keyof ActionValues>(key: K, value: ActionValues[K]) =>
    setV((p) => ({ ...p, [key]: value }));
  const units = v.responsibleUnitIds ?? [];
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={action ? t('treatment.edit') : t('treatment.add')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!v.titleAr?.trim() && !v.titleEn?.trim()}
              data-testid="risk-action-save"
              onClick={() =>
                save.mutate(
                  { riskId, actionId: action?.id, values: v, version: action?.version ?? null },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('treatment.titleAr')}>
            <Input
              dir="rtl"
              value={v.titleAr ?? ''}
              onChange={(e) => set('titleAr', e.target.value || null)}
              data-testid="risk-action-title-ar"
            />
          </FormField>
          <FormField label={t('treatment.titleEn')}>
            <Input
              dir="ltr"
              value={v.titleEn ?? ''}
              onChange={(e) => set('titleEn', e.target.value || null)}
              data-testid="risk-action-title-en"
            />
          </FormField>
          <FormField label={t('fields.owner')}>
            <EmployeePicker
              value={v.ownerEmployeeId ?? undefined}
              onChange={(x) => set('ownerEmployeeId', x ?? null)}
            />
          </FormField>
          <FormField label={t('treatment.cost')}>
            <Input
              type="number"
              min={0}
              value={v.cost ?? ''}
              onChange={(e) => set('cost', e.target.value === '' ? null : Number(e.target.value))}
            />
          </FormField>
          <FormField label={t('fields.plannedStart')}>
            <Input
              type="date"
              value={v.plannedStart ?? ''}
              onChange={(e) => set('plannedStart', e.target.value || null)}
            />
          </FormField>
          <FormField label={t('fields.plannedEnd')}>
            <Input
              type="date"
              value={v.plannedEnd ?? ''}
              onChange={(e) => set('plannedEnd', e.target.value || null)}
              data-testid="risk-action-planned-end"
            />
          </FormField>
          <div className="flex flex-col gap-2 md:col-span-2">
            <span className="text-sm font-medium">{t('treatment.responsibleUnits')}</span>
            <UnitSelect
              value={undefined}
              onChange={(id) => {
                if (id && !units.includes(id)) set('responsibleUnitIds', [...units, id]);
              }}
            />
            <ResponsibleUnitChips
              ids={units}
              names={unitNames}
              onRemove={(id) =>
                set(
                  'responsibleUnitIds',
                  units.filter((u) => u !== id),
                )
              }
            />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ResponsibleUnitChips({
  ids,
  names,
  onRemove,
}: {
  ids: string[];
  names: Record<string, string>;
  onRemove: (id: string) => void;
}) {
  const { t } = useTranslation('access');
  const { i18n } = useTranslation();
  const tree = useUnitTree();
  const label = (id: string) => {
    if (names[id]) return names[id];
    const u = tree.data?.find((x) => x.id === id);
    return u ? pickText(i18n.language, u.nameAr, u.nameEn) || u.code : '…';
  };
  return (
    <span className="flex flex-wrap gap-1">
      {ids.map((id) => (
        <span key={id} className="flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs">
          {label(id)}
          <button type="button" onClick={() => onRemove(id)} aria-label={t('pickers.clear')}>
            <X className="size-3" aria-hidden />
          </button>
        </span>
      ))}
    </span>
  );
}

/** Progress update: % 0–100 (sent as a ratio), a note (required), actual dates. */
export function ProgressDialog({ action, onClose }: { action: TreatmentAction; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useRiskMutation(riskApi.progress);
  const [pct, setPct] = useState(String(Math.round(action.completionPct * 100)));
  const [note, setNote] = useState('');
  const [actualStart, setActualStart] = useState(action.actualStart ?? '');
  const [actualEnd, setActualEnd] = useState(action.actualEnd ?? '');
  const value = Number(pct);
  const valid = pct !== '' && value >= 0 && value <= 100 && note.trim().length > 0;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('treatment.updateProgress')}
        description={action.number}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!valid}
              data-testid="risk-progress-save"
              onClick={() =>
                save.mutate(
                  {
                    id: action.id,
                    completionPct: value / 100,
                    note: note || null,
                    actualStart: actualStart || null,
                    actualEnd: actualEnd || null,
                    version: action.version,
                  },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('treatment.percent')} required className="md:col-span-2">
            <Input
              type="number"
              min={0}
              max={100}
              step={1}
              value={pct}
              onChange={(e) => setPct(e.target.value)}
              data-testid="risk-progress-pct"
            />
          </FormField>
          <FormField label={t('common.note')} required className="md:col-span-2">
            <Textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              data-testid="risk-progress-note"
            />
          </FormField>
          <FormField label={t('treatment.actualStart')}>
            <Input type="date" value={actualStart} onChange={(e) => setActualStart(e.target.value)} />
          </FormField>
          <FormField label={t('treatment.actualEnd')}>
            <Input type="date" value={actualEnd} onChange={(e) => setActualEnd(e.target.value)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ConvertDialog({ action, onClose }: { action: TreatmentAction; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const labels = useRiskLabels();
  const options = useRiskOptions();
  const convert = useRiskMutation(riskApi.convert);
  const [effectiveness, setEffectiveness] = useState<string>();
  const [nameAr, setNameAr] = useState(action.values.titleAr ?? '');
  const [nameEn, setNameEn] = useState(action.values.titleEn ?? '');
  const [typeCode, setTypeCode] = useState(NONE);
  const [natureCode, setNatureCode] = useState(NONE);
  const [frequencyCode, setFrequencyCode] = useState(NONE);
  const n = (v: string) => (v === NONE ? null : v);
  const lookup = (value: string, set: (v: string) => void, type: string) => (
    <Select
      value={value}
      onValueChange={set}
      options={[{ value: NONE, label: t('common.none') }, ...labels.lookupOptions(type)]}
    />
  );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('treatment.convert')}
        description={t('treatment.convertHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={convert.isPending}
              disabled={!effectiveness}
              data-testid="risk-convert-save"
              onClick={() =>
                effectiveness &&
                convert.mutate(
                  {
                    id: action.id,
                    input: {
                      effectivenessCode: effectiveness,
                      nameAr: nameAr || null,
                      nameEn: nameEn || null,
                      typeCode: n(typeCode),
                      natureCode: n(natureCode),
                      frequencyCode: n(frequencyCode),
                    },
                  },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {t('treatment.convert')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('controls.effectiveness')} required className="md:col-span-2">
            <Select
              value={effectiveness}
              onValueChange={setEffectiveness}
              options={(options.data?.framework.content.controlEffectiveness ?? []).map((e) => ({
                value: e.code,
                label: labels.effectiveness(e.code),
              }))}
            />
          </FormField>
          <FormField label={t('controls.nameAr')}>
            <Input dir="rtl" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('controls.nameEn')}>
            <Input dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <FormField label={t('controls.type')}>
            {lookup(typeCode, setTypeCode, 'risk.control-type')}
          </FormField>
          <FormField label={t('controls.nature')}>
            {lookup(natureCode, setNatureCode, 'risk.control-nature')}
          </FormField>
          <FormField label={t('controls.frequency')}>
            {lookup(frequencyCode, setFrequencyCode, 'risk.control-frequency')}
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export function UpdatesDialog({ action, onClose }: { action: TreatmentAction; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { language } = useRiskT();
  const names = useNames();
  const num = useNum();
  const updates = useActionUpdates(action.id);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent title={t('treatment.updates')} description={action.number} className="max-w-2xl">
        {!updates.data ? (
          <Skeleton className="h-24" />
        ) : updates.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('common.noData')}</p>
        ) : (
          <ul className="flex flex-col divide-y text-sm" data-testid="risk-action-updates">
            {updates.data.map((u) => (
              <li key={u.id} className="flex flex-col gap-0.5 py-2">
                <span className="flex flex-wrap items-center gap-2">
                  <strong className="tabular-nums">{num(Math.round(u.completionPct * 100))} %</strong>
                  <span className="text-muted-foreground">
                    {names.person(u.by)} · {formatDateTime(u.at, language)}
                  </span>
                  {u.draft && <StatusChip tone="info">{t('treatment.draftMcp')}</StatusChip>}
                </span>
                {u.note && <span className="whitespace-pre-line">{u.note}</span>}
              </li>
            ))}
          </ul>
        )}
      </DialogContent>
    </Dialog>
  );
}
