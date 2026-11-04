import { Check, Pencil, Plus, Sparkles, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, pickText } from '@/lib/format';
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
import { useProjectItems, useProjectMutation, type ProjectFull, type Stakeholder } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { standardsApi, useCommunications, type Communication } from './standardsApi';
import { isOverdue } from './standardsLogic';

const frequencies = ['weekly', 'biweekly', 'monthly', 'quarterly', 'once'] as const;
const asFrequency = (f: string) => ((frequencies as readonly string[]).includes(f) ? (f as 'once') : 'once');

/** Communication plan (docs/design/portfolio.md §35): who hears what, how and how often; due items highlighted. */
export function CommunicationTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const list = useCommunications(project.id);
  const generate = useProjectMutation(project.id, standardsApi.generateCommunications);
  const done = useProjectMutation(project.id, standardsApi.communicationDone);
  const remove = useProjectMutation(project.id, standardsApi.removeCommunication);
  const [editing, setEditing] = useState<{ row?: Communication } | null>(null);
  const [removing, setRemoving] = useState<Communication | null>(null);
  const lang = i18n.language;
  const today = new Date().toISOString().slice(0, 10);
  const canEdit = project.permissions.canEdit && project.status === 'active';
  const rows = (list.data ?? []).filter((c) => c.status === 'active');
  const error = generate.error ?? done.error ?? remove.error;
  return (
    <section className="flex flex-col gap-4" data-testid="communication-plan">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('communication.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('communication.hint')}</p>
        </div>
        {canEdit && (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              loading={generate.isPending}
              data-testid="communication-generate"
              onClick={() =>
                generate.mutate(
                  { id: project.id },
                  { onSuccess: () => toast({ title: t('communication.generated') }) },
                )
              }
            >
              <Sparkles aria-hidden />
              {t('communication.generate')}
            </Button>
            <Button onClick={() => setEditing({})}>
              <Plus aria-hidden />
              {t('communication.add')}
            </Button>
          </div>
        )}
      </header>
      {error && <FieldErrors error={error} />}
      {list.isPending ? (
        <Skeleton className="h-32" />
      ) : list.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, list.error)} />
      ) : rows.length === 0 ? (
        <EmptyState title={t('communication.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('communication.audience')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.message')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.method')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.frequency')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.nextDue')}</TableHeaderCell>
                <TableHeaderCell>{t('communication.lastDone')}</TableHeaderCell>
                {canEdit && <TableHeaderCell>{t('actionsColumn')}</TableHeaderCell>}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((c) => {
                const overdue = isOverdue(c.nextDue, today);
                return (
                  <TableRow
                    key={c.id}
                    data-testid="communication-row"
                    className={c.due ? 'bg-warning-soft/40' : undefined}
                  >
                    <TableCell dir="auto">
                      {c.stakeholderName ?? c.audience ?? '—'}
                      {c.stakeholderName && c.audience && (
                        <span className="block text-xs text-muted-foreground">{c.audience}</span>
                      )}
                    </TableCell>
                    <TableCell dir="auto" className="whitespace-pre-line">
                      {c.message ?? '—'}
                    </TableCell>
                    <TableCell dir="auto">{c.method ?? '—'}</TableCell>
                    <TableCell>{t(`communication.frequencies.${asFrequency(c.frequency)}`)}</TableCell>
                    <TableCell>{c.owner ? pickText(lang, c.owner.nameAr, c.owner.nameEn) : '—'}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-1">
                        {c.nextDue ? formatDate(c.nextDue, lang) : '—'}
                        {overdue ? (
                          <StatusChip tone="danger">{t('communication.overdue')}</StatusChip>
                        ) : (
                          c.due && <StatusChip tone="warning">{t('communication.due')}</StatusChip>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>{c.lastDone ? formatDate(c.lastDone, lang) : '—'}</TableCell>
                    {canEdit && (
                      <TableCell>
                        <div className="flex gap-1">
                          {c.nextDue && (
                            <Button
                              size="sm"
                              variant="outline"
                              loading={done.isPending}
                              data-testid="communication-done"
                              onClick={() =>
                                done.mutate(
                                  { id: project.id, itemId: c.id },
                                  { onSuccess: () => toast({ title: t('communication.marked') }) },
                                )
                              }
                            >
                              <Check aria-hidden />
                              {t('communication.markDone')}
                            </Button>
                          )}
                          <Button
                            size="sm"
                            variant="ghost"
                            aria-label={tc('actions.edit')}
                            onClick={() => setEditing({ row: c })}
                          >
                            <Pencil aria-hidden />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            aria-label={t('remove')}
                            onClick={() => setRemoving(c)}
                          >
                            <Trash2 aria-hidden />
                          </Button>
                        </div>
                      </TableCell>
                    )}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && (
        <CommunicationDialog project={project} row={editing.row} onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={t('communication.removeTitle')}
        description={t('communication.removeHint')}
        confirmLabel={t('remove')}
        destructive
        loading={remove.isPending}
        onConfirm={() =>
          removing &&
          remove.mutate({ id: project.id, itemId: removing.id }, { onSettled: () => setRemoving(null) })
        }
      />
    </section>
  );
}

function CommunicationDialog({
  project,
  row,
  onClose,
}: {
  project: ProjectFull;
  row: Communication | undefined;
  onClose: () => void;
}) {
  const { t } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.saveCommunication);
  const stakeholders = useProjectItems<Stakeholder>(project.id, 'stakeholders');
  const [v, setV] = useState({
    stakeholderId: row?.stakeholderId ?? '',
    audience: row?.audience ?? '',
    message: row?.message ?? '',
    method: row?.method ?? '',
    frequency: row?.frequency ?? 'monthly',
    owner: row?.owner?.id ?? undefined,
    nextDue: row?.nextDue ?? undefined,
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? t('communication.edit') : t('communication.add')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.message.trim() === '' || (v.audience.trim() === '' && !v.stakeholderId)}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: project.id,
                    itemId: row?.id,
                    values: {
                      stakeholderId: v.stakeholderId || null,
                      audience: v.audience.trim() || null,
                      message: v.message.trim() || null,
                      method: v.method.trim() || null,
                      frequency: v.frequency,
                      ownerEmployeeId: v.owner ?? null,
                      nextDue: v.nextDue ?? null,
                    },
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
        <div className="grid gap-3 md:grid-cols-2" data-testid="communication-dialog">
          <FormField label={t('communication.stakeholder')}>
            <Select
              value={v.stakeholderId || '*'}
              onValueChange={(id) => set({ stakeholderId: id === '*' ? '' : id })}
              options={[
                { value: '*', label: t('raid.none') },
                ...(stakeholders.data ?? [])
                  .filter((s) => s.status === 'active')
                  .map((s) => ({ value: s.id, label: s.name ?? '—' })),
              ]}
            />
          </FormField>
          <FormField label={t('communication.audience')} required={!v.stakeholderId}>
            <Input dir="auto" value={v.audience} onChange={(e) => set({ audience: e.target.value })} />
          </FormField>
          <FormField label={t('communication.message')} required className="md:col-span-2">
            <Textarea value={v.message} onChange={(e) => set({ message: e.target.value })} />
          </FormField>
          <FormField label={t('communication.method')}>
            <Input dir="auto" value={v.method} onChange={(e) => set({ method: e.target.value })} />
          </FormField>
          <FormField label={t('communication.frequency')} required>
            <Select
              value={v.frequency}
              onValueChange={(frequency) => set({ frequency })}
              options={frequencies.map((f) => ({ value: f, label: t(`communication.frequencies.${f}`) }))}
            />
          </FormField>
          <FormField label={t('communication.owner')}>
            <EmployeePicker value={v.owner} onChange={(id) => set({ owner: id })} />
          </FormField>
          <FormField label={t('communication.nextDue')}>
            <DatePicker value={v.nextDue} onChange={(d) => set({ nextDue: d })} />
          </FormField>
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
