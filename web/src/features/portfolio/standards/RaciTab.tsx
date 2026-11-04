import { Plus } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { pickText } from '@/lib/format';
import {
  Button,
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
  useToast,
} from '@/ui';
import { useMilestones, useProjectMutation, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { standardsApi, useRaci, type RaciEntry } from './standardsApi';
import { raciAreasWithoutSingleA, type Tone } from './standardsLogic';

const roles = ['R', 'A', 'C', 'I'] as const;
type Role = (typeof roles)[number];
const roleTone: Record<Role, Tone> = { R: 'info', A: 'warning', C: 'success', I: 'neutral' };
const asRole = (r: string): Role => ((roles as readonly string[]).includes(r) ? (r as Role) : 'R');

/** RACI matrix (docs/design/portfolio.md §35): areas × people, one Accountable per area. */
export function RaciTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const raci = useRaci(project.id);
  const [editing, setEditing] = useState<{ row?: RaciEntry; area?: string } | null>(null);
  const lang = i18n.language;
  if (raci.isPending) return <Skeleton className="h-40" />;
  if (raci.isError) return <StatusBanner tone="danger" title={errorMessage(tc, raci.error)} />;
  const { entries, areas, people, canEdit } = raci.data;
  const { missing, multiple } = raciAreasWithoutSingleA(entries);
  const editable = canEdit && project.status === 'active';
  const listOf = (items: string[]) => new Intl.ListFormat(lang === 'ar' ? 'ar' : 'en').format(items);
  return (
    <section className="flex flex-col gap-4" data-testid="raci-matrix">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('raci.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('raci.hint')}</p>
        </div>
        {editable && (
          <Button onClick={() => setEditing({})} data-testid="raci-add">
            <Plus aria-hidden />
            {t('raci.add')}
          </Button>
        )}
      </header>
      <ul className="flex flex-wrap gap-3 text-xs text-muted-foreground" aria-label={t('raci.legend')}>
        {roles.map((r) => (
          <li key={r} className="flex items-center gap-1">
            <StatusChip tone={roleTone[r]}>{r}</StatusChip>
            {t(`raci.roles.${r}`)}
          </li>
        ))}
      </ul>
      {(missing.length > 0 || multiple.length > 0) && (
        <StatusBanner
          tone="warning"
          title={t('raci.oneAccountable')}
          description={[
            missing.length > 0 ? t('raci.missingA', { areas: listOf(missing) }) : null,
            multiple.length > 0 ? t('raci.multipleA', { areas: listOf(multiple) }) : null,
          ]
            .filter(Boolean)
            .join(' ')}
        />
      )}
      {entries.length === 0 ? (
        <EmptyState title={t('raci.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('raci.area')}</TableHeaderCell>
                {people.map((p) => (
                  <TableHeaderCell key={p.id}>{pickText(lang, p.nameAr, p.nameEn)}</TableHeaderCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {areas.map((area) => (
                <TableRow key={area} data-testid="raci-row">
                  <TableCell dir="auto" className="font-medium">
                    {area}
                    {editable && (
                      <Button
                        size="sm"
                        variant="ghost"
                        className="ms-2"
                        aria-label={t('raci.addTo', { area })}
                        onClick={() => setEditing({ area })}
                      >
                        <Plus aria-hidden />
                      </Button>
                    )}
                  </TableCell>
                  {people.map((p) => {
                    const entry = entries.find((e) => e.area === area && e.employeeId === p.id);
                    if (!entry) return <TableCell key={p.id} aria-label={t('raci.noRole')} />;
                    const role = asRole(entry.role);
                    const chip = (
                      <StatusChip tone={roleTone[role]}>
                        <abbr title={t(`raci.roles.${role}`)} className="no-underline">
                          {role}
                        </abbr>
                      </StatusChip>
                    );
                    return (
                      <TableCell key={p.id}>
                        {editable ? (
                          <button
                            type="button"
                            className="rounded-full focus-visible:outline-2 focus-visible:outline-primary"
                            aria-label={t('raci.editEntry', {
                              area,
                              person: pickText(lang, p.nameAr, p.nameEn),
                              role: t(`raci.roles.${role}`),
                            })}
                            onClick={() => setEditing({ row: entry })}
                          >
                            {chip}
                          </button>
                        ) : (
                          chip
                        )}
                      </TableCell>
                    );
                  })}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && (
        <RaciDialog
          project={project}
          row={editing.row}
          area={editing.area}
          areas={areas}
          onClose={() => setEditing(null)}
        />
      )}
    </section>
  );
}

function RaciDialog({
  project,
  row,
  area,
  areas,
  onClose,
}: {
  project: ProjectFull;
  row: RaciEntry | undefined;
  area: string | undefined;
  areas: string[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const listId = useId();
  const save = useProjectMutation(project.id, standardsApi.saveRaci);
  const remove = useProjectMutation(project.id, standardsApi.removeRaci);
  const milestones = useMilestones(project.id);
  const [v, setV] = useState({
    area: row?.area ?? area ?? '',
    milestoneId: row?.milestoneId ?? '',
    employeeId: row?.employeeId ?? undefined,
    role: row?.role ?? 'R',
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const done = {
    onSuccess: () => {
      toast({ title: t('saved') });
      onClose();
    },
  };
  const error = save.error ?? remove.error;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? t('raci.edit') : t('raci.add')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            {row && (
              <Button
                variant="danger"
                loading={remove.isPending}
                onClick={() => remove.mutate({ id: project.id, itemId: row.id }, done)}
              >
                {t('remove')}
              </Button>
            )}
            <Button
              disabled={v.area.trim() === '' || !v.employeeId}
              loading={save.isPending}
              onClick={() =>
                v.employeeId &&
                save.mutate(
                  {
                    id: project.id,
                    itemId: row?.id,
                    values: {
                      area: v.area.trim(),
                      milestoneId: v.milestoneId || null,
                      employeeId: v.employeeId,
                      role: v.role,
                    },
                  },
                  done,
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="raci-dialog">
          <FormField label={t('raci.area')} required hint={t('raci.areaHint')}>
            <Input dir="auto" list={listId} value={v.area} onChange={(e) => set({ area: e.target.value })} />
          </FormField>
          <datalist id={listId}>
            {areas.map((a) => (
              <option key={a} value={a} />
            ))}
          </datalist>
          <FormField label={t('raci.milestone')}>
            <Select
              value={v.milestoneId || '*'}
              onValueChange={(id) => {
                const m = milestones.data?.milestones.find((x) => x.id === id);
                set({
                  milestoneId: id === '*' ? '' : id,
                  ...(m && v.area.trim() === ''
                    ? { area: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}` }
                    : {}),
                });
              }}
              options={[
                { value: '*', label: t('raid.none') },
                ...(milestones.data?.milestones ?? [])
                  .filter((m) => m.status !== 'cancelled')
                  .map((m) => ({
                    value: m.id,
                    label: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}`,
                  })),
              ]}
            />
          </FormField>
          <FormField label={t('raci.person')} required>
            <EmployeePicker value={v.employeeId} onChange={(id) => set({ employeeId: id })} />
          </FormField>
          <FormField label={t('raci.role')} required hint={t('raci.oneAccountable')}>
            <Select
              value={v.role}
              onValueChange={(role) => set({ role })}
              options={roles.map((r) => ({ value: r, label: `${r} — ${t(`raci.roles.${r}`)}` }))}
            />
          </FormField>
          {error && <FieldErrors error={error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}
