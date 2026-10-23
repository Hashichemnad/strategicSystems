import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { useMe } from '@/features/auth/session';
import { Button, FormField, Input, PageHeader, Select, Skeleton, StatusBanner, Switch, useToast } from '@/ui';

type Input = components['schemas']['SmtpSettingsInput'];

/** Outgoing mail server for password reset, sign-in codes and invitations (Notifications reuse it in Phase 3). */
export function EmailSettingsPage() {
  const { t } = useTranslation('admin');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const me = useMe();
  const queryClient = useQueryClient();
  const current = useQuery({
    queryKey: ['identity', 'smtp'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/admin/smtp', { signal })),
  });
  const [draft, setDraft] = useState<Input>();
  const [testTo, setTestTo] = useState('');
  const [errors, setErrors] = useState<components['schemas']['Problem']['errors']>();
  useEffect(() => {
    if (!current.data) return;
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- drop the read-only flag from the editable draft
    const { hasPassword, ...rest } = current.data;
    setDraft({ ...rest, password: null, clearPassword: false });
  }, [current.data]);
  useEffect(() => {
    if (me?.email && !testTo) setTestTo(me.email);
  }, [me?.email, testTo]);

  const onError = (failure: unknown) => {
    if (failure instanceof ApiError && failure.problem.errors?.length) setErrors(failure.problem.errors);
    else toast({ tone: 'error', title: errorMessage(tc, failure) });
  };
  const save = useMutation({
    mutationFn: async (body: Input) => unwrap(await api.PUT('/api/v1/identity/admin/smtp', { body })),
    onSuccess: (saved) => {
      setErrors(undefined);
      queryClient.setQueryData(['identity', 'smtp'], saved);
      toast({ title: t('saved') });
    },
    onError,
  });
  const test = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/identity/admin/smtp/test', { body: { to: testTo, settings: draft ?? null } }),
      ),
    onSuccess: () => toast({ title: t('email.testSent', { to: testTo }) }),
    onError,
  });

  if (!draft)
    return current.isError ? (
      <StatusBanner tone="danger" title={errorMessage(tc, current.error)} />
    ) : (
      <Skeleton className="h-96" />
    );
  const error = (field: string) => {
    const e = errors?.find((x) => x.field === field);
    return e ? translateMessageKey(tc, e.messageKey, e.params ?? undefined) : undefined;
  };
  const text = (
    key: 'host' | 'userName' | 'fromAddress' | 'fromNameAr' | 'fromNameEn' | 'publicBaseUrl',
    dir: 'ltr' | 'rtl' = 'ltr',
  ) => (
    <FormField
      label={t(`email.fields.${key}`)}
      error={error(key)}
      hint={key === 'publicBaseUrl' ? t('email.publicBaseUrlHint') : undefined}
    >
      <Input
        dir={dir}
        value={draft[key] ?? ''}
        onChange={(e) => setDraft({ ...draft, [key]: e.target.value || null })}
      />
    </FormField>
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('email.title')} description={t('email.description')} />
      <form
        className="flex flex-col gap-6"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(draft);
        }}
      >
        <div className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <Switch
            className="sm:col-span-2"
            checked={draft.enabled}
            onCheckedChange={(enabled) => setDraft({ ...draft, enabled })}
            label={t('email.fields.enabled')}
          />
          {text('host')}
          <FormField label={t('email.fields.port')} error={error('port')}>
            <Input
              type="number"
              dir="ltr"
              value={draft.port}
              onChange={(e) => setDraft({ ...draft, port: Number(e.target.value) })}
            />
          </FormField>
          <FormField label={t('email.fields.security')} error={error('security')}>
            <Select
              value={draft.security}
              onValueChange={(security) => setDraft({ ...draft, security })}
              options={(['starttls', 'tls', 'none'] as const).map((value) => ({
                value,
                label: t(`email.security.${value}`),
              }))}
            />
          </FormField>
          {text('userName')}
          <FormField
            label={t('email.fields.password')}
            hint={current.data?.hasPassword ? t('email.passwordKept') : undefined}
          >
            <Input
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={draft.password ?? ''}
              onChange={(e) => setDraft({ ...draft, password: e.target.value || null })}
            />
          </FormField>
          {text('fromAddress')}
          {text('fromNameAr', 'rtl')}
          {text('fromNameEn')}
          {text('publicBaseUrl')}
        </div>
        <Button type="submit" loading={save.isPending} className="self-start">
          {tc('actions.save')}
        </Button>
      </form>
      <div className="flex flex-wrap items-end gap-3 rounded-lg border bg-card p-5">
        <FormField label={t('email.testTo')} className="min-w-64 flex-1">
          <Input dir="ltr" type="email" value={testTo} onChange={(e) => setTestTo(e.target.value)} />
        </FormField>
        <Button variant="outline" loading={test.isPending} onClick={() => test.mutate()}>
          {t('email.sendTest')}
        </Button>
      </div>
    </div>
  );
}
