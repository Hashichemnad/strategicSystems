import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import { useLookupOptions, useLookupValues } from '@/features/settings/lookups';
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
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { SOURCE_TYPE_LOOKUP, useSourceSystems, type SourceSystem } from './api';

const MANAGE = 'integration.source-system.manage';

const empty = (value: string) => (value.trim() === '' ? null : value.trim());

/** Settings → Integrations → Source systems (SPEC-ADDENDUM §B, ADR 0164): the systems external IDs come from. Never deleted. */
export function SourceSystemsPage() {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const [showInactive, setShowInactive] = useState(false);
  const sources = useSourceSystems(showInactive);
  const types = useLookupValues(SOURCE_TYPE_LOOKUP).data ?? [];
  const canManage = useCan(MANAGE);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<SourceSystem | 'new' | null>(null);
  const toggle = useMutation({
    mutationFn: async (source: SourceSystem) =>
      unwrap(
        source.active
          ? await api.POST('/api/v1/integration/source-systems/{id}/deactivate', {
              params: { path: { id: source.id } },
              body: { version: source.version },
            })
          : await api.POST('/api/v1/integration/source-systems/{id}/activate', {
              params: { path: { id: source.id } },
              body: { version: source.version },
            }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['integration', 'source-systems'] }),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const typeLabel = (code: string) => {
    const value = types.find((v) => v.code === code);
    return value ? pickText(i18n.language, value.labelAr, value.labelEn) || code : code;
  };

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('sources.title')}
        description={t('sources.description')}
        actions={
          canManage && (
            <Button onClick={() => setEditing('new')}>
              <Plus aria-hidden />
              {t('sources.create')}
            </Button>
          )
        }
      />
      <Switch checked={showInactive} onCheckedChange={setShowInactive} label={t('sources.showInactive')} />
      {sources.isPending ? (
        <Skeleton className="h-40 rounded-lg" />
      ) : (sources.data ?? []).length === 0 ? (
        <EmptyState title={t('sources.empty')} description={t('sources.emptyHint')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.code')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.type')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.references')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              {canManage && (
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              )}
            </TableRow>
          </TableHead>
          <TableBody>
            {sources.data?.map((source) => (
              <TableRow key={source.id} data-testid={`source-${source.code}`}>
                <TableCell dir="ltr" className="text-start font-mono text-xs">
                  {source.code}
                </TableCell>
                <TableCell>
                  <div className="flex flex-col">
                    <span>{pickText(i18n.language, source.nameAr, source.nameEn)}</span>
                    {source.description && (
                      <span className="text-xs text-muted-foreground">{source.description}</span>
                    )}
                  </div>
                </TableCell>
                <TableCell>{typeLabel(source.typeCode)}</TableCell>
                <TableCell>{source.referenceCount}</TableCell>
                <TableCell>
                  <StatusChip tone={source.active ? 'success' : 'neutral'}>
                    {source.active ? t('status.active') : t('status.inactive')}
                  </StatusChip>
                </TableCell>
                {canManage && (
                  <TableCell className="text-end">
                    <div className="flex justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => setEditing(source)}>
                        {tc('actions.edit')}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        loading={toggle.isPending && toggle.variables.id === source.id}
                        onClick={() => toggle.mutate(source)}
                      >
                        {source.active ? t('sources.deactivate') : t('sources.activate')}
                      </Button>
                    </div>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && (
        <SourceSystemDialog
          source={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: ['integration', 'source-systems'] })}
        />
      )}
    </div>
  );
}

function SourceSystemDialog({
  source,
  onClose,
  onSaved,
}: {
  source?: SourceSystem;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [code, setCode] = useState(source?.code ?? '');
  const [nameAr, setNameAr] = useState(source?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(source?.nameEn ?? '');
  const [typeCode, setTypeCode] = useState(source?.typeCode ?? '-');
  const [description, setDescription] = useState(source?.description ?? '');
  const typeOptions = useLookupOptions(SOURCE_TYPE_LOOKUP, source?.typeCode, t('sources.chooseType'));
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        code: source ? source.code : code.trim(),
        nameAr: empty(nameAr),
        nameEn: empty(nameEn),
        typeCode: typeCode === '-' ? null : typeCode,
        description: empty(description),
        version: source?.version ?? 0,
      };
      return source
        ? unwrap(
            await api.PUT('/api/v1/integration/source-systems/{id}', {
              params: { path: { id: source.id } },
              body,
            }),
          )
        : unwrap(await api.POST('/api/v1/integration/source-systems', { body }));
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={source ? t('sources.edit') : t('sources.create')}
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
          <FormField label={t('fields.code')} required hint={t('sources.codeHint')}>
            <Input
              dir="ltr"
              name="code"
              value={code}
              disabled={!!source}
              onChange={(e) => setCode(e.target.value)}
            />
          </FormField>
          <FormField label={t('fields.type')} required>
            <Select
              value={typeCode}
              onValueChange={setTypeCode}
              options={typeOptions}
              aria-label={t('fields.type')}
            />
          </FormField>
          <FormField label={t('fields.nameAr')}>
            <Input
              dir="rtl"
              lang="ar"
              name="nameAr"
              value={nameAr}
              onChange={(e) => setNameAr(e.target.value)}
            />
          </FormField>
          <FormField label={t('fields.nameEn')}>
            <Input
              dir="ltr"
              lang="en"
              name="nameEn"
              value={nameEn}
              onChange={(e) => setNameEn(e.target.value)}
            />
          </FormField>
          <FormField label={t('fields.description')} className="sm:col-span-2">
            <Textarea
              value={description}
              name="description"
              onChange={(e) => setDescription(e.target.value)}
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}
