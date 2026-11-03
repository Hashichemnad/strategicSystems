import { Bot, CircleCheck, CircleX, Pencil, Plus, PlugZap } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDateTime, formatNumber } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import {
  useAiProviders,
  useAiSettings,
  useAiUsage,
  useAiUsageSummary,
  useFetchAiModels,
  useSaveAiProvider,
  useSaveAiSettings,
  useTestAiProvider,
  type AiProvider,
  type AiSettings,
  type AiSettingsDocument,
} from './api';

const DEFAULT = '__default';
type FieldErrors = components['schemas']['Problem']['errors'];

/** Settings → AI (docs/design/ai.md §2–3, §8). Every change is saved through the API, which audits it. */
export function AiSettingsPage() {
  const { t } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const settings = useAiSettings();
  const providers = useAiProviders();
  const save = useSaveAiSettings();
  if (settings.isError) return <StatusBanner tone="danger" title={errorMessage(tc, settings.error)} />;
  if (!settings.data || !providers.data) return <Skeleton className="h-96" />;
  const document = settings.data;
  const update = (change: (current: AiSettings) => AiSettings) =>
    save.mutate(
      { settings: change(document.settings), version: document.version },
      {
        onSuccess: () => toast({ title: t('settings.saved') }),
        onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
      },
    );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <div className="rounded-lg border bg-card p-5">
        <Switch
          checked={document.settings.enabled}
          disabled={save.isPending}
          onCheckedChange={(enabled) => update((s) => ({ ...s, enabled }))}
          label={t('settings.master')}
          description={t('settings.masterHint')}
        />
      </div>
      <Tabs defaultValue="providers">
        <TabsList>
          <TabsTrigger value="providers">{t('settings.tabs.providers')}</TabsTrigger>
          <TabsTrigger value="features">{t('settings.tabs.features')}</TabsTrigger>
          <TabsTrigger value="data">{t('settings.tabs.data')}</TabsTrigger>
          <TabsTrigger value="usage">{t('settings.tabs.usage')}</TabsTrigger>
        </TabsList>
        <TabsContent value="providers">
          <ProvidersTab
            document={document}
            providers={providers.data}
            onDefault={(id) => update((s) => ({ ...s, defaultProviderId: id }))}
          />
        </TabsContent>
        <TabsContent value="features">
          <FeaturesTab document={document} providers={providers.data} update={update} />
        </TabsContent>
        <TabsContent value="data">
          <DataTab document={document} update={update} saving={save.isPending} />
        </TabsContent>
        <TabsContent value="usage">
          <UsageTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function ProvidersTab({
  document,
  providers,
  onDefault,
}: {
  document: AiSettingsDocument;
  providers: AiProvider[];
  onDefault: (id: string | null) => void;
}) {
  const { t } = useTranslation('ai');
  const { t: tc, i18n } = useTranslation();
  const toast = useToast();
  const test = useTestAiProvider();
  const [editing, setEditing] = useState<AiProvider | 'new' | null>(null);
  const [results, setResults] = useState<Record<string, components['schemas']['AiProviderTestDto']>>({});
  const typeLabel = (type: string) => (t as unknown as (k: string) => string)(`settings.types.${type}`);
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-4">
        <FormField label={t('settings.providers.default')} className="min-w-64">
          <Select
            value={document.settings.defaultProviderId ?? DEFAULT}
            onValueChange={(value) => onDefault(value === DEFAULT ? null : value)}
            options={[
              { value: DEFAULT, label: t('settings.providers.none') },
              ...providers.map((p) => ({ value: p.id, label: p.name })),
            ]}
          />
        </FormField>
        <Button className="ms-auto" onClick={() => setEditing('new')}>
          <Plus aria-hidden />
          {t('settings.providers.add')}
        </Button>
      </div>
      {providers.length === 0 ? (
        <EmptyState
          icon={Bot}
          title={t('settings.providers.empty')}
          description={t('settings.providers.emptyHint')}
        />
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {providers.map((provider) => {
            const result = results[provider.id];
            return (
              <div
                key={provider.id}
                className="flex flex-col gap-3 rounded-lg border bg-card p-4"
                data-testid="ai-provider-card"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="font-semibold">{provider.name}</h3>
                  <StatusChip tone={provider.active ? 'success' : 'neutral'}>
                    {provider.active ? t('settings.providers.active') : t('settings.providers.inactive')}
                  </StatusChip>
                  {document.settings.defaultProviderId === provider.id && (
                    <StatusChip tone="info">{t('settings.providers.default')}</StatusChip>
                  )}
                </div>
                <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
                  <dt className="text-muted-foreground">{t('settings.fields.type')}</dt>
                  <dd>{typeLabel(provider.type)}</dd>
                  <dt className="text-muted-foreground">{t('settings.fields.defaultModel')}</dt>
                  <dd dir="ltr" className="text-start">
                    {provider.defaultModel ?? '—'}
                  </dd>
                  {provider.endpoint && (
                    <>
                      <dt className="text-muted-foreground">{t('settings.fields.endpoint')}</dt>
                      <dd dir="ltr" className="truncate text-start">
                        {provider.endpoint}
                      </dd>
                    </>
                  )}
                  <dt className="text-muted-foreground">{t('settings.fields.apiKey')}</dt>
                  <dd>
                    {provider.hasApiKey ? t('settings.providers.keyStored') : t('settings.providers.noKey')}
                  </dd>
                </dl>
                {result && (
                  <StatusBanner
                    tone={result.success ? 'success' : 'danger'}
                    title={
                      result.success
                        ? t('settings.providers.testOk', {
                            ms: formatNumber(result.latencyMs, i18n.language),
                            model: result.model ?? '—',
                          })
                        : t('settings.providers.testFailed', {
                            reason: translateMessageKey(tc, result.messageKey ?? 'ai.errors.providerError'),
                          })
                    }
                  />
                )}
                <div className="flex flex-wrap gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    loading={test.isPending && test.variables.id === provider.id}
                    onClick={() =>
                      test.mutate(
                        { id: provider.id },
                        {
                          onSuccess: (outcome) =>
                            setResults((current) => ({ ...current, [provider.id]: outcome })),
                          onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                        },
                      )
                    }
                  >
                    {result?.success ? (
                      <CircleCheck aria-hidden />
                    ) : result ? (
                      <CircleX aria-hidden />
                    ) : (
                      <PlugZap aria-hidden />
                    )}
                    {t('settings.providers.test')}
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setEditing(provider)}>
                    <Pencil aria-hidden />
                    {t('settings.providers.edit')}
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      )}
      <ProviderDialog
        document={document}
        provider={editing === 'new' ? null : editing}
        open={editing !== null}
        onOpenChange={(open) => !open && setEditing(null)}
      />
    </div>
  );
}

interface ProviderDraft {
  name: string;
  type: string;
  endpoint: string;
  apiKey: string;
  clearApiKey: boolean;
  defaultModel: string;
  models: string;
  temperature: string;
  maxOutputTokens: string;
  timeoutSeconds: string;
  active: boolean;
}

function ProviderDialog({
  document,
  provider,
  open,
  onOpenChange,
}: {
  document: AiSettingsDocument;
  provider: AiProvider | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { t } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSaveAiProvider();
  const fetchModels = useFetchAiModels();
  const [errors, setErrors] = useState<FieldErrors>();
  const [draft, setDraft] = useState<ProviderDraft>(empty(document));
  useEffect(() => {
    setErrors(undefined);
    setDraft(
      provider
        ? {
            name: provider.name,
            type: provider.type,
            endpoint: provider.endpoint ?? '',
            apiKey: '',
            clearApiKey: false,
            defaultModel: provider.defaultModel ?? '',
            models: provider.models.join('\n'),
            temperature: provider.temperature === null ? '' : String(provider.temperature),
            maxOutputTokens: String(provider.maxOutputTokens),
            timeoutSeconds: String(provider.timeoutSeconds),
            active: provider.active,
          }
        : empty(document),
    );
  }, [provider, open, document]);
  const type = document.providerTypes.find((p) => p.type === draft.type);
  const error = (field: string) => {
    const found = errors?.find((e) => e.field === field);
    return found ? translateMessageKey(tc, found.messageKey, found.params ?? undefined) : undefined;
  };
  const typeLabel = (value: string) => (t as unknown as (k: string) => string)(`settings.types.${value}`);
  const submit = () =>
    save.mutate(
      {
        id: provider?.id ?? null,
        body: {
          name: draft.name,
          type: draft.type,
          endpoint: draft.endpoint || null,
          apiKey: draft.apiKey || null,
          clearApiKey: draft.clearApiKey,
          defaultModel: draft.defaultModel || null,
          models: draft.models
            .split('\n')
            .map((m) => m.trim())
            .filter(Boolean),
          temperature: draft.temperature === '' ? null : Number(draft.temperature),
          maxOutputTokens: Number(draft.maxOutputTokens),
          timeoutSeconds: Number(draft.timeoutSeconds),
          active: draft.active,
          version: provider?.version ?? 0,
        },
      },
      {
        onSuccess: () => {
          toast({ title: t('settings.providers.saved') });
          onOpenChange(false);
        },
        onError: (failure) => {
          if (failure instanceof ApiError && failure.problem.errors?.length)
            setErrors(failure.problem.errors);
          else toast({ tone: 'error', title: errorMessage(tc, failure) });
        },
      },
    );
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={provider ? t('settings.providers.editTitle') : t('settings.providers.createTitle')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={submit}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('settings.fields.name')} error={error('name')}>
            <Input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
          </FormField>
          <FormField label={t('settings.fields.type')} error={error('type')}>
            <Select
              value={draft.type}
              onValueChange={(value) => setDraft({ ...draft, type: value })}
              options={document.providerTypes.map((p) => ({ value: p.type, label: typeLabel(p.type) }))}
            />
          </FormField>
          <FormField
            label={t('settings.fields.endpoint')}
            error={error('endpoint')}
            hint={t('settings.fields.endpointHint')}
            required={type?.requiresEndpoint ?? false}
            className="sm:col-span-2"
          >
            <Input
              dir="ltr"
              value={draft.endpoint}
              onChange={(e) => setDraft({ ...draft, endpoint: e.target.value })}
            />
          </FormField>
          <FormField
            label={t('settings.fields.apiKey')}
            error={error('apiKey')}
            hint={provider?.hasApiKey ? t('settings.providers.keyKept') : undefined}
            className="sm:col-span-2"
          >
            <Input
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={draft.apiKey}
              onChange={(e) => setDraft({ ...draft, apiKey: e.target.value })}
            />
          </FormField>
          {provider?.hasApiKey && (
            <Switch
              className="sm:col-span-2"
              checked={draft.clearApiKey}
              onCheckedChange={(clearApiKey) => setDraft({ ...draft, clearApiKey })}
              label={t('settings.providers.clearKey')}
            />
          )}
          <FormField label={t('settings.fields.defaultModel')} error={error('defaultModel')}>
            <Input
              dir="ltr"
              value={draft.defaultModel}
              onChange={(e) => setDraft({ ...draft, defaultModel: e.target.value })}
            />
          </FormField>
          <FormField label={t('settings.fields.models')} hint={t('settings.fields.modelsHint')}>
            <Textarea
              dir="ltr"
              rows={3}
              value={draft.models}
              onChange={(e) => setDraft({ ...draft, models: e.target.value })}
            />
          </FormField>
          {provider && (
            <Button
              variant="outline"
              size="sm"
              className="self-start sm:col-span-2"
              loading={fetchModels.isPending}
              onClick={() =>
                fetchModels.mutate(
                  { id: provider.id, apiKey: draft.apiKey || null },
                  {
                    onSuccess: (models) => {
                      setDraft({
                        ...draft,
                        models: models.join('\n'),
                        defaultModel: draft.defaultModel || (models[0] ?? ''),
                      });
                      toast({ title: t('settings.providers.modelsFetched', { count: models.length }) });
                    },
                    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                  },
                )
              }
            >
              {t('settings.providers.fetchModels')}
            </Button>
          )}
          <FormField label={t('settings.fields.temperature')} error={error('temperature')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={draft.temperature}
              onChange={(e) => setDraft({ ...draft, temperature: e.target.value })}
            />
          </FormField>
          <FormField label={t('settings.fields.maxOutputTokens')} error={error('maxOutputTokens')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={draft.maxOutputTokens}
              onChange={(e) => setDraft({ ...draft, maxOutputTokens: e.target.value })}
            />
          </FormField>
          <FormField label={t('settings.fields.timeoutSeconds')} error={error('timeoutSeconds')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={draft.timeoutSeconds}
              onChange={(e) => setDraft({ ...draft, timeoutSeconds: e.target.value })}
            />
          </FormField>
          <Switch
            checked={draft.active}
            onCheckedChange={(active) => setDraft({ ...draft, active })}
            label={t('settings.fields.active')}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function empty(document: AiSettingsDocument): ProviderDraft {
  return {
    name: '',
    type: document.providerTypes[0]?.type ?? 'anthropic',
    endpoint: '',
    apiKey: '',
    clearApiKey: false,
    defaultModel: '',
    models: '',
    temperature: '',
    maxOutputTokens: '2000',
    timeoutSeconds: '60',
    active: true,
  };
}

function FeaturesTab({
  document,
  providers,
  update,
}: {
  document: AiSettingsDocument;
  providers: AiProvider[];
  update: (change: (current: AiSettings) => AiSettings) => void;
}) {
  const { t, i18n } = useTranslation('ai');
  const featureSettings = (key: string) =>
    document.settings.features?.[key] ?? { enabled: true, providerId: null, model: null };
  const change = (key: string, value: Partial<ReturnType<typeof featureSettings>>) =>
    update((s) => ({
      ...s,
      features: { ...(s.features ?? {}), [key]: { ...featureSettings(key), ...value } },
    }));
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('settings.features.feature')}</TableHeaderCell>
          <TableHeaderCell>{t('settings.features.enabled')}</TableHeaderCell>
          <TableHeaderCell>{t('settings.features.provider')}</TableHeaderCell>
          <TableHeaderCell>{t('settings.features.model')}</TableHeaderCell>
          <TableHeaderCell>{t('settings.features.access')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {document.features.map((feature) => {
          const current = featureSettings(feature.key);
          return (
            <TableRow key={feature.key}>
              <TableCell className="font-medium">
                {i18n.language === 'ar' ? feature.nameAr : feature.nameEn}
              </TableCell>
              <TableCell>
                <Switch
                  hideLabel
                  label={t('settings.features.enabled')}
                  checked={current.enabled}
                  onCheckedChange={(enabled) => change(feature.key, { enabled })}
                />
              </TableCell>
              <TableCell>
                {feature.needsProvider ? (
                  <Select
                    aria-label={t('settings.features.provider')}
                    value={current.providerId ?? DEFAULT}
                    onValueChange={(value) =>
                      change(feature.key, { providerId: value === DEFAULT ? null : value })
                    }
                    options={[
                      { value: DEFAULT, label: t('settings.features.useDefault') },
                      ...providers.map((p) => ({ value: p.id, label: p.name })),
                    ]}
                  />
                ) : (
                  <span className="text-sm text-muted-foreground">{t('settings.features.noProvider')}</span>
                )}
              </TableCell>
              <TableCell>
                {feature.needsProvider && (
                  <Input
                    dir="ltr"
                    aria-label={t('settings.features.model')}
                    placeholder={t('settings.features.useDefault')}
                    defaultValue={current.model ?? ''}
                    onBlur={(e) =>
                      e.target.value !== (current.model ?? '') &&
                      change(feature.key, { model: e.target.value || null })
                    }
                  />
                )}
              </TableCell>
              <TableCell className="text-xs text-muted-foreground">
                {t('settings.features.accessHint', { permission: feature.permission })}
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function DataTab({
  document,
  update,
  saving,
}: {
  document: AiSettingsDocument;
  update: (change: (current: AiSettings) => AiSettings) => void;
  saving: boolean;
}) {
  const { t, i18n } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const sharing = document.settings.dataSharing ?? {
    maskPersonalData: true,
    allowDocuments: true,
    systems: null,
  };
  const limits = document.settings.limits ?? { monthlyTokens: null, monthlyRequestsPerUser: null };
  const [tokens, setTokens] = useState(limits.monthlyTokens?.toString() ?? '');
  const [requests, setRequests] = useState(limits.monthlyRequestsPerUser?.toString() ?? '');
  const system = (code: string) => sharing.systems?.[code] ?? { allowed: true, excludedFields: null };
  const setSharing = (value: Partial<typeof sharing>) =>
    update((s) => ({ ...s, dataSharing: { ...sharing, ...value } }));
  const setSystem = (code: string, value: Partial<ReturnType<typeof system>>) =>
    setSharing({ systems: { ...(sharing.systems ?? {}), [code]: { ...system(code), ...value } } });
  return (
    <div className="flex flex-col gap-6">
      <div className="grid gap-4 rounded-lg border bg-card p-5">
        <Switch
          checked={sharing.maskPersonalData}
          onCheckedChange={(maskPersonalData) => setSharing({ maskPersonalData })}
          label={t('settings.data.mask')}
          description={t('settings.data.maskHint')}
        />
        <Switch
          checked={sharing.allowDocuments}
          onCheckedChange={(allowDocuments) => setSharing({ allowDocuments })}
          label={t('settings.data.documents')}
          description={t('settings.data.documentsHint')}
        />
      </div>
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('settings.data.system')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.data.allowed')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.data.excluded')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {document.systems.map((s) => (
            <TableRow key={s.code}>
              <TableCell className="font-medium">{i18n.language === 'ar' ? s.nameAr : s.nameEn}</TableCell>
              <TableCell>
                <Switch
                  hideLabel
                  label={t('settings.data.allowed')}
                  checked={system(s.code).allowed}
                  onCheckedChange={(allowed) => setSystem(s.code, { allowed })}
                />
              </TableCell>
              <TableCell>
                <Input
                  dir="ltr"
                  aria-label={t('settings.data.excluded')}
                  placeholder={t('settings.data.excludedHint')}
                  defaultValue={(system(s.code).excludedFields ?? []).join(', ')}
                  onBlur={(e) => {
                    const next = e.target.value
                      .split(',')
                      .map((f) => f.trim())
                      .filter(Boolean);
                    if (next.join(',') !== (system(s.code).excludedFields ?? []).join(','))
                      setSystem(s.code, { excludedFields: next.length ? next : null });
                  }}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <form
        className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          update((s) => ({
            ...s,
            limits: {
              monthlyTokens: tokens === '' ? null : Number(tokens),
              monthlyRequestsPerUser: requests === '' ? null : Number(requests),
            },
          }));
        }}
      >
        <h3 className="font-semibold sm:col-span-2">{t('settings.data.limits')}</h3>
        <FormField label={t('settings.data.monthlyTokens')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            placeholder={t('settings.data.noLimit')}
            value={tokens}
            onChange={(e) => setTokens(e.target.value)}
          />
        </FormField>
        <FormField label={t('settings.data.monthlyRequests')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            placeholder={t('settings.data.noLimit')}
            value={requests}
            onChange={(e) => setRequests(e.target.value)}
          />
        </FormField>
        <Button type="submit" className="self-start" loading={saving}>
          {tc('actions.save')}
        </Button>
      </form>
    </div>
  );
}

function UsageTab() {
  const { t, i18n } = useTranslation('ai');
  const [page, setPage] = useState(1);
  const summary = useAiUsageSummary();
  const usage = useAiUsage({ page });
  const featureName = (key: string) => (t as unknown as (k: string) => string)(`features.${key}`);
  return (
    <div className="flex flex-col gap-4">
      {summary.data && (
        <div className="flex flex-col gap-3 rounded-lg border bg-card p-5">
          <h3 className="font-semibold">
            {t('settings.usage.thisMonth', { date: formatDateTime(summary.data.monthStart, i18n.language) })}
          </h3>
          <div className="flex flex-wrap gap-6 text-sm">
            <span>
              {t('settings.usage.requests')}:{' '}
              <strong>{formatNumber(summary.data.requests, i18n.language)}</strong>
            </span>
            <span>
              {t('settings.usage.tokens')}:{' '}
              <strong>{formatNumber(summary.data.tokens, i18n.language)}</strong>
              {summary.data.monthlyTokenLimit
                ? ` · ${t('settings.usage.limit', { value: formatNumber(summary.data.monthlyTokenLimit, i18n.language) })}`
                : ''}
            </span>
          </div>
          <div className="flex flex-wrap gap-2">
            {summary.data.byFeature.map((f) => (
              <StatusChip key={f.feature} tone={f.failures > 0 ? 'warning' : 'neutral'}>
                {featureName(f.feature)}: {formatNumber(f.requests, i18n.language)}
              </StatusChip>
            ))}
          </div>
        </div>
      )}
      <h3 className="font-semibold">{t('settings.usage.log')}</h3>
      {usage.data && usage.data.items.length === 0 ? (
        <EmptyState icon={Bot} title={t('settings.usage.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('settings.usage.when')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.usage.user')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.usage.feature')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.usage.model')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.usage.tokensCol')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.usage.outcome')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(usage.data?.items ?? []).map((row) => (
              <TableRow key={row.id}>
                <TableCell className="whitespace-nowrap">
                  {formatDateTime(row.occurredAt, i18n.language)}
                </TableCell>
                <TableCell>{row.userName ?? '—'}</TableCell>
                <TableCell>{featureName(row.feature)}</TableCell>
                <TableCell dir="ltr" className="text-start">
                  {row.model ?? '—'}
                </TableCell>
                <TableCell dir="ltr" className="text-start">
                  {formatNumber(row.inputTokens, i18n.language)} /{' '}
                  {formatNumber(row.outputTokens, i18n.language)}
                </TableCell>
                <TableCell>
                  <StatusChip tone={row.success ? 'success' : 'danger'}>
                    {row.success ? t('settings.usage.ok') : t('settings.usage.failed')}
                  </StatusChip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {usage.data && (
        <Pagination
          page={page}
          totalPages={Math.max(1, Math.ceil(usage.data.totalCount / usage.data.pageSize))}
          totalCount={usage.data.totalCount}
          onPageChange={setPage}
        />
      )}
    </div>
  );
}
