import { Cable, TriangleAlert, Unplug } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  CopyField,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
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
  type Tone,
} from '@/ui';
import {
  useMcpClients,
  useMcpSettings,
  useRevokeMcpClient,
  useSaveMcpSettings,
  type McpClient,
  type McpSettings,
} from './api';

const kindTones: Record<string, Tone> = { read: 'info', write: 'warning', approve: 'danger' };

/** Settings → MCP (docs/design/mcp.md §4): the server switch, sign-in for clients, tools and limits, connected clients. */
export function McpSettingsPage() {
  const { t, i18n } = useTranslation('mcp');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const document = useMcpSettings();
  const clients = useMcpClients();
  const save = useSaveMcpSettings();
  const revoke = useRevokeMcpClient();
  const [draft, setDraft] = useState<McpSettings>();
  const [hosts, setHosts] = useState('');
  const [errors, setErrors] = useState<components['schemas']['Problem']['errors']>();
  const [revoking, setRevoking] = useState<McpClient | null>(null);
  useEffect(() => {
    if (!document.data) return;
    setDraft(document.data.settings);
    setHosts(
      (
        document.data.settings.allowedRedirectHosts ?? ['localhost', '127.0.0.1', 'claude.ai', 'claude.com']
      ).join('\n'),
    );
  }, [document.data]);
  if (document.isError) return <StatusBanner tone="danger" title={errorMessage(tc, document.error)} />;
  if (!document.data || !draft) return <Skeleton className="h-96" />;
  const error = (field: string) => {
    const found = errors?.find((e) => e.field === field);
    return found ? translateMessageKey(tc, found.messageKey, found.params ?? undefined) : undefined;
  };
  const tool = (name: string) => draft.tools?.[name] ?? { enabled: null, rateLimitPerMinute: null };
  const setTool = (name: string, value: Partial<ReturnType<typeof tool>>) =>
    setDraft({ ...draft, tools: { ...(draft.tools ?? {}), [name]: { ...tool(name), ...value } } });
  const kindLabel = (kind: string) => (t as unknown as (k: string) => string)(`settings.kinds.${kind}`);
  const submit = () =>
    save.mutate(
      {
        settings: {
          ...draft,
          allowedRedirectHosts: hosts
            .split('\n')
            .map((h) => h.trim())
            .filter(Boolean),
        },
        version: document.data.version,
      },
      {
        onSuccess: () => {
          setErrors(undefined);
          toast({ title: t('settings.saved') });
        },
        onError: (failure) => {
          if (failure instanceof ApiError && failure.problem.errors?.length)
            setErrors(failure.problem.errors);
          else toast({ tone: 'error', title: errorMessage(tc, failure) });
        },
      },
    );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <form
        className="flex flex-col gap-6"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <div className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <Switch
            className="sm:col-span-2"
            checked={draft.enabled}
            onCheckedChange={(enabled) => setDraft({ ...draft, enabled })}
            label={t('settings.enabled')}
            description={t('settings.enabledHint')}
          />
          <CopyField label={t('settings.endpoint')} value={document.data.endpoint} />
          <CopyField label={t('settings.issuer')} value={document.data.issuer} />
          <details className="text-sm sm:col-span-2">
            <summary className="cursor-pointer font-medium text-primary">{t('settings.guide')}</summary>
            <p className="mt-2 text-muted-foreground">{t('settings.guideText')}</p>
            <pre dir="ltr" className="mt-2 overflow-x-auto rounded bg-muted p-3 text-start text-xs">
              {`claude mcp add --transport http strategy ${document.data.endpoint}`}
            </pre>
          </details>
        </div>
        <div className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <h3 className="font-semibold sm:col-span-2">{t('settings.oauth')}</h3>
          <Switch
            className="sm:col-span-2"
            checked={draft.allowDynamicRegistration}
            onCheckedChange={(allowDynamicRegistration) => setDraft({ ...draft, allowDynamicRegistration })}
            label={t('settings.dcr')}
            description={t('settings.dcrHint')}
          />
          <FormField
            label={t('settings.hosts')}
            hint={t('settings.hostsHint')}
            error={error('allowedRedirectHosts')}
            className="sm:col-span-2"
          >
            <Textarea dir="ltr" rows={4} value={hosts} onChange={(e) => setHosts(e.target.value)} />
          </FormField>
          <FormField label={t('settings.accessMinutes')} error={error('accessTokenMinutes')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={draft.accessTokenMinutes}
              onChange={(e) => setDraft({ ...draft, accessTokenMinutes: Number(e.target.value) || 0 })}
            />
          </FormField>
          <FormField label={t('settings.refreshDays')} error={error('refreshTokenDays')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={draft.refreshTokenDays}
              onChange={(e) => setDraft({ ...draft, refreshTokenDays: Number(e.target.value) || 0 })}
            />
          </FormField>
          <FormField label={t('settings.rateLimit')} error={error('rateLimitPerMinute')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={draft.rateLimitPerMinute}
              onChange={(e) => setDraft({ ...draft, rateLimitPerMinute: Number(e.target.value) || 0 })}
            />
          </FormField>
        </div>
        <div className="flex flex-col gap-3">
          <h3 className="font-semibold">{t('settings.tools')}</h3>
          <StatusBanner tone="warning" title={t('settings.approveWarning')} />
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('settings.tool')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.kind')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.toolEnabled')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.toolLimit')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {document.data.tools.map((row) => {
                const current = tool(row.name);
                const enabled = current.enabled ?? row.defaultEnabled;
                return (
                  <TableRow key={row.name} data-testid={`mcp-tool-${row.name}`}>
                    <TableCell>
                      <div className="flex flex-col">
                        <span className="font-medium">
                          {i18n.language === 'ar' ? row.titleAr : row.titleEn}
                        </span>
                        <code dir="ltr" className="text-start text-xs text-muted-foreground">
                          {row.name}
                        </code>
                      </div>
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={kindTones[row.kind] ?? 'neutral'}>
                        {row.kind === 'approve' && <TriangleAlert className="size-3" aria-hidden />}
                        {kindLabel(row.kind)}
                      </StatusChip>
                    </TableCell>
                    <TableCell>
                      <Switch
                        hideLabel
                        label={`${t('settings.toolEnabled')} ${row.name}`}
                        checked={enabled}
                        onCheckedChange={(value) => setTool(row.name, { enabled: value })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        dir="ltr"
                        className="w-28"
                        inputMode="numeric"
                        aria-label={`${t('settings.toolLimit')} ${row.name}`}
                        placeholder={t('settings.default')}
                        value={current.rateLimitPerMinute ?? ''}
                        onChange={(e) =>
                          setTool(row.name, {
                            rateLimitPerMinute: e.target.value === '' ? null : Number(e.target.value),
                          })
                        }
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
        <Button type="submit" loading={save.isPending} className="self-start">
          {tc('actions.save')}
        </Button>
      </form>
      <div className="flex flex-col gap-3">
        <h3 className="font-semibold">{t('settings.clients')}</h3>
        {clients.data && clients.data.length === 0 ? (
          <EmptyState icon={Cable} title={t('settings.clientsEmpty')} />
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('settings.client')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.redirects')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.registered')}</TableHeaderCell>
                <TableHeaderCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {(clients.data ?? []).map((client) => (
                <TableRow key={client.clientId}>
                  <TableCell className="font-medium">{client.name ?? client.clientId}</TableCell>
                  <TableCell dir="ltr" className="text-start text-xs">
                    {client.redirectUris.join(', ')}
                  </TableCell>
                  <TableCell>
                    {client.registeredAt ? formatDateTime(client.registeredAt, i18n.language) : '—'}
                  </TableCell>
                  <TableCell>
                    <Button size="sm" variant="ghost" onClick={() => setRevoking(client)}>
                      <Unplug aria-hidden />
                      {t('settings.revoke')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
      <ConfirmDialog
        open={revoking !== null}
        onOpenChange={(open) => !open && setRevoking(null)}
        title={t('settings.revokeTitle')}
        description={t('settings.revokeDescription', { client: revoking?.name ?? revoking?.clientId ?? '' })}
        confirmLabel={t('settings.revoke')}
        destructive
        loading={revoke.isPending}
        onConfirm={() =>
          revoking &&
          revoke.mutate(revoking.clientId, {
            onSuccess: () => {
              toast({ title: t('settings.revoked') });
              setRevoking(null);
            },
            onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
          })
        }
      />
    </div>
  );
}
