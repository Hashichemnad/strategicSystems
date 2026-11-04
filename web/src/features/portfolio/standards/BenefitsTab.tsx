import { Gauge, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
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
} from '@/ui';
import { useProjectMutation, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  standardsApi,
  useBenefits,
  useRequestBenefitMutation,
  useRequestBenefits,
  type Benefit,
  type BenefitValues,
} from './standardsApi';
import { benefitTone, parseNumber } from './standardsLogic';

const benefitStatuses = ['planned', 'realised', 'partly', 'not-realised'] as const;
const measuredStatuses = ['realised', 'partly', 'not-realised'] as const;
const asStatus = (s: string) =>
  (benefitStatuses as readonly string[]).includes(s) ? (s as 'planned') : 'planned';

/** Benefits of a project (docs/design/portfolio.md §35): defined from the request, measured in Closure / Impact. */
export function BenefitsTab({ project }: { project: ProjectFull }) {
  const list = useBenefits(project.id);
  const save = useProjectMutation(project.id, standardsApi.saveBenefit);
  const remove = useProjectMutation(project.id, standardsApi.removeBenefit);
  const canEdit = project.permissions.canEdit && project.status === 'active';
  const canMeasure = canEdit && (project.stage === 'impact' || project.stage === 'closure');
  return (
    <BenefitsList
      testId="project-benefits"
      query={list}
      canEdit={canEdit}
      canMeasure={canMeasure}
      onSave={(row, values) =>
        save.mutateAsync({ id: project.id, itemId: row?.id, values, version: row?.version })
      }
      onRemove={(row) => remove.mutateAsync({ id: project.id, itemId: row.id })}
      measureProjectId={project.id}
    />
  );
}

/** Benefits defined on a draft request; carried to the project when it is created. */
export function RequestBenefitsPanel({ requestId, canEdit }: { requestId: string; canEdit: boolean }) {
  const list = useRequestBenefits(requestId);
  const save = useRequestBenefitMutation(requestId, standardsApi.saveRequestBenefit);
  const remove = useRequestBenefitMutation(requestId, standardsApi.removeRequestBenefit);
  return (
    <BenefitsList
      testId="request-benefits"
      query={list}
      canEdit={canEdit}
      canMeasure={false}
      onSave={(row, values) =>
        save.mutateAsync({ id: requestId, itemId: row?.id, values, version: row?.version })
      }
      onRemove={(row) => remove.mutateAsync({ id: requestId, itemId: row.id })}
    />
  );
}

