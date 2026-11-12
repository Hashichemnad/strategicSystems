import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  Skeleton,
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
import {
  syncKeys,
  useMappingPacks,
  useObjectMappings,
  useSyncMutation,
  type MappingPackDocument,
  type ObjectMapping,
  type SyncConnection,
} from './api';
import { MappingEditor } from './MappingEditor';
import { Code, isPlainObject, parseJson, useSyncT } from './shared';

/** Object mappings of the connection's pack: order, source → target, enable (only once validated), editor, export / import. */
export function MappingsTab({ connection, canManage }: { connection: SyncConnection; canManage: boolean }) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const packId = connection.mappingPackId;
  const packs = useMappingPacks();
  const mappings = useObjectMappings(packId);
  const [editing, setEditing] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const pack = packs.data?.find((p) => p.id === packId);
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const enable = useSyncMutation(async (input: { mapping: ObjectMapping; enabled: boolean }) =>
    unwrap(
      await api.POST('/api/v1/integration/sync/mappings/{mappingId}/enabled', {
        params: { path: { mappingId: input.mapping.id } },
        body: { enabled: input.enabled, version: input.mapping.version },
      }),
    ),
  );
  const importPack = useSyncMutation(async (document: MappingPackDocument) =>
    unwrap(await api.POST('/api/v1/integration/sync/packs/import', { body: document })),
  );
  const exportPack = async () => {
    if (!packId) return;
    try {
      const document = unwrap(
        await api.GET('/api/v1/integration/sync/packs/{packId}/export', { params: { path: { packId } } }),
      );
      const blob = new Blob([JSON.stringify(document, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = window.document.createElement('a');
      link.href = url;
      link.download = `${document.code ?? 'mapping-pack'}-v${document.version ?? 1}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (failure) {
      onError(failure);
    }
  };
  const readImport = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    const document = parseJson(await file.text(), isPlainObject) as MappingPackDocument | undefined;
    if (!document) {
      toast({ tone: 'error', title: t('sync.mappings.importInvalid') });
      return;
    }
    importPack.mutate(document, {
      onSuccess: () => toast({ tone: 'success', title: t('sync.mappings.imported') }),
      onError,
    });
  };
  if (!packId) return <EmptyState className="mt-4" title={t('sync.mappings.noPack')} />;
  return (
    <div className="flex flex-col gap-4 pt-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="font-medium">
            {pack
              ? `${pickText(language, pack.nameAr, pack.nameEn) || pack.code} · v${pack.packVersion}`
              : ''}
          </span>
          <span className="text-sm text-muted-foreground">{t('sync.mappings.hint')}</span>
        </div>
        {canManage && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => void exportPack()}>
              {t('sync.mappings.export')}
            </Button>
            <Button
              variant="outline"
              loading={importPack.isPending}
              onClick={() => fileInput.current?.click()}
            >
              {t('sync.mappings.import')}
            </Button>
            <input
              ref={fileInput}
              type="file"
              accept="application/json,.json"
              className="hidden"
              aria-label={t('sync.mappings.import')}
              onChange={(e) => void readImport(e)}
            />
          </div>
        )}
      </div>
      {mappings.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell className="text-end">{t('sync.mappings.order')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.objects.object')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.mappings.kind')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.objects.target')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.objects.state')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.mappings.enabled')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.mappings.notes')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(mappings.data ?? []).map((m) => (
              <TableRow key={m.id} data-testid={`mapping-${m.key}`}>
                <TableCell className="text-end tabular-nums">{formatNumber(m.loadOrder, language)}</TableCell>
                <TableCell className="min-w-48 whitespace-nowrap">
                  <div className="flex flex-col gap-0.5">
                    <Code>{m.key}</Code>
                    <span className="text-xs text-muted-foreground">
                      <Code>{m.sourceObject}</Code>
                    </span>
                  </div>
                </TableCell>
                <TableCell>{tx(`sync.objectKinds.${m.kind}`, { defaultValue: m.kind })}</TableCell>
                <TableCell>{m.targetType ? <Code>{m.targetType}</Code> : '—'}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    {m.confirmedAt ? (
                      <StatusChip tone="info">{t('sync.objects.confirmed')}</StatusChip>
                    ) : (
                      <StatusChip tone="warning">{t('sync.objects.notConfirmed')}</StatusChip>
                    )}
                    {m.provisional && <StatusChip tone="warning">{t('sync.objects.provisional')}</StatusChip>}
                  </div>
                </TableCell>
                <TableCell>
                  <Switch
                    hideLabel
                    label={t('sync.mappings.enableObject', { object: m.key })}
                    checked={m.enabled}
                    disabled={!canManage || (!m.enabled && !m.confirmedAt) || enable.isPending}
                    onCheckedChange={(enabled) => enable.mutate({ mapping: m, enabled }, { onError })}
                  />
                </TableCell>
                <TableCell className="max-w-sm text-xs text-muted-foreground">
                  {pickText(language, m.notesAr, m.notesEn)}
                </TableCell>
                <TableCell className="text-end">
                  <Button variant="ghost" size="sm" onClick={() => setEditing(m.id)}>
                    {canManage ? tc('actions.edit') : t('sync.mappings.view')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && (
        <MappingEditor
          connectionId={connection.id}
          mappingId={editing}
          canManage={canManage}
          onClose={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: syncKeys.mappings(packId) });
          }}
        />
      )}
    </div>
  );
}
