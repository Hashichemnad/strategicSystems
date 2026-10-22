import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { profileRoute } from '@/app/router';
import { useTheme, type ThemePreference } from '@/app/theme';
import { MfaSetup, RecoveryCodes } from '@/features/auth/MfaSetup';
import { PasswordPolicyHint } from '@/features/auth/PasswordPolicyHint';
import { meKey, useMe } from '@/features/auth/session';
import { isLanguage } from '@/i18n';
import { formatDateTime, pickText } from '@/lib/format';
import { writePreference } from '@/lib/preferences';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  CopyField,
  Dialog,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  StatusBanner,
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
  useToast,
} from '@/ui';
import { DelegationsPanel } from './DelegationsPanel';

type Tab = 'preferences' | 'security' | 'sessions' | 'tokens' | 'delegations';

export function ProfilePage() {
  const { t } = useTranslation('profile');
  const { tab } = profileRoute.useSearch();
  const navigate = useNavigate();
  const me = useMe();
  const { i18n } = useTranslation();
  const name = me?.employee ? pickText(i18n.language, me.employee.nameAr, me.employee.nameEn) : me?.userName;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={name}
        meta={me?.employee && pickText(i18n.language, me.employee.unitNameAr, me.employee.unitNameEn)}
      />
      <Tabs
        value={tab}
        onValueChange={(value) => void navigate({ to: '/profile', search: { tab: value as Tab } })}
      >
        <TabsList className="flex-wrap">
          <TabsTrigger value="preferences">{t('tabs.preferences')}</TabsTrigger>
          <TabsTrigger value="security">{t('tabs.security')}</TabsTrigger>
          <TabsTrigger value="sessions">{t('tabs.sessions')}</TabsTrigger>
          <TabsTrigger value="tokens">{t('tabs.tokens')}</TabsTrigger>
          <TabsTrigger value="delegations">{t('tabs.delegations')}</TabsTrigger>
        </TabsList>
        <TabsContent value="preferences" className="pt-6">
          <PreferencesPanel />
        </TabsContent>
        <TabsContent value="security" className="flex flex-col gap-8 pt-6">
          {me?.hasPassword && <PasswordPanel />}
          <MfaPanel />
        </TabsContent>
        <TabsContent value="sessions" className="pt-6">
          <SessionsPanel />
        </TabsContent>
        <TabsContent value="tokens" className="pt-6">
          <TokensPanel />
        </TabsContent>
        <TabsContent value="delegations" className="pt-6">
          <DelegationsPanel />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Section({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-5">
      <div>
        <h2 className="text-lg font-semibold">{title}</h2>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}

function PreferencesPanel() {
  const { t } = useTranslation('profile');
  const { t: ts } = useTranslation('shell');
  const { i18n } = useTranslation();
  const { theme, setTheme } = useTheme();
  const queryClient = useQueryClient();
  const toast = useToast();
  const save = async (language: string | null, nextTheme: string | null) => {
    await api.PUT('/api/v1/identity/me/preferences', { body: { language, theme: nextTheme } });
    await queryClient.invalidateQueries({ queryKey: meKey });
    toast({ title: t('saved') });
  };
  return (
    <Section title={t('preferences.title')} description={t('preferences.description')}>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('preferences.language')}>
          <Select
            value={i18n.language}
            options={[
              { value: 'ar', label: 'العربية' },
              { value: 'en', label: 'English' },
            ]}
            onValueChange={(value) => {
              if (!isLanguage(value)) return;
              writePreference('language', value);
              void i18n.changeLanguage(value);
              void save(value, null);
            }}
          />
        </FormField>
        <FormField label={t('preferences.theme')}>
          <Select
            value={theme}
            options={(['light', 'dark', 'system'] as const).map((value) => ({
              value,
              label: ts(`theme.${value}`),
            }))}
            onValueChange={(value) => {
              setTheme(value as ThemePreference);
              void save(null, value);
            }}
          />
        </FormField>
      </div>
    </Section>
  );
}

function PasswordPanel() {
  const { t } = useTranslation('profile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [errors, setErrors] = useState<components['schemas']['Problem']['errors']>();
  const policy = useQuery({
    queryKey: ['identity', 'password-policy'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/auth/password-policy', { signal })),
  });
  const change = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/identity/me/password', {
          body: { currentPassword: current, newPassword: next },
        }),
      ),
    onSuccess: () => {
      setCurrent('');
      setNext('');
      setErrors(undefined);
      toast({ title: t('password.changed') });
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.problem.errors?.length) setErrors(failure.problem.errors);
      else toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
  });
  const fieldError = (field: string) =>
    errors
      ?.filter((e) => e.field === field)
      .map((e) => translateMessageKey(tc, e.messageKey, e.params ?? undefined))
      .join(' ');

  return (
    <Section title={t('password.title')} description={t('password.description')}>
      {policy.data && <PasswordPolicyHint policy={policy.data} />}
      <form
        className="grid gap-4 sm:max-w-md"
        onSubmit={(event) => {
          event.preventDefault();
          change.mutate();
        }}
      >
        <FormField label={t('password.current')} error={fieldError('currentPassword') || undefined}>
          <Input
            type="password"
            dir="ltr"
            autoComplete="current-password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
        </FormField>
        <FormField label={t('password.new')} error={fieldError('newPassword') || undefined}>
          <Input
            type="password"
            dir="ltr"
            autoComplete="new-password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
          />
        </FormField>
        <Button
          type="submit"
          loading={change.isPending}
          disabled={!current || !next}
          className="justify-self-start"
        >
          {t('password.submit')}
        </Button>
      </form>
    </Section>
  );
}

