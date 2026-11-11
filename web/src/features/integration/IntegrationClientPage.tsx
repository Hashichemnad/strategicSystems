import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  CopyField,
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import {
  CLIENT_MANAGE,
  WEBHOOK_MANAGE,
  clientKeys,
  useClientCredentials,
  useClientRoles,
  useClientUsage,
  useExportSchedules,
  useIntegrationCatalogue,
  useIntegrationClient,
  useWebhookSubscriptions,
  type IntegrationClient,
  type WebhookSubscription,
} from './clients';
import { ClientDialog, SecretDialog, stateTone, useSystemName } from './IntegrationClientsPage';
import { DeliveryLog } from './WebhookDeliveriesPage';

/** One integration client (ADR 0191): details, permissions (roles = data scope), credentials, webhooks, deliveries, usage, exports. */
export function IntegrationClientPage() {
  const { clientId } = useParams({ from: '/app/settings/integrations/clients/$clientId' });
  const { t, i18n } = useTranslation('integration');
  const client = useIntegrationClient(clientId);
  const canWebhooks = useCan(WEBHOOK_MANAGE);
  if (client.isPending) return <Skeleton className="h-64 rounded-lg" />;
  if (!client.data) return <EmptyState title={t('clients.notFound')} />;
  const c = client.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={pickText(i18n.language, c.nameAr, c.nameEn)}
        description={c.description ?? undefined}
        meta={
          <>
            <StatusChip tone={stateTone[c.state] ?? 'neutral'}>
              {t(`clients.states.${c.state}` as 'clients.states.active')}
            </StatusChip>
            <span dir="ltr" className="font-mono text-xs text-muted-foreground">
              {c.clientId}
            </span>
          </>
        }
        actions={
          <Button variant="outline" asChild>
            <Link to="/settings/integrations/clients">{t('clients.back')}</Link>
          </Button>
        }
      />
      <Tabs defaultValue="details">
        <TabsList>
          <TabsTrigger value="details">{t('clients.tabs.details')}</TabsTrigger>
          <TabsTrigger value="permissions">{t('clients.tabs.permissions')}</TabsTrigger>
          <TabsTrigger value="credentials">{t('clients.tabs.credentials')}</TabsTrigger>
          {canWebhooks && <TabsTrigger value="webhooks">{t('clients.tabs.webhooks')}</TabsTrigger>}
          {canWebhooks && <TabsTrigger value="deliveries">{t('clients.tabs.deliveries')}</TabsTrigger>}
          <TabsTrigger value="usage">{t('clients.tabs.usage')}</TabsTrigger>
          <TabsTrigger value="exports">{t('clients.tabs.exports')}</TabsTrigger>
        </TabsList>
        <TabsContent value="details">
          <DetailsTab client={c} />
        </TabsContent>
        <TabsContent value="permissions">
          <PermissionsTab client={c} />
        </TabsContent>
        <TabsContent value="credentials">
          <CredentialsTab client={c} />
        </TabsContent>
        {canWebhooks && (
          <TabsContent value="webhooks">
            <WebhooksTab client={c} />
          </TabsContent>
        )}
        {canWebhooks && (
          <TabsContent value="deliveries">
            <DeliveryLog clientId={c.id} />
          </TabsContent>
        )}
        <TabsContent value="usage">
          <UsageTab client={c} />
        </TabsContent>
        <TabsContent value="exports">
          <ExportsTab client={c} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}

function DetailsTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const catalogue = useIntegrationCatalogue().data;
  const systemName = useSystemName(catalogue);
  const canManage = useCan(CLIENT_MANAGE) && client.state !== 'revoked';
  const [editing, setEditing] = useState(false);
  const [revoking, setRevoking] = useState(false);
  const [reason, setReason] = useState('');
  const queryClient = useQueryClient();
  const toast = useToast();
  const revoke = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{id}/revoke', {
          params: { path: { id: client.id } },
          body: { reason, version: client.version },
        }),
      ),
    onSuccess: () => {
      setRevoking(false);
      void queryClient.invalidateQueries({ queryKey: clientKeys.all });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const typeLabel = (code: string) => {
    for (const system of catalogue ?? []) {
      const type = system.recordTypes.find((r) => r.code === code);
      if (type) return pickText(i18n.language, type.labelAr, type.labelEn);
    }
    return code;
  };
  return (
    <div className="flex flex-col gap-4 pt-4">
      <dl className="grid gap-4 sm:grid-cols-3">
        <Fact label={t('clients.kind')}>
          {t(`clients.kinds.${client.authKind}` as 'clients.kinds.api_key')}
        </Fact>
        <Fact label={t('clients.scopes')}>
          {client.scopes
            .map((s) => t(`clients.scopeNames.${s === 'feeds' ? 'feeds' : 'apiRead'}`))
            .join('، ')}
        </Fact>
        <Fact label={t('clients.actingUnit')}>
          {pickText(i18n.language, client.actingUnitNameAr, client.actingUnitNameEn) || t('clients.noUnit')}
        </Fact>
        <Fact label={t('clients.systems')}>{client.allowedSystems.map(systemName).join('، ')}</Fact>
        <Fact label={t('clients.recordTypes')}>
          {client.allowedRecordTypes.length === 0
            ? t('clients.allRecordTypes')
            : client.allowedRecordTypes.map(typeLabel).join('، ')}
        </Fact>
        <Fact label={t('clients.ipAllowList')}>
          <span dir="ltr">
            {client.ipAllowList.length === 0 ? t('clients.anyAddress') : client.ipAllowList.join(', ')}
          </span>
        </Fact>
        <Fact label={t('clients.rateLimit')}>
          {t('clients.perMinute', { count: client.rateLimitPerMinute })}
        </Fact>
        <Fact label={t('clients.expires')}>{formatDate(client.expiresAt, i18n.language)}</Fact>
        <Fact label={t('clients.lastUsed')}>
          {client.lastUsedAt
            ? `${formatDateTime(client.lastUsedAt, i18n.language)} · ${client.lastUsedIp ?? ''}`
            : t('clients.never')}
        </Fact>
        {client.revokedAt && (
          <Fact label={t('clients.revokedOn')}>
            {formatDateTime(client.revokedAt, i18n.language)} — {client.revokeReason}
          </Fact>
        )}
      </dl>
      {canManage && (
        <div className="flex gap-2">
          <Button onClick={() => setEditing(true)}>{tc('actions.edit')}</Button>
          <Button variant="danger" onClick={() => setRevoking(true)}>
            {t('clients.revoke')}
          </Button>
        </div>
      )}
      {editing && <ClientDialog client={client} onClose={() => setEditing(false)} />}
      {revoking && (
        <Dialog open onOpenChange={(open) => !open && setRevoking(false)}>
          <DialogContent
            title={t('clients.revoke')}
            description={t('clients.revokeWarning')}
            footer={
              <>
                <DialogClose asChild>
                  <Button variant="outline">{tc('actions.cancel')}</Button>
                </DialogClose>
                <Button variant="danger" loading={revoke.isPending} onClick={() => revoke.mutate()}>
                  {t('clients.revoke')}
                </Button>
              </>
            }
          >
            <FormField label={t('clients.reason')} required>
              <Textarea name="reason" value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
          </DialogContent>
        </Dialog>
      )}
    </div>
  );
}

function PermissionsTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('access');
  const roles = useClientRoles(client.id);
  const canManage = useCan(CLIENT_MANAGE) && client.state !== 'revoked';
  const queryClient = useQueryClient();
  const toast = useToast();
  const [selected, setSelected] = useState<string[] | null>(null);
  const current = selected ?? (roles.data ?? []).filter((r) => r.assigned).map((r) => r.roleId);
  const systemName = useSystemName(useIntegrationCatalogue().data);
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/v1/integration/clients/{id}/roles', {
          params: { path: { id: client.id } },
          body: { roleIds: current },
        }),
      ),
    onSuccess: (saved) => {
      queryClient.setQueryData(clientKeys.part(client.id, 'roles'), saved);
      setSelected(null);
      toast({ tone: 'success', title: tc('status.saved') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  if (roles.isPending) return <Skeleton className="mt-4 h-40 rounded-lg" />;
  return (
    <div className="flex flex-col gap-4 pt-4">
      <p className="text-sm text-muted-foreground">{t('clients.permissionsHint')}</p>
      {(roles.data ?? []).length === 0 ? (
        <EmptyState title={t('clients.noRoles')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>
                <span className="sr-only">{t('clients.assigned')}</span>
              </TableHeaderCell>
              <TableHeaderCell>{t('clients.role')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.system')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.grants')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {roles.data?.map((role) => (
              <TableRow key={role.roleId} data-testid={`role-${role.code}`}>
                <TableCell>
                  <Checkbox
                    label={
                      <span className="sr-only">{pickText(i18n.language, role.nameAr, role.nameEn)}</span>
                    }
                    disabled={!canManage}
                    checked={current.includes(role.roleId)}
                    onChange={(e) =>
                      setSelected(
                        e.target.checked
                          ? [...current, role.roleId]
                          : current.filter((id) => id !== role.roleId),
                      )
                    }
                  />
                </TableCell>
                <TableCell>{pickText(i18n.language, role.nameAr, role.nameEn) || role.code}</TableCell>
                <TableCell>{systemName(role.systemCode)}</TableCell>
                <TableCell>
                  <ul className="flex flex-col gap-0.5 text-xs">
                    {role.permissions.map((p) => (
                      <li key={p.permission}>
                        <span dir="ltr" className="font-mono">
                          {p.permission}
                        </span>{' '}
                        <StatusChip tone="info">{ta(`scopes.${p.scope}` as 'scopes.all')}</StatusChip>
                      </li>
                    ))}
                  </ul>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {canManage && (
        <div>
          <Button loading={save.isPending} disabled={selected === null} onClick={() => save.mutate()}>
            {tc('actions.save')}
          </Button>
        </div>
      )}
    </div>
  );
}

const credentialTone: Record<string, Tone> = {
  active: 'success',
  ending: 'warning',
  expired: 'neutral',
  revoked: 'neutral',
};

function CredentialsTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const credentials = useClientCredentials(client.id);
  const canManage = useCan(CLIENT_MANAGE) && client.state !== 'revoked';
  const queryClient = useQueryClient();
  const toast = useToast();
  const [overlap, setOverlap] = useState('7');
  const [secret, setSecret] = useState<string | null>(null);
  const [revoking, setRevoking] = useState<string | null>(null);
  const refresh = () =>
    void queryClient.invalidateQueries({ queryKey: clientKeys.part(client.id, 'credentials') });
  const rotate = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{id}/credentials/rotate', {
          params: { path: { id: client.id } },
          body: { overlapDays: client.authKind === 'oauth' ? 0 : Number(overlap) },
        }),
      ),
    onSuccess: (rotated) => {
      setSecret(rotated.secret);
      refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const revoke = useMutation({
    mutationFn: async (credentialId: string) =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{id}/credentials/{credentialId}/revoke', {
          params: { path: { id: client.id, credentialId } },
        }),
      ),
    onSuccess: () => {
      setRevoking(null);
      refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-4 pt-4">
      {canManage && (
        <div className="flex flex-wrap items-end gap-3">
          {client.authKind !== 'oauth' && (
            <FormField label={t('clients.overlapDays')} hint={t('clients.overlapHint')}>
              <Input
                type="number"
                min={0}
                max={30}
                name="overlapDays"
                value={overlap}
                onChange={(e) => setOverlap(e.target.value)}
              />
            </FormField>
          )}
          <Button loading={rotate.isPending} onClick={() => rotate.mutate()}>
            {t('clients.rotate')}
          </Button>
          {client.authKind === 'oauth' && (
            <p className="text-sm text-muted-foreground">{t('clients.oauthRotateHint')}</p>
          )}
        </div>
      )}
      {credentials.isPending ? (
        <Skeleton className="h-32 rounded-lg" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('clients.prefix')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.created')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.ends')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.lastUsed')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              {canManage && (
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              )}
            </TableRow>
          </TableHead>
          <TableBody>
            {credentials.data?.map((c) => (
              <TableRow key={c.id}>
                <TableCell dir="ltr" className="text-start font-mono text-xs">
                  {(c.kind === 'secret' ? 'sis_' : 'sik_') + c.prefix + '_…'}
                </TableCell>
                <TableCell>{formatDateTime(c.createdAt, i18n.language)}</TableCell>
                <TableCell>{c.expiresAt ? formatDateTime(c.expiresAt, i18n.language) : '—'}</TableCell>
                <TableCell>
                  {c.lastUsedAt ? formatDateTime(c.lastUsedAt, i18n.language) : t('clients.never')}
                </TableCell>
                <TableCell>
                  <StatusChip tone={credentialTone[c.state] ?? 'neutral'}>
                    {t(`clients.credentialStates.${c.state}` as 'clients.credentialStates.active')}
                  </StatusChip>
                </TableCell>
                {canManage && (
                  <TableCell className="text-end">
                    {(c.state === 'active' || c.state === 'ending') && (
                      <Button variant="ghost" size="sm" onClick={() => setRevoking(c.id)}>
                        {t('clients.revokeCredential')}
                      </Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {secret && (
        <SecretDialog
          clientId={client.clientId}
          secret={secret}
          kind={client.authKind}
          onClose={() => setSecret(null)}
        />
      )}
      <ConfirmDialog
        open={revoking !== null}
        onOpenChange={(open) => !open && setRevoking(null)}
        title={t('clients.revokeCredential')}
        description={t('clients.revokeCredentialWarning')}
        confirmLabel={t('clients.revokeCredential')}
        destructive
        onConfirm={() => revoking && revoke.mutate(revoking)}
      />
    </div>
  );
}

function WebhooksTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const subscriptions = useWebhookSubscriptions(client.id);
  const canManage = client.state !== 'revoked';
  const [editing, setEditing] = useState<WebhookSubscription | 'new' | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const refresh = () =>
    void queryClient.invalidateQueries({ queryKey: clientKeys.part(client.id, 'webhooks') });
  const regenerate = useMutation({
    mutationFn: async (id: string) =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{clientId}/webhooks/{id}/secret', {
          params: { path: { clientId: client.id, id } },
        }),
      ),
    onSuccess: (result) => setSecret(result.secret),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const test = useMutation({
    mutationFn: async (id: string) =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{clientId}/webhooks/{id}/test', {
          params: { path: { clientId: client.id, id } },
        }),
      ),
    onSuccess: () => {
      toast({ tone: 'success', title: t('webhooks.testQueued') });
      void queryClient.invalidateQueries({ queryKey: ['integration', 'deliveries'] });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-4 pt-4">
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t('webhooks.hint')}</p>
        {canManage && <Button onClick={() => setEditing('new')}>{t('webhooks.create')}</Button>}
      </div>
      {subscriptions.isPending ? (
        <Skeleton className="h-32 rounded-lg" />
      ) : (subscriptions.data ?? []).length === 0 ? (
        <EmptyState title={t('webhooks.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('webhooks.name')}</TableHeaderCell>
              <TableHeaderCell>{t('webhooks.url')}</TableHeaderCell>
              <TableHeaderCell>{t('webhooks.events')}</TableHeaderCell>
              <TableHeaderCell>{t('webhooks.queue')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {subscriptions.data?.map((s) => (
              <TableRow key={s.id} data-testid={`webhook-${s.name}`}>
                <TableCell>{s.name}</TableCell>
                <TableCell dir="ltr" className="max-w-xs truncate text-start font-mono text-xs">
                  {s.targetUrl}
                </TableCell>
                <TableCell>
                  <ul dir="ltr" className="text-start font-mono text-xs">
                    {s.events.map((e) => (
                      <li key={e}>{e}</li>
                    ))}
                  </ul>
                </TableCell>
                <TableCell>
                  {t('webhooks.pendingDead', {
                    pending: formatNumber(s.pending, i18n.language),
                    dead: formatNumber(s.dead, i18n.language),
                  })}
                </TableCell>
                <TableCell>
                  <StatusChip tone={s.active ? 'success' : 'neutral'}>
                    {s.active ? t('status.active') : t('status.inactive')}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  {canManage && (
                    <div className="flex justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => setEditing(s)}>
                        {tc('actions.edit')}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        loading={test.isPending && test.variables === s.id}
                        onClick={() => test.mutate(s.id)}
                      >
                        {t('webhooks.sendTest')}
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => regenerate.mutate(s.id)}>
                        {t('webhooks.regenerate')}
                      </Button>
                    </div>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && (
        <SubscriptionDialog
          client={client}
          subscription={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={(newSecret) => {
            setEditing(null);
            refresh();
            if (newSecret) setSecret(newSecret);
          }}
        />
      )}
      {secret && (
        <Dialog open onOpenChange={(open) => !open && setSecret(null)}>
          <DialogContent
            title={t('webhooks.secretTitle')}
            description={t('clients.secretOnce')}
            footer={
              <DialogClose asChild>
                <Button>{tc('actions.close')}</Button>
              </DialogClose>
            }
          >
            <CopyField value={secret} label={t('webhooks.secret')} />
            <p className="text-sm text-muted-foreground">{t('webhooks.verifyHint')}</p>
          </DialogContent>
        </Dialog>
      )}
    </div>
  );
}

function SubscriptionDialog({
  client,
  subscription,
  onClose,
  onSaved,
}: {
  client: IntegrationClient;
  subscription?: WebhookSubscription;
  onClose: () => void;
  onSaved: (secret?: string) => void;
}) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const catalogue = (useIntegrationCatalogue().data ?? []).filter((s) =>
    client.allowedSystems.includes(s.code),
  );
  const [name, setName] = useState(subscription?.name ?? '');
  const [url, setUrl] = useState(subscription?.targetUrl ?? 'https://');
  const [events, setEvents] = useState<string[]>(subscription?.events ?? []);
  const [active, setActive] = useState(subscription?.active ?? true);
  const save = useMutation({
    mutationFn: async () => {
      const body = { name, targetUrl: url, events, active, version: subscription?.version ?? 0 };
      if (subscription) {
        unwrap(
          await api.PUT('/api/v1/integration/clients/{clientId}/webhooks/{id}', {
            params: { path: { clientId: client.id, id: subscription.id } },
            body,
          }),
        );
        return undefined;
      }
      return unwrap(
        await api.POST('/api/v1/integration/clients/{clientId}/webhooks', {
          params: { path: { clientId: client.id } },
          body,
        }),
      ).secret;
    },
    onSuccess: (secret) => onSaved(secret),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className="max-w-2xl"
        title={subscription ? t('webhooks.edit') : t('webhooks.create')}
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
        <div className="flex flex-col gap-4">
          <FormField label={t('webhooks.name')} required>
            <Input name="name" value={name} onChange={(e) => setName(e.target.value)} />
          </FormField>
          <FormField label={t('webhooks.url')} required hint={t('webhooks.urlHint')}>
            <Input dir="ltr" name="targetUrl" value={url} onChange={(e) => setUrl(e.target.value)} />
          </FormField>
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-medium">{t('webhooks.events')}</legend>
            {catalogue.map((system) =>
              system.events.length === 0 ? null : (
                <div key={system.code} className="flex flex-col gap-1">
                  <span className="text-xs font-semibold text-muted-foreground">
                    {pickText(i18n.language, system.nameAr, system.nameEn)}
                  </span>
                  {system.events.map((e) => (
                    <Checkbox
                      key={e.code}
                      label={pickText(i18n.language, e.labelAr, e.labelEn)}
                      description={e.code}
                      checked={events.includes(e.code)}
                      onChange={(ev) =>
                        setEvents(
                          ev.target.checked ? [...events, e.code] : events.filter((x) => x !== e.code),
                        )
                      }
                    />
                  ))}
                </div>
              ),
            )}
          </fieldset>
          {subscription && (
            <Checkbox
              label={t('status.active')}
              checked={active}
              onChange={(e) => setActive(e.target.checked)}
            />
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function UsageTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const [page, setPage] = useState(1);
  const usage = useClientUsage(client.id, page);
  if (usage.isPending) return <Skeleton className="mt-4 h-40 rounded-lg" />;
  const log = usage.data?.log;
  const days = usage.data?.days ?? [];
  return (
    <div className="flex flex-col gap-4 pt-4">
      <dl className="grid gap-4 sm:grid-cols-3">
        <Fact label={t('usage.calls30')}>
          {formatNumber(
            days.reduce((s, d) => s + d.calls, 0),
            i18n.language,
          )}
        </Fact>
        <Fact label={t('usage.refused30')}>
          {formatNumber(
            days.reduce((s, d) => s + d.refused, 0),
            i18n.language,
          )}
        </Fact>
        <Fact label={t('usage.rows30')}>
          {formatNumber(
            days.reduce((s, d) => s + d.rows, 0),
            i18n.language,
          )}
        </Fact>
      </dl>
      {(log?.items ?? []).length === 0 ? (
        <EmptyState title={t('usage.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('usage.at')}</TableHeaderCell>
              <TableHeaderCell>{t('usage.request')}</TableHeaderCell>
              <TableHeaderCell>{t('usage.status')}</TableHeaderCell>
              <TableHeaderCell>{t('usage.rows')}</TableHeaderCell>
              <TableHeaderCell>{t('usage.duration')}</TableHeaderCell>
              <TableHeaderCell>{t('usage.ip')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {log?.items.map((row, index) => (
              <TableRow key={`${row.at}-${index}`}>
                <TableCell>{formatDateTime(row.at, i18n.language)}</TableCell>
                <TableCell dir="ltr" className="text-start font-mono text-xs">
                  {row.method} {row.path}
                </TableCell>
                <TableCell>
                  <StatusChip tone={row.statusCode < 400 ? 'success' : 'danger'}>
                    {row.refusedReason
                      ? t(`usage.refused.${row.refusedReason}` as 'usage.refused.ip')
                      : row.statusCode}
                  </StatusChip>
                </TableCell>
                <TableCell>{row.rows ?? '—'}</TableCell>
                <TableCell>{t('usage.ms', { count: row.durationMs })}</TableCell>
                <TableCell dir="ltr" className="text-start text-xs">
                  {row.ip}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {log && log.totalCount > log.pageSize && (
        <div className="flex gap-2">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {t('pager.previous')}
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={page * log.pageSize >= log.totalCount}
            onClick={() => setPage(page + 1)}
          >
            {t('pager.next')}
          </Button>
        </div>
      )}
    </div>
  );
}

function ExportsTab({ client }: { client: IntegrationClient }) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const schedules = useExportSchedules(client.id);
  const catalogue = (useIntegrationCatalogue().data ?? []).filter((s) =>
    client.allowedSystems.includes(s.code),
  );
  const canManage = useCan(CLIENT_MANAGE) && client.state !== 'revoked';
  const queryClient = useQueryClient();
  const toast = useToast();
  const [name, setName] = useState('');
  const [folder, setFolder] = useState('');
  const [format, setFormat] = useState('json');
  const [hours, setHours] = useState('24');
  const [types, setTypes] = useState<string[]>([]);
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/integration/clients/{clientId}/exports', {
          params: { path: { clientId: client.id } },
          body: {
            name,
            recordTypes: types,
            format,
            folder,
            everyHours: Number(hours),
            active: true,
            version: 0,
          },
        }),
      ),
    onSuccess: () => {
      setName('');
      setTypes([]);
      void queryClient.invalidateQueries({ queryKey: clientKeys.part(client.id, 'exports') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-4 pt-4">
      <p className="text-sm text-muted-foreground">{t('exports.hint')}</p>
      {(schedules.data ?? []).length > 0 && (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('exports.name')}</TableHeaderCell>
              <TableHeaderCell>{t('exports.folder')}</TableHeaderCell>
              <TableHeaderCell>{t('exports.every')}</TableHeaderCell>
              <TableHeaderCell>{t('exports.lastRun')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {schedules.data?.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  {s.name} <span className="text-xs text-muted-foreground">({s.format.toUpperCase()})</span>
                </TableCell>
                <TableCell dir="ltr" className="text-start font-mono text-xs">
                  {s.folder}
                </TableCell>
                <TableCell>{t('exports.hours', { count: s.everyHours })}</TableCell>
                <TableCell>
                  {s.lastRunAt
                    ? `${formatDateTime(s.lastRunAt, i18n.language)} · ${s.lastRunStatus ?? ''}`
                    : '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {canManage && (
        <div className="grid gap-3 rounded-lg border p-4 sm:grid-cols-2">
          <FormField label={t('exports.name')}>
            <Input name="exportName" value={name} onChange={(e) => setName(e.target.value)} />
          </FormField>
          <FormField label={t('exports.folder')} hint={t('exports.folderHint')}>
            <Input dir="ltr" name="folder" value={folder} onChange={(e) => setFolder(e.target.value)} />
          </FormField>
          <FormField label={t('exports.format')}>
            <Select
              value={format}
              onValueChange={setFormat}
              aria-label={t('exports.format')}
              options={[
                { value: 'json', label: 'JSON' },
                { value: 'csv', label: 'CSV' },
              ]}
            />
          </FormField>
          <FormField label={t('exports.every')}>
            <Input
              type="number"
              min={1}
              max={168}
              name="everyHours"
              value={hours}
              onChange={(e) => setHours(e.target.value)}
            />
          </FormField>
          <fieldset className="flex flex-col gap-1 sm:col-span-2">
            <legend className="mb-1 text-sm font-medium">{t('clients.recordTypes')}</legend>
            {catalogue.flatMap((s) =>
              s.recordTypes.map((r) => (
                <Checkbox
                  key={r.code}
                  label={pickText(i18n.language, r.labelAr, r.labelEn)}
                  checked={types.includes(r.code)}
                  onChange={(e) =>
                    setTypes(e.target.checked ? [...types, r.code] : types.filter((x) => x !== r.code))
                  }
                />
              )),
            )}
          </fieldset>
          <div>
            <Button loading={create.isPending} onClick={() => create.mutate()}>
              {t('exports.create')}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