function BenefitsList({
  testId,
  query,
  canEdit,
  canMeasure,
  onSave,
  onRemove,
  measureProjectId,
}: {
  testId: string;
  query: ReturnType<typeof useBenefits>;
  canEdit: boolean;
  canMeasure: boolean;
  onSave: (row: Benefit | undefined, values: BenefitValues) => Promise<unknown>;
  onRemove: (row: Benefit) => Promise<unknown>;
  measureProjectId?: string;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const [editing, setEditing] = useState<{ row?: Benefit } | null>(null);
  const [measuring, setMeasuring] = useState<Benefit | null>(null);
  const [removing, setRemoving] = useState<Benefit | null>(null);
  const [removeError, setRemoveError] = useState<unknown>();
  const [busy, setBusy] = useState(false);
  const lang = i18n.language;
  const value = (n: number | null, unit: string | null) =>
    n === null ? '—' : `${formatNumber(n, lang)}${unit ? ` ${unit}` : ''}`;
  const rows = query.data ?? [];
  return (
    <section className="flex flex-col gap-4" data-testid={testId}>
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('benefits.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('benefits.hint')}</p>
        </div>
        {canEdit && (
          <Button onClick={() => setEditing({})} data-testid="benefit-add">
            <Plus aria-hidden />
            {t('benefits.add')}
          </Button>
        )}
      </header>
      {removeError !== undefined && <FieldErrors error={removeError} />}
      {query.isPending ? (
        <Skeleton className="h-32" />
      ) : query.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, query.error)} />
      ) : rows.length === 0 ? (
        <EmptyState title={t('benefits.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('benefits.benefit')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.measure')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.baseline')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.target')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.actual')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.measurementDate')}</TableHeaderCell>
                <TableHeaderCell>{t('benefits.status')}</TableHeaderCell>
                {(canEdit || canMeasure) && <TableHeaderCell>{t('actionsColumn')}</TableHeaderCell>}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((b) => (
                <TableRow key={b.id} data-testid="benefit-row">
                  <TableCell dir="auto">{pickText(lang, b.titleAr, b.titleEn) || '—'}</TableCell>
                  <TableCell dir="auto">{b.measure ?? '—'}</TableCell>
                  <TableCell className="tabular-nums">{value(b.baselineValue, b.unit)}</TableCell>
                  <TableCell className="tabular-nums">{value(b.targetValue, b.unit)}</TableCell>
                  <TableCell className="tabular-nums">
                    {value(b.actualValue, b.unit)}
                    {b.note && (
                      <span className="block text-xs text-muted-foreground" dir="auto">
                        {b.note}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>{b.owner ? pickText(lang, b.owner.nameAr, b.owner.nameEn) : '—'}</TableCell>
                  <TableCell>{b.measurementDate ? formatDate(b.measurementDate, lang) : '—'}</TableCell>
                  <TableCell>
                    <StatusChip tone={benefitTone[b.status] ?? 'neutral'}>
                      {t(`benefits.statuses.${asStatus(b.status)}`)}
                    </StatusChip>
                  </TableCell>
                  {(canEdit || canMeasure) && (
                    <TableCell>
                      <div className="flex gap-1">
                        {canMeasure && (
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() => setMeasuring(b)}
                            data-testid="benefit-measure"
                          >
                            <Gauge aria-hidden />
                            {t('benefits.measureAction')}
                          </Button>
                        )}
                        {canEdit && (
                          <>
                            <Button
                              size="sm"
                              variant="ghost"
                              aria-label={tc('actions.edit')}
                              onClick={() => setEditing({ row: b })}
                            >
                              <Pencil aria-hidden />
                            </Button>
                            <Button
                              size="sm"
                              variant="ghost"
                              aria-label={t('remove')}
                              onClick={() => setRemoving(b)}
                            >
                              <Trash2 aria-hidden />
                            </Button>
                          </>
                        )}
                      </div>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && <BenefitDialog row={editing.row} onSave={onSave} onClose={() => setEditing(null)} />}
      {measuring && measureProjectId && (
        <MeasureDialog projectId={measureProjectId} row={measuring} onClose={() => setMeasuring(null)} />
      )}
      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={t('benefits.removeTitle')}
        description={t('benefits.removeHint')}
        confirmLabel={t('remove')}
        destructive
        loading={busy}
        onConfirm={async () => {
          if (!removing) return;
          setBusy(true);
          setRemoveError(undefined);
          try {
            await onRemove(removing);
          } catch (failure) {
            setRemoveError(failure);
          } finally {
            setBusy(false);
            setRemoving(null);
          }
        }}
      />
    </section>
  );
}

function BenefitDialog({
  row,
  onSave,
  onClose,
}: {
  row: Benefit | undefined;
  onSave: (row: Benefit | undefined, values: BenefitValues) => Promise<unknown>;
  onClose: () => void;
}) {
  const { t } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const text = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));
  const [v, setV] = useState({
    titleAr: row?.titleAr ?? '',
    titleEn: row?.titleEn ?? '',
    measure: row?.measure ?? '',
    baselineValue: text(row?.baselineValue),
    targetValue: text(row?.targetValue),
    unit: row?.unit ?? '',
    owner: row?.owner?.id ?? undefined,
    measurementDate: row?.measurementDate ?? undefined,
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const save = async () => {
    setBusy(true);
    setError(undefined);
    try {
      await onSave(row, {
        titleAr: v.titleAr.trim() || null,
        titleEn: v.titleEn.trim() || null,
        measure: v.measure.trim() || null,
        baselineValue: parseNumber(v.baselineValue),
        targetValue: parseNumber(v.targetValue),
        unit: v.unit.trim() || null,
        ownerEmployeeId: v.owner ?? null,
        measurementDate: v.measurementDate ?? null,
      });
      toast({ title: t('saved') });
      onClose();
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? t('benefits.edit') : t('benefits.add')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.titleAr.trim() === '' && v.titleEn.trim() === ''}
              loading={busy}
              onClick={() => void save()}
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="benefit-dialog">
          <FormField label={t('benefits.titleAr')}>
            <Input dir="rtl" value={v.titleAr} onChange={(e) => set({ titleAr: e.target.value })} />
          </FormField>
          <FormField label={t('benefits.titleEn')}>
            <Input dir="ltr" value={v.titleEn} onChange={(e) => set({ titleEn: e.target.value })} />
          </FormField>
          <FormField label={t('benefits.measure')} className="md:col-span-2" hint={t('benefits.measureHint')}>
            <Textarea value={v.measure} onChange={(e) => set({ measure: e.target.value })} />
          </FormField>
          <FormField label={t('benefits.baseline')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={v.baselineValue}
              onChange={(e) => set({ baselineValue: e.target.value })}
            />
          </FormField>
          <FormField label={t('benefits.target')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={v.targetValue}
              onChange={(e) => set({ targetValue: e.target.value })}
            />
          </FormField>
          <FormField label={t('benefits.unit')}>
            <Input dir="auto" value={v.unit} onChange={(e) => set({ unit: e.target.value })} />
          </FormField>
          <FormField label={t('benefits.measurementDate')}>
            <DatePicker value={v.measurementDate} onChange={(d) => set({ measurementDate: d })} />
          </FormField>
          <FormField label={t('benefits.owner')} className="md:col-span-2">
            <EmployeePicker value={v.owner} onChange={(id) => set({ owner: id })} />
          </FormField>
          {error !== undefined && (
            <div className="md:col-span-2">
              <FieldErrors error={error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function MeasureDialog({
  projectId,
  row,
  onClose,
}: {
  projectId: string;
  row: Benefit;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const measure = useProjectMutation(projectId, standardsApi.measureBenefit);
  const [actual, setActual] = useState(row.actualValue === null ? '' : String(row.actualValue));
  const [status, setStatus] = useState<string | undefined>(
    (measuredStatuses as readonly string[]).includes(row.status) ? row.status : undefined,
  );
  const [note, setNote] = useState(row.note ?? '');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('benefits.measureTitle')}
        description={pickText(i18n.language, row.titleAr, row.titleEn)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!status}
              loading={measure.isPending}
              onClick={() =>
                status &&
                measure.mutate(
                  {
                    id: projectId,
                    itemId: row.id,
                    version: row.version,
                    values: { actualValue: parseNumber(actual), status, note: note.trim() || null },
                  },
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
        <div className="flex flex-col gap-3" data-testid="benefit-measure-dialog">
          <p className="text-sm text-muted-foreground">
            {t('benefits.targetIs', {
              value: row.targetValue === null ? '—' : formatNumber(row.targetValue, i18n.language),
              unit: row.unit ?? '',
            })}
          </p>
          <FormField label={t('benefits.actual')}>
            <Input dir="ltr" inputMode="decimal" value={actual} onChange={(e) => setActual(e.target.value)} />
          </FormField>
          <FormField label={t('benefits.status')} required>
            <Select
              value={status}
              onValueChange={setStatus}
              options={measuredStatuses.map((s) => ({ value: s, label: t(`benefits.statuses.${s}`) }))}
            />
          </FormField>
          <FormField label={t('benefits.note')}>
            <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
          </FormField>
          {measure.isError && <FieldErrors error={measure.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}