function MfaPanel() {
  const { t } = useTranslation('profile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [setupOpen, setSetupOpen] = useState(false);
  const [codes, setCodes] = useState<string[]>();
  const mfa = useQuery({
    queryKey: ['identity', 'me', 'mfa'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/me/mfa', { signal })),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['identity', 'me'] });
  const disable = useMutation({
    mutationFn: async (method: string) =>
      unwrap(await api.POST('/api/v1/identity/me/mfa/disable', { body: { method } })),
    onSuccess: () => void refresh(),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const regenerate = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/v1/identity/me/mfa/recovery-codes')),
    onSuccess: (result) => {
      setCodes(result.codes);
      void refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  const data = mfa.data;
  const missing = data
    ? [data.totpAllowed && !data.totp && 'totp', data.emailAllowed && !data.email && 'email'].filter(
        (m): m is string => !!m,
      )
    : [];
  return (
    <Section title={t('mfa.title')} description={t('mfa.description')}>
      {data?.requiredByPolicy && !data.enabled && (
        <StatusBanner tone="warning" title={t('mfa.requiredByPolicy')} />
      )}
      {data && (
        <ul className="flex flex-col divide-y rounded-md border">
          {(['totp', 'email'] as const).map((method) => {
            const on = method === 'totp' ? data.totp : data.email;
            return (
              <li key={method} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span className="flex items-center gap-2">
                  <span className="font-medium">{t(`mfa.methods.${method}`)}</span>
                  <StatusChip tone={on ? 'success' : 'neutral'}>{on ? t('mfa.on') : t('mfa.off')}</StatusChip>
                </span>
                {on && (
                  <Button
                    variant="outline"
                    size="sm"
                    loading={disable.isPending}
                    onClick={() => disable.mutate(method)}
                  >
                    {t('mfa.turnOff')}
                  </Button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      <div className="flex flex-wrap gap-2">
        {missing.length > 0 && <Button onClick={() => setSetupOpen(true)}>{t('mfa.add')}</Button>}
        {data?.enabled && (
          <Button variant="outline" loading={regenerate.isPending} onClick={() => regenerate.mutate()}>
            {t('mfa.regenerate', { count: data.recoveryCodesLeft })}
          </Button>
        )}
      </div>
      {codes && <RecoveryCodes codes={codes} />}
      <Dialog open={setupOpen} onOpenChange={setSetupOpen}>
        <DialogContent title={t('mfa.add')}>
          <MfaSetup
            api={{
              methods: missing,
              startTotp: async () => {
                try {
                  return unwrap(await api.POST('/api/v1/identity/me/mfa/totp'));
                } catch (failure) {
                  toast({ tone: 'error', title: errorMessage(tc, failure) });
                  return undefined;
                }
              },
              startEmail: async () => unwrap(await api.POST('/api/v1/identity/me/mfa/email')),
              confirm: async (method, code) => {
                const result = unwrap(
                  await api.POST('/api/v1/identity/me/mfa/confirm', { body: { method, code } }),
                );
                setSetupOpen(false);
                setCodes(result.codes);
                await refresh();
              },
            }}
          />
        </DialogContent>
      </Dialog>
    </Section>
  );
}

function SessionsPanel() {
  const { t } = useTranslation('profile');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const sessions = useQuery({
    queryKey: ['identity', 'me', 'sessions'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/me/sessions', { signal })),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['identity', 'me', 'sessions'] });
  const revoke = useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/v1/identity/me/sessions/{id}/revoke', { params: { path: { id } } })),
    onSuccess: () => void refresh(),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const revokeOthers = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/v1/identity/me/sessions/revoke-others')),
    onSuccess: () => {
      toast({ title: t('sessions.othersEnded') });
      void refresh();
    },
  });

  return (
    <Section title={t('sessions.title')} description={t('sessions.description')}>
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('sessions.device')}</TableHeaderCell>
            <TableHeaderCell>{t('sessions.method')}</TableHeaderCell>
            <TableHeaderCell>{t('sessions.started')}</TableHeaderCell>
            <TableHeaderCell>{t('sessions.lastSeen')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{tc('actions.more')}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {sessions.data?.map((session) => (
            <TableRow key={session.id}>
              <TableCell className="max-w-64">
                <span className="block truncate text-xs" dir="ltr" title={session.userAgent ?? undefined}>
                  {session.userAgent ?? '—'}
                </span>
                <span className="text-xs text-muted-foreground" dir="ltr">
                  {session.ipAddress}
                </span>
              </TableCell>
              <TableCell>
                {t(`sessions.methods.${session.method}`, { defaultValue: session.method })}
              </TableCell>
              <TableCell>{formatDateTime(session.createdAt, i18n.language)}</TableCell>
              <TableCell>{formatDateTime(session.lastSeenAt, i18n.language)}</TableCell>
              <TableCell className="text-end">
                {session.current ? (
                  <StatusChip tone="success">{t('sessions.current')}</StatusChip>
                ) : (
                  <Button variant="ghost" size="sm" onClick={() => revoke.mutate(session.id)}>
                    {t('sessions.end')}
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <Button
        variant="outline"
        className="self-start"
        loading={revokeOthers.isPending}
        onClick={() => revokeOthers.mutate()}
      >
        {t('sessions.endOthers')}
      </Button>
    </Section>
  );
}

const tokenScopes = ['api.read', 'api.write', 'mcp'] as const;

function TokensPanel() {
  const { t } = useTranslation('profile');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [days, setDays] = useState('90');
  const [scopes, setScopes] = useState<string[]>(['api.read']);
  const [created, setCreated] = useState<string>();
  const [revokeId, setRevokeId] = useState<string>();
  const tokens = useQuery({
    queryKey: ['identity', 'me', 'tokens'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/me/tokens', { signal })),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['identity', 'me', 'tokens'] });
  const create = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/v1/identity/me/tokens', { body: { name, scopes, days: Number(days) } })),
    onSuccess: (result) => {
      setCreated(result.token);
      setName('');
      void refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const revoke = useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/v1/identity/me/tokens/{id}/revoke', { params: { path: { id } } })),
    onSuccess: () => {
      setRevokeId(undefined);
      void refresh();
    },
  });

  return (
    <Section title={t('tokens.title')} description={t('tokens.description')}>
      {tokens.data?.length === 0 ? (
        <EmptyState title={t('tokens.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('tokens.name')}</TableHeaderCell>
              <TableHeaderCell>{t('tokens.scopes')}</TableHeaderCell>
              <TableHeaderCell>{t('tokens.expires')}</TableHeaderCell>
              <TableHeaderCell>{t('tokens.lastUsed')}</TableHeaderCell>
              <TableHeaderCell>{t('tokens.state')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {tokens.data?.map((token) => (
              <TableRow key={token.id}>
                <TableCell>
                  <span className="font-medium">{token.name}</span>
                  <span className="block font-mono text-xs text-muted-foreground" dir="ltr">
                    sst_{token.prefix}_…
                  </span>
                </TableCell>
                <TableCell dir="ltr" className="text-xs">
                  {token.scopes.join(', ')}
                </TableCell>
                <TableCell>{formatDateTime(token.expiresAt, i18n.language)}</TableCell>
                <TableCell>
                  {token.lastUsedAt ? formatDateTime(token.lastUsedAt, i18n.language) : '—'}
                </TableCell>
                <TableCell>
                  <StatusChip tone={token.state === 'active' ? 'success' : 'neutral'}>
                    {t(`tokens.states.${token.state}`, { defaultValue: token.state })}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  {token.state === 'active' && (
                    <Button variant="ghost" size="sm" onClick={() => setRevokeId(token.id)}>
                      {t('tokens.revoke')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Button className="self-start" onClick={() => setCreating(true)}>
        {t('tokens.create')}
      </Button>

      <Dialog
        open={creating}
        onOpenChange={(open) => {
          setCreating(open);
          if (!open) setCreated(undefined);
        }}
      >
        <DialogContent title={t('tokens.create')}>
          {created ? (
            <div className="flex flex-col gap-3">
              <StatusBanner tone="warning" title={t('tokens.copyNow')} />
              <CopyField value={created} label={t('tokens.token')} />
            </div>
          ) : (
            <form
              className="flex flex-col gap-4"
              onSubmit={(event) => {
                event.preventDefault();
                create.mutate();
              }}
            >
              <FormField label={t('tokens.name')} required>
                <Input value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
              </FormField>
              <FormField label={t('tokens.lifetimeDays')}>
                <Input
                  type="number"
                  min={1}
                  max={730}
                  dir="ltr"
                  value={days}
                  onChange={(e) => setDays(e.target.value)}
                />
              </FormField>
              <fieldset className="flex flex-col gap-2">
                <legend className="mb-1 text-sm font-medium">{t('tokens.scopes')}</legend>
                {tokenScopes.map((scope) => (
                  <Checkbox
                    key={scope}
                    label={t(`tokens.scopeLabels.${scope}`)}
                    description={scope}
                    checked={scopes.includes(scope)}
                    onChange={(e) =>
                      setScopes((current) =>
                        e.target.checked ? [...current, scope] : current.filter((s) => s !== scope),
                      )
                    }
                  />
                ))}
              </fieldset>
              <Button type="submit" loading={create.isPending} disabled={!name.trim() || scopes.length === 0}>
                {tc('actions.create')}
              </Button>
            </form>
          )}
        </DialogContent>
      </Dialog>
      <ConfirmDialog
        open={revokeId !== undefined}
        onOpenChange={(open) => !open && setRevokeId(undefined)}
        title={t('tokens.revokeTitle')}
        description={t('tokens.revokeDescription')}
        confirmLabel={t('tokens.revoke')}
        destructive
        loading={revoke.isPending}
        onConfirm={() => revokeId && revoke.mutate(revokeId)}
      />
    </Section>
  );
}
