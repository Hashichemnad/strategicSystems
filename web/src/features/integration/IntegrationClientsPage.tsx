import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { KeyRound, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { UnitSelect } from '@/features/access/pickers';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
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
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import {
  CLIENT_MANAGE,
  clientKeys,
  useIntegrationCatalogue,
  useIntegrationClients,
  type CatalogueSystem,
  type IntegrationClient,
} from './clients';

export const stateTone: Record<string, Tone> = { active: 'success', expired: 'warning', revoked: 'neutral' };

const empty = (value: string) => (value.trim() === '' ? null : value.trim());

/** Settings → Integrations → Clients (SPEC-ADDENDUM §F, ADR 0191): service accounts of other systems. */
export function IntegrationClientsPage() {
  const { t, i18n } = useTranslation('integration');
  const clients = useIntegrationClients();
  const catalogue = useIntegrationCatalogue();
  const canManage = useCan(CLIENT_MANAGE);
  const [creating, setCreating] = useState(false);
  const [secret, setSecret] = useState<{ clientId: string; secret: string; kind: string } | null>(null);
  const systemName = useSystemName(catalogue.data);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('clients.title')}
        description={t('clients.description')}
        actions={
          canManage && (
            <Button onClick={() => setCreating(true)}>
              <Plus aria-hidden />
              {t('clients.create')}
            </Button>
          )
        }
      />
      {clients.isPending ? (
        <Skeleton className="h-40 rounded-lg" />
      ) : (clients.data ?? []).length === 0 ? (
        <EmptyState icon={KeyRound} title={t('clients.empty')} description={t('clients.emptyHint')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('clients.name')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.kind')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.systems')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.expires')}</TableHeaderCell>
              <TableHeaderCell>{t('clients.lastUsed')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {clients.data?.map((client) => (
              <TableRow key={client.id} data-testid={`client-${client.clientId}`}>
                <TableCell>
                  <Link
                    to="/settings/integrations/clients/$clientId"
                    params={{ clientId: client.id }}
                    className="font-medium text-primary hover:underline"
                  >
                    {pickText(i18n.language, client.nameAr, client.nameEn)}
                  </Link>
                  <div dir="ltr" className="text-start font-mono text-xs text-muted-foreground">
                    {client.clientId}
                  </div>
                </TableCell>
                <TableCell>{t(`clients.kinds.${client.authKind}` as 'clients.kinds.api_key')}</TableCell>
                <TableCell>{client.allowedSystems.map(systemName).join('، ')}</TableCell>
                <TableCell>{formatDate(client.expiresAt, i18n.language)}</TableCell>
                <TableCell>
                  {client.lastUsedAt ? formatDateTime(client.lastUsedAt, i18n.language) : t('clients.never')}
                </TableCell>
                <TableCell>
                  <StatusChip tone={stateTone[client.state] ?? 'neutral'}>
                    {t(`clients.states.${client.state}` as 'clients.states.active')}
                  </StatusChip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {creating && (
        <ClientDialog
          onClose={() => setCreating(false)}
          onCreated={(created) => {
            setCreating(false);
            setSecret({
              clientId: created.client.clientId,
              secret: created.secret,
              kind: created.client.authKind,
            });
          }}
        />
      )}
      {secret && <SecretDialog {...secret} onClose={() => setSecret(null)} />}
    </div>
  );
}

export function useSystemName(catalogue: CatalogueSystem[] | undefined) {
  const { i18n } = useTranslation();
  return (code: string) => {
    const system = catalogue?.find((s) => s.code === code);
    return system ? pickText(i18n.language, system.nameAr, system.nameEn) : code;
  };
}

/** The client id and secret / key, shown once after creation or rotation. */
export function SecretDialog({
  clientId,
  secret,
  kind,
  onClose,
}: {
  clientId: string;
  secret: string;
  kind: string;
  onClose: () => void;
}) {
  const { t } = useTranslation('integration');
  const { t: tc } = useTranslation();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('clients.secretTitle')}
        description={t('clients.secretOnce')}
        footer={
          <DialogClose asChild>
            <Button>{tc('actions.close')}</Button>
          </DialogClose>
        }
      >
        <div className="flex flex-col gap-3" data-testid="client-secret">
          <FormField label={t('clients.clientId')}>
            <CopyField value={clientId} label={t('clients.clientId')} />
          </FormField>
          <FormField label={kind === 'oauth' ? t('clients.clientSecret') : t('clients.apiKey')}>
            <CopyField
              value={secret}
              label={kind === 'oauth' ? t('clients.clientSecret') : t('clients.apiKey')}
            />
          </FormField>
          <p className="text-sm text-muted-foreground">
            {kind === 'oauth' ? t('clients.howOAuth') : t('clients.howApiKey')}
          </p>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Create or edit a client's settings (systems, record types, acting unit, IP list, rate limit, expiry, scopes). */
export function ClientDialog({
  client,
  onClose,
  onCreated,
}: {
  client?: IntegrationClient;
  onClose: () => void;
  onCreated?: (created: { client: IntegrationClient; secret: string }) => void;
}) {
  const { t, i18n } = useTranslation('integration');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const catalogue = useIntegrationCatalogue().data ?? [];
  const [nameAr, setNameAr] = useState(client?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(client?.nameEn ?? '');
  const [description, setDescription] = useState(client?.description ?? '');
  const [authKind, setAuthKind] = useState(client?.authKind ?? 'api_key');
  const [scopes, setScopes] = useState<string[]>(client?.scopes ?? ['feeds']);
  const [systems, setSystems] = useState<string[]>(client?.allowedSystems ?? []);
  const [types, setTypes] = useState<string[]>(client?.allowedRecordTypes ?? []);
  const [unit, setUnit] = useState<string | undefined>(client?.actingUnitId ?? undefined);
  const [ips, setIps] = useState((client?.ipAllowList ?? []).join('\n'));
  const [rate, setRate] = useState(String(client?.rateLimitPerMinute ?? 120));
  const [expires, setExpires] = useState(client ? client.expiresAt.slice(0, 10) : '');
  const toggle = (list: string[], value: string, on: boolean) =>
    on ? [...list, value] : list.filter((v) => v !== value);
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        nameAr: empty(nameAr),
        nameEn: empty(nameEn),
        description: empty(description),
        authKind,
        actingUnitId: unit ?? null,
        scopes,
        allowedSystems: systems,
        allowedRecordTypes: types.filter((code) =>
          catalogue.some((s) => systems.includes(s.code) && s.recordTypes.some((r) => r.code === code)),
        ),
        ipAllowList: ips
          .split(/\r?\n/)
          .map((v) => v.trim())
          .filter(Boolean),
        rateLimitPerMinute: Number(rate) || null,
        expiresAt: expires ? new Date(`${expires}T23:59:59Z`).toISOString() : null,
        version: client?.version ?? 0,
      };
      return client
        ? {
            client: unwrap(
              await api.PUT('/api/v1/integration/clients/{id}', {
                params: { path: { id: client.id } },
                body,
              }),
            ),
            secret: '',
          }
        : unwrap(await api.POST('/api/v1/integration/clients', { body }));
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: clientKeys.all });
      if (client) {
        onClose();
      } else {
        onCreated?.(saved);
      }
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className="max-w-3xl"
        title={client ? t('clients.edit') : t('clients.create')}
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
              name="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </FormField>
          {!client && (
            <FormField label={t('clients.kind')} hint={t('clients.kindHint')}>
              <Select
                value={authKind}
                onValueChange={setAuthKind}
                aria-label={t('clients.kind')}
                options={[
                  { value: 'api_key', label: t('clients.kinds.api_key') },
                  { value: 'oauth', label: t('clients.kinds.oauth') },
                ]}
              />
            </FormField>
          )}
          <FormField label={t('clients.actingUnit')} hint={t('clients.actingUnitHint')}>
            <UnitSelect value={unit} onChange={setUnit} allowEmpty />
          </FormField>
          <fieldset className="flex flex-col gap-2 sm:col-span-2">
            <legend className="mb-1 text-sm font-medium">{t('clients.scopes')}</legend>
            {['feeds', 'api.read'].map((scope) => (
              <Checkbox
                key={scope}
                label={t(`clients.scopeNames.${scope === 'feeds' ? 'feeds' : 'apiRead'}`)}
                checked={scopes.includes(scope)}
                onChange={(e) => setScopes(toggle(scopes, scope, e.target.checked))}
              />
            ))}
          </fieldset>
          <fieldset className="flex flex-col gap-2 sm:col-span-2">
            <legend className="mb-1 text-sm font-medium">{t('clients.systems')}</legend>
            <p className="text-xs text-muted-foreground">{t('clients.recordTypesHint')}</p>
            <div className="grid gap-3 sm:grid-cols-2">
              {catalogue.map((system) => (
                <div
                  key={system.code}
                  className="rounded-md border p-3"
                  data-testid={`system-${system.code}`}
                >
                  <Checkbox
                    label={pickText(i18n.language, system.nameAr, system.nameEn)}
                    checked={systems.includes(system.code)}
                    onChange={(e) => setSystems(toggle(systems, system.code, e.target.checked))}
                  />
                  {systems.includes(system.code) && system.recordTypes.length > 1 && (
                    <div className="ms-6 mt-2 flex flex-col gap-1">
                      {system.recordTypes.map((type) => (
                        <Checkbox
                          key={type.code}
                          label={pickText(i18n.language, type.labelAr, type.labelEn)}
                          checked={types.includes(type.code)}
                          onChange={(e) => setTypes(toggle(types, type.code, e.target.checked))}
                        />
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </fieldset>
          <FormField label={t('clients.ipAllowList')} hint={t('clients.ipHint')}>
            <Textarea dir="ltr" name="ipAllowList" value={ips} onChange={(e) => setIps(e.target.value)} />
          </FormField>
          <div className="flex flex-col gap-4">
            <FormField label={t('clients.rateLimit')}>
              <Input
                type="number"
                min={1}
                max={10000}
                name="rateLimit"
                value={rate}
                onChange={(e) => setRate(e.target.value)}
              />
            </FormField>
            <FormField label={t('clients.expires')} hint={client ? undefined : t('clients.expiresHint')}>
              <Input
                type="date"
                name="expiresAt"
                value={expires}
                onChange={(e) => setExpires(e.target.value)}
              />
            </FormField>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
