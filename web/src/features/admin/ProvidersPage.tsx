import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDateTime, pickText } from '@/lib/format';
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
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';

type Provider = components['schemas']['ProviderDto'];
type TypeSchema = components['schemas']['ProviderTypeSchema'];
type Field = components['schemas']['ProviderField'];
type Settings = Record<string, string | number | boolean | null>;

const providersKey = ['identity', 'providers', 'admin'] as const;

/**
 * External sign-in providers (OIDC, UAE Pass, SAML 2.0, LDAP / AD), configured at runtime and disabled by default.
 * The form for each type comes from the server's schema, so the server stays the single source of the rules.
 */
export function ProvidersPage() {
  const { t } = useTranslation('admin');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<Provider | 'new' | null>(null);
  const providers = useQuery({
    queryKey: providersKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/admin/providers', { signal })),
  });
  const schema = useQuery({
    queryKey: ['identity', 'providers', 'schema'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/identity/admin/providers/schema', { signal })),
    staleTime: Infinity,
  });
  const toggle = useMutation({
    mutationFn: async (provider: Provider) =>
      unwrap(
        await api.POST(
          provider.enabled
            ? '/api/v1/identity/admin/providers/{id}/disable'
            : '/api/v1/identity/admin/providers/{id}/enable',
          {
            params: { path: { id: provider.id } },
          },
        ),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['identity', 'providers'] }),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('providers.title')}
        description={t('providers.description')}
        actions={
          <Button onClick={() => setEditing('new')} disabled={!schema.data}>
            <Plus aria-hidden />
            {t('providers.create')}
          </Button>
        }
      />
      {providers.isError && <StatusBanner tone="danger" title={errorMessage(tc, providers.error)} />}
      {providers.data?.length === 0 ? (
        <EmptyState title={t('providers.empty')} description={t('providers.emptyHint')} icon={KeyRound} />
      ) : (
        <ul className="grid gap-4 md:grid-cols-2">
          {providers.data?.map((provider) => (
            <li key={provider.id} className="flex flex-col gap-3 rounded-lg border bg-card p-5">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <h2 className="font-semibold">
                    {pickText(i18n.language, provider.displayNameAr, provider.displayNameEn)}
                  </h2>
                  <p className="text-xs text-muted-foreground">
                    {t(`providers.types.${provider.type as 'oidc'}`)} · <span dir="ltr">{provider.code}</span>
                  </p>
                </div>
                <StatusChip tone={provider.enabled ? 'success' : 'neutral'}>
                  {provider.enabled ? t('providers.enabled') : t('providers.disabled')}
                </StatusChip>
              </div>
              {provider.lastTestAt && (
                <p className="text-xs">
                  <StatusChip tone={provider.lastTestOk ? 'success' : 'danger'}>
                    {provider.lastTestOk ? t('providers.testOk') : t('providers.testFailed')}
                  </StatusChip>{' '}
                  {provider.lastTestMessageKey && translateMessageKey(tc, provider.lastTestMessageKey)} ·{' '}
                  {formatDateTime(provider.lastTestAt, i18n.language)}
                </p>
              )}
              <div className="mt-auto flex flex-wrap gap-2">
                <Button variant="outline" size="sm" onClick={() => setEditing(provider)}>
                  {tc('actions.edit')}
                </Button>
                <Button
                  variant={provider.enabled ? 'outline' : 'primary'}
                  size="sm"
                  loading={toggle.isPending && toggle.variables.id === provider.id}
                  onClick={() => toggle.mutate(provider)}
                >
                  {provider.enabled ? t('providers.disable') : t('providers.enable')}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      {editing && schema.data && (
        <ProviderDialog
          provider={editing === 'new' ? undefined : editing}
          schemas={schema.data}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            void queryClient.invalidateQueries({ queryKey: ['identity', 'providers'] });
            setEditing(saved);
          }}
        />
      )}
    </div>
  );
}

function ProviderDialog({
  provider,
  schemas,
  onClose,
  onSaved,
}: {
  provider?: Provider;
  schemas: TypeSchema[];
  onClose: () => void;
  onSaved: (provider: Provider) => void;
}) {
  const { t } = useTranslation('admin');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [type, setType] = useState(provider?.type ?? 'oidc');
  const typeSchema = schemas.find((s) => s.type === type);
  const [code, setCode] = useState(provider?.code ?? '');
  const [nameAr, setNameAr] = useState(provider?.displayNameAr ?? '');
  const [nameEn, setNameEn] = useState(provider?.displayNameEn ?? '');
  const [sort, setSort] = useState(String(provider?.sort ?? 0));
  const [matchField, setMatchField] = useState(
    provider?.matchField ?? typeSchema?.defaultMatchField ?? 'email',
  );
  const [autoLink, setAutoLink] = useState(provider?.autoLink ?? true);
  const [jit, setJit] = useState(provider?.jitCreateUser ?? false);
  const [secret, setSecret] = useState('');
  const [settings, setSettings] = useState<Settings>(
    () => (provider?.settings as Settings | undefined) ?? defaults(typeSchema),
  );
  const [errors, setErrors] = useState<components['schemas']['Problem']['errors']>();
  const [testUser, setTestUser] = useState('');
  const [testResult, setTestResult] = useState<components['schemas']['ProviderTestResultDto']>();

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        code,
        type,
        displayNameAr: nameAr || null,
        displayNameEn: nameEn || null,
        sort: Number(sort) || 0,
        settings: settings as unknown as Record<string, never>,
        secret: secret || null,
        clearSecret: false,
        matchField,
        autoLink,
        jitCreateUser: jit,
        version: provider?.version ?? 0,
      };
      return provider
        ? unwrap(
            await api.PUT('/api/v1/identity/admin/providers/{id}', {
              params: { path: { id: provider.id } },
              body,
            }),
          )
        : unwrap(await api.POST('/api/v1/identity/admin/providers', { body }));
    },
    onSuccess: (saved) => {
      setErrors(undefined);
      setSecret('');
      toast({ title: t('saved') });
      onSaved(saved);
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.problem.errors?.length) setErrors(failure.problem.errors);
      else toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
  });
  const test = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/identity/admin/providers/{id}/test', {
          params: { path: { id: provider?.id ?? '' } },
          body: { testUserName: testUser || null },
        }),
      ),
    onSuccess: setTestResult,
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  const error = (field: string) => {
    const e = errors?.find((x) => x.field === field);
    return e ? translateMessageKey(tc, e.messageKey, e.params ?? undefined) : undefined;
  };
  const origin = window.location.origin;
  const group = (name: string) => typeSchema?.fields.filter((f) => (f.group ?? 'connection') === name) ?? [];

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={provider ? t('providers.edit') : t('providers.create')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={() => save.mutate()}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex max-h-[70dvh] flex-col gap-6 overflow-y-auto pe-1">
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('providers.type')}>
              <Select
                value={type}
                disabled={!!provider}
                onValueChange={(value) => {
                  setType(value);
                  const next = schemas.find((s) => s.type === value);
                  setSettings(defaults(next));
                  setMatchField(next?.defaultMatchField ?? 'email');
                }}
                options={schemas.map((s) => ({
                  value: s.type,
                  label: t(`providers.types.${s.type as 'oidc'}`),
                }))}
              />
            </FormField>
            <FormField
              label={t('providers.code')}
              error={error('code')}
              hint={t('providers.codeHint')}
              required
            >
              <Input
                dir="ltr"
                value={code}
                disabled={!!provider}
                onChange={(e) => setCode(e.target.value.toLowerCase())}
              />
            </FormField>
            <FormField label={t('providers.nameAr')} error={error('displayNameAr')}>
              <Input dir="rtl" lang="ar" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
            </FormField>
            <FormField label={t('providers.nameEn')} error={error('displayNameEn')}>
              <Input dir="ltr" lang="en" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
            </FormField>
            <FormField label={t('providers.sort')}>
              <Input type="number" dir="ltr" value={sort} onChange={(e) => setSort(e.target.value)} />
            </FormField>
            {typeSchema?.hasSecret && (
              <FormField
                label={t(type === 'ldap' ? 'providers.bindPassword' : 'providers.clientSecret')}
                error={error('secret')}
                hint={provider?.hasSecret ? t('providers.secretKept') : undefined}
                required={typeSchema.secretRequired}
              >
                <Input
                  type="password"
                  dir="ltr"
                  autoComplete="new-password"
                  value={secret}
                  onChange={(e) => setSecret(e.target.value)}
                />
              </FormField>
            )}
          </div>

          {(['connection', 'mapping'] as const).map((name) => (
            <fieldset key={name} className="grid gap-4 rounded-md border p-4 sm:grid-cols-2">
              <legend className="px-1 text-sm font-semibold">{t(`providers.groups.${name}`)}</legend>
              {group(name).map((field) => (
                <SettingField
                  key={field.key}
                  field={field}
                  value={settings[field.key]}
                  error={error(`settings.${field.key}`)}
                  onChange={(value) => setSettings({ ...settings, [field.key]: value })}
                />
              ))}
            </fieldset>
          ))}

          <fieldset className="flex flex-col gap-3 rounded-md border p-4">
            <legend className="px-1 text-sm font-semibold">{t('providers.groups.linking')}</legend>
            <FormField label={t('providers.matchField')} className="max-w-xs">
              <Select
                value={matchField}
                onValueChange={setMatchField}
                options={(['email', 'employee_no', 'username'] as const).map((value) => ({
                  value,
                  label: t(`providers.matchFields.${value}`),
                }))}
              />
            </FormField>
            <Checkbox
              label={t('providers.autoLink')}
              description={t('providers.autoLinkHint')}
              checked={autoLink}
              onChange={(e) => setAutoLink(e.target.checked)}
            />
            <Checkbox
              label={t('providers.jit')}
              description={t('providers.jitHint')}
              checked={jit}
              onChange={(e) => setJit(e.target.checked)}
            />
          </fieldset>

          {provider && (provider.callbackPath ?? provider.metadataPath) && (
            <div className="flex flex-col gap-2">
              <p className="text-sm font-semibold">{t('providers.registerAtIdp')}</p>
              {provider.callbackPath && (
                <CopyField value={`${origin}${provider.callbackPath}`} label={t('providers.callbackUrl')} />
              )}
              {provider.metadataPath && (
                <CopyField value={`${origin}${provider.metadataPath}`} label={t('providers.metadataUrl')} />
              )}
            </div>
          )}

          {provider && (
            <div className="flex flex-col gap-3 rounded-md border p-4">
              <p className="text-sm font-semibold">{t('providers.testTitle')}</p>
              {type === 'ldap' && (
                <FormField label={t('providers.testUser')} className="max-w-xs">
                  <Input dir="ltr" value={testUser} onChange={(e) => setTestUser(e.target.value)} />
                </FormField>
              )}
              <Button
                variant="outline"
                className="self-start"
                loading={test.isPending}
                onClick={() => test.mutate()}
              >
                {t('providers.runTest')}
              </Button>
              {testResult && (
                <StatusBanner
                  tone={testResult.ok ? 'success' : 'danger'}
                  title={translateMessageKey(tc, testResult.messageKey)}
                  description={
                    testResult.details && (
                      <dl className="grid grid-cols-[auto_1fr] gap-x-3 text-xs" dir="ltr">
                        {Object.entries(testResult.details).map(([key, value]) => (
                          <div key={key} className="contents">
                            <dt className="font-medium">{key}</dt>
                            <dd className="break-all">{value}</dd>
                          </div>
                        ))}
                      </dl>
                    )
                  }
                />
              )}
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function defaults(schema: TypeSchema | undefined): Settings {
  const settings: Settings = {};
  for (const field of schema?.fields ?? []) {
    if (field.default !== undefined && field.default !== null)
      settings[field.key] = field.default as string | number | boolean;
  }
  return settings;
}

function SettingField({
  field,
  value,
  error,
  onChange,
}: {
  field: Field;
  value: Settings[string] | undefined;
  error?: string | undefined;
  onChange: (value: Settings[string]) => void;
}) {
  const { t } = useTranslation('admin');
  const label = t(`providers.fields.${field.key}`, { defaultValue: field.key });
  if (field.kind === 'boolean') {
    return (
      <Checkbox
        className="sm:col-span-2"
        label={label}
        checked={value === true}
        onChange={(e) => onChange(e.target.checked)}
      />
    );
  }

  if (field.kind === 'select') {
    return (
      <FormField label={label} error={error} required={field.required}>
        <Select
          value={typeof value === 'string' ? value : undefined}
          onValueChange={onChange}
          options={(field.options ?? []).map((o) => ({
            value: o,
            label: t(`providers.options.${o}`, { defaultValue: o }),
          }))}
        />
      </FormField>
    );
  }

  if (field.kind === 'multiline') {
    return (
      <FormField label={label} error={error} required={field.required} className="sm:col-span-2">
        <Textarea
          dir="ltr"
          className="font-mono text-xs"
          value={typeof value === 'string' ? value : ''}
          onChange={(e) => onChange(e.target.value || null)}
        />
      </FormField>
    );
  }

  return (
    <FormField label={label} error={error} required={field.required}>
      <Input
        dir="ltr"
        type={field.kind === 'number' ? 'number' : field.kind === 'url' ? 'url' : 'text'}
        value={value === null || value === undefined ? '' : String(value)}
        onChange={(e) =>
          onChange(
            field.kind === 'number'
              ? e.target.value === ''
                ? null
                : Number(e.target.value)
              : e.target.value || null,
          )
        }
      />
    </FormField>
  );
}
