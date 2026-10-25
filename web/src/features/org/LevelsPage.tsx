import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  PageHeader,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { nullIfEmpty, orgKeys, useLevels, type OrgLevel } from './api';

/** Configurable level names (Organisation, Sector, Department, …) and the level-order rule. */
export function LevelsPage() {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const levels = useLevels();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<OrgLevel | 'new' | null>(null);
  const settings = useQuery({
    queryKey: ['org', 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/org/settings', { signal })),
  });
  const saveSettings = useMutation({
    mutationFn: async (enforce: boolean) =>
      unwrap(
        await api.PUT('/api/v1/org/settings', {
          body: { enforceLevelOrder: enforce, version: settings.data?.version ?? 0 },
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['org', 'settings'] }),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('levels.title')}
        description={t('levels.description')}
        actions={
          <Button onClick={() => setEditing('new')}>
            <Plus aria-hidden />
            {t('levels.create')}
          </Button>
        }
      />
      {settings.data && (
        <div className="rounded-lg border bg-card p-4">
          <Switch
            checked={settings.data.enforceLevelOrder}
            onCheckedChange={(value) => saveSettings.mutate(value)}
            disabled={saveSettings.isPending}
            label={t('levels.enforceOrder')}
            description={t('levels.enforceOrderHint')}
          />
        </div>
      )}
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('levels.sort')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{tc('actions.more')}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {levels.data?.map((level) => (
            <TableRow key={level.id}>
              <TableCell>{level.sort}</TableCell>
              <TableCell dir="ltr" className="text-start font-mono text-xs">
                {level.code}
              </TableCell>
              <TableCell>{pickText(i18n.language, level.nameAr, level.nameEn)}</TableCell>
              <TableCell>
                <StatusChip tone={level.active ? 'success' : 'neutral'}>
                  {level.active ? t('status.active') : t('status.inactive')}
                </StatusChip>
              </TableCell>
              <TableCell className="text-end">
                <Button variant="ghost" size="sm" onClick={() => setEditing(level)}>
                  {tc('actions.edit')}
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {editing && (
        <LevelDialog
          level={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: orgKeys.levels })}
        />
      )}
    </div>
  );
}

function LevelDialog({
  level,
  onClose,
  onSaved,
}: {
  level?: OrgLevel;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation('org');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [code, setCode] = useState(level?.code ?? '');
  const [nameAr, setNameAr] = useState(level?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(level?.nameEn ?? '');
  const [sort, setSort] = useState(String(level?.sort ?? 60));
  const [active, setActive] = useState(level?.active ?? true);
  const save = useMutation({
    mutationFn: async () =>
      level
        ? unwrap(
            await api.PUT('/api/v1/org/levels/{id}', {
              params: { path: { id: level.id } },
              body: {
                nameAr: nullIfEmpty(nameAr),
                nameEn: nullIfEmpty(nameEn),
                sort: Number(sort),
                active,
                version: level.version,
              },
            }),
          )
        : unwrap(
            await api.POST('/api/v1/org/levels', {
              body: { code, nameAr: nullIfEmpty(nameAr), nameEn: nullIfEmpty(nameEn), sort: Number(sort) },
            }),
          ),
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={level ? t('levels.edit') : t('levels.create')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={() => save.mutate()}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('fields.code')} required hint={t('hints.levelCode')}>
            <Input dir="ltr" value={code} disabled={!!level} onChange={(e) => setCode(e.target.value)} />
          </FormField>
          <FormField label={t('levels.sort')}>
            <Input type="number" dir="ltr" value={sort} onChange={(e) => setSort(e.target.value)} />
          </FormField>
          <FormField label={t('fields.nameAr')}>
            <Input dir="rtl" lang="ar" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('fields.nameEn')}>
            <Input dir="ltr" lang="en" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          {level && (
            <Switch
              checked={active}
              onCheckedChange={setActive}
              label={t('status.active')}
              className="sm:col-span-2"
            />
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
