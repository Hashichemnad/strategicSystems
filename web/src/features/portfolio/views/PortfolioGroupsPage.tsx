import { FolderTree, Pencil, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
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
import { usePortfolioFeatures } from '../featuresApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  usePortfolioGroups,
  useSavePortfolioGroup,
  type PortfolioGroup,
  type PortfolioGroupValues,
} from './viewsApi';

/** Portfolio → Portfolios & programmes (docs/design/portfolio.md §35): tree with counts, progress and delays. */
export function PortfolioGroupsPage() {
  const { t } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const features = usePortfolioFeatures();
  const on = features.data?.features.portfolios === true;
  const groups = usePortfolioGroups(on);
  // The server checks portfolio.portfolio.manage again on every save.
  const canManage = useCan('portfolio.portfolio.manage');
  const [editing, setEditing] = useState<{ group?: PortfolioGroup; kind: string; parentId?: string } | null>(
    null,
  );

  if (features.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (!on) return <EmptyState title={t('groups.off')} icon={FolderTree} />;

  const list = groups.data ?? [];
  const portfolios = list.filter((g) => g.kind === 'portfolio');
  const ids = new Set(portfolios.map((p) => p.id));
  const orphans = list.filter((g) => g.kind === 'programme' && (!g.parentId || !ids.has(g.parentId)));

  return (
    <div className="flex flex-col gap-6" data-testid="portfolio-groups">
      <PageHeader
        title={t('groups.title')}
        description={t('groups.description')}
        actions={
          canManage ? (
            <div className="flex flex-wrap gap-2">
              <Button data-testid="group-new-portfolio" onClick={() => setEditing({ kind: 'portfolio' })}>
                <Plus aria-hidden />
                {t('groups.newPortfolio')}
              </Button>
              <Button
                variant="outline"
                data-testid="group-new-programme"
                onClick={() => setEditing({ kind: 'programme' })}
              >
                <Plus aria-hidden />
                {t('groups.newProgramme')}
              </Button>
            </div>
          ) : undefined
        }
      />
      {groups.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : groups.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, groups.error)} />
      ) : list.length === 0 ? (
        <EmptyState title={t('groups.empty')} icon={FolderTree} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('groups.name')}</TableHeaderCell>
                <TableHeaderCell>{t('groups.kind')}</TableHeaderCell>
                <TableHeaderCell>{t('groups.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('groups.projects')}</TableHeaderCell>
                <TableHeaderCell>{t('groups.progress')}</TableHeaderCell>
                <TableHeaderCell>{t('groups.delayed')}</TableHeaderCell>
                {canManage && (
                  <TableHeaderCell>
                    <span className="sr-only">{t('groups.actions')}</span>
                  </TableHeaderCell>
                )}
              </TableRow>
            </TableHead>
            <TableBody>
              {portfolios.map((p) => (
                <GroupRows
                  key={p.id}
                  group={p}
                  programmes={list.filter((g) => g.kind === 'programme' && g.parentId === p.id)}
                  canManage={canManage}
                  onEdit={(group) => setEditing({ group, kind: group.kind })}
                  onAddProgramme={() => setEditing({ kind: 'programme', parentId: p.id })}
                />
              ))}
              {orphans.map((g) => (
                <GroupRow
                  key={g.id}
                  group={g}
                  depth={0}
                  canManage={canManage}
                  onEdit={() => setEditing({ group: g, kind: g.kind })}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && (
        <GroupDialog
          group={editing.group}
          kind={editing.kind}
          parentId={editing.parentId}
          portfolios={portfolios}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}

function GroupRows({
  group,
  programmes,
  canManage,
  onEdit,
  onAddProgramme,
}: {
  group: PortfolioGroup;
  programmes: PortfolioGroup[];
  canManage: boolean;
  onEdit: (group: PortfolioGroup) => void;
  onAddProgramme: () => void;
}) {
  return (
    <>
      <GroupRow
        group={group}
        depth={0}
        canManage={canManage}
        onEdit={() => onEdit(group)}
        onAdd={onAddProgramme}
      />
      {programmes.map((c) => (
        <GroupRow key={c.id} group={c} depth={1} canManage={canManage} onEdit={() => onEdit(c)} />
      ))}
    </>
  );
}

function GroupRow({
  group,
  depth,
  canManage,
  onEdit,
  onAdd,
}: {
  group: PortfolioGroup;
  depth: number;
  canManage: boolean;
  onEdit: () => void;
  onAdd?: () => void;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const language = i18n.language;
  const name = pickText(language, group.nameAr, group.nameEn);
  return (
    <TableRow data-testid="group-row" data-kind={group.kind}>
      <TableCell>
        <div className={depth ? 'ps-6' : undefined}>
          <span className="font-medium">{name}</span>
          <span className="block text-xs text-muted-foreground" dir="ltr">
            {group.code}
          </span>
          {group.description && (
            <span className="block text-xs text-muted-foreground">{group.description}</span>
          )}
        </div>
      </TableCell>
      <TableCell>
        <StatusChip tone={group.kind === 'portfolio' ? 'info' : 'neutral'}>
          {t(`groups.kinds.${group.kind}`, { defaultValue: group.kind })}
        </StatusChip>
        {group.status !== 'active' && (
          <StatusChip tone="neutral" className="ms-1">
            {t('groups.inactive')}
          </StatusChip>
        )}
      </TableCell>
      <TableCell>{group.owner ? pickText(language, group.owner.nameAr, group.owner.nameEn) : '—'}</TableCell>
      <TableCell className="tabular-nums">{formatNumber(group.projects, language)}</TableCell>
      <TableCell className="tabular-nums">
        {t('percent', { value: formatNumber(Math.round(group.averageProgress * 1000) / 10, language) })}
      </TableCell>
      <TableCell className={group.delayed > 0 ? 'text-danger tabular-nums' : 'tabular-nums'}>
        {formatNumber(group.delayed, language)}
      </TableCell>
      {canManage && (
        <TableCell>
          <div className="flex justify-end gap-1">
            {onAdd && (
              <Button
                variant="ghost"
                size="sm"
                onClick={onAdd}
                aria-label={t('groups.addProgrammeTo', { name })}
              >
                <Plus aria-hidden />
              </Button>
            )}
            <Button variant="ghost" size="sm" onClick={onEdit} aria-label={t('groups.editNamed', { name })}>
              <Pencil aria-hidden />
            </Button>
          </div>
        </TableCell>
      )}
    </TableRow>
  );
}

function GroupDialog({
  group,
  kind,
  parentId,
  portfolios,
  onClose,
}: {
  group?: PortfolioGroup | undefined;
  kind: string;
  parentId?: string | undefined;
  portfolios: PortfolioGroup[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSavePortfolioGroup();
  const [values, setValues] = useState<PortfolioGroupValues>({
    kind: group?.kind ?? kind,
    code: group?.code ?? '',
    nameAr: group?.nameAr ?? '',
    nameEn: group?.nameEn ?? '',
    description: group?.description ?? '',
    parentId: group?.parentId ?? parentId ?? null,
    ownerEmployeeId: group?.owner?.id ?? null,
  });
  const set = (patch: Partial<PortfolioGroupValues>) => setValues((v) => ({ ...v, ...patch }));
  const NONE = 'none';
  const submit = () =>
    save.mutate(
      {
        id: group?.id,
        version: group?.version,
        values: {
          ...values,
          description: values.description?.trim() || null,
          parentId: values.kind === 'programme' ? values.parentId : null,
        },
      },
      {
        onSuccess: () => {
          toast({ title: t('groups.saved') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={
          group
            ? t('groups.edit')
            : t(values.kind === 'portfolio' ? 'groups.newPortfolio' : 'groups.newProgramme')
        }
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!values.code?.trim() || (!values.nameAr?.trim() && !values.nameEn?.trim())}
              data-testid="group-save"
              onClick={submit}
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="group-dialog">
          <FormField label={t('groups.kind')} required>
            <Select
              value={values.kind ?? 'portfolio'}
              disabled={Boolean(group)}
              onValueChange={(v) => set({ kind: v })}
              options={(['portfolio', 'programme'] as const).map((k) => ({
                value: k,
                label: t(`groups.kinds.${k}`),
              }))}
            />
          </FormField>
          <FormField label={t('groups.code')} required>
            <Input
              dir="ltr"
              value={values.code ?? ''}
              maxLength={60}
              onChange={(e) => set({ code: e.target.value })}
            />
          </FormField>
          <FormField label={t('groups.nameAr')}>
            <Input
              dir="rtl"
              lang="ar"
              value={values.nameAr ?? ''}
              onChange={(e) => set({ nameAr: e.target.value })}
            />
          </FormField>
          <FormField label={t('groups.nameEn')}>
            <Input
              dir="ltr"
              lang="en"
              value={values.nameEn ?? ''}
              onChange={(e) => set({ nameEn: e.target.value })}
            />
          </FormField>
          {values.kind === 'programme' && (
            <FormField label={t('groups.parent')} className="md:col-span-2">
              <Select
                value={values.parentId ?? NONE}
                onValueChange={(v) => set({ parentId: v === NONE ? null : v })}
                options={[
                  { value: NONE, label: t('groups.noParent') },
                  ...portfolios.map((p) => ({
                    value: p.id,
                    label: `${p.code} · ${pickText(i18n.language, p.nameAr, p.nameEn)}`,
                  })),
                ]}
              />
            </FormField>
          )}
          <FormField label={t('groups.owner')} className="md:col-span-2">
            <EmployeePicker
              value={values.ownerEmployeeId ?? undefined}
              onChange={(v) => set({ ownerEmployeeId: v ?? null })}
            />
          </FormField>
          <FormField label={t('groups.descriptionField')} className="md:col-span-2">
            <Textarea
              value={values.description ?? ''}
              onChange={(e) => set({ description: e.target.value })}
            />
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
