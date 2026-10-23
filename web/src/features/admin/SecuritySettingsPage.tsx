import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { Button, FormField, Input, PageHeader, Select, Skeleton, StatusBanner, Switch, useToast } from '@/ui';

type Settings = components['schemas']['SecuritySettingsDto'];
type NumberKey =
  | 'passwordMinLength'
  | 'passwordHistory'
  | 'lockoutThreshold'
  | 'lockoutMinutes'
  | 'sessionIdleMinutes'
  | 'sessionAbsoluteHours'
  | 'tokenDefaultDays'
  | 'tokenMaxDays';
type FlagKey =
  | 'passwordRequireUpper'
  | 'passwordRequireLower'
  | 'passwordRequireDigit'
  | 'passwordRequireSymbol'
  | 'mfaAllowTotp'
  | 'mfaAllowEmail'
  | 'requireLocalMfaAfterSso';

/** Password policy, lockout, session limits, MFA policy and token lifetime; applied at runtime without a restart. */
export function SecuritySettingsPage() {
  const { t } = useTranslation('admin');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const current = useQuery({
    queryKey: ['identity', 'security-settings'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/identity/admin/security-settings', { signal })),
  });
  const [draft, setDraft] = useState<Settings>();
  const [fieldErrors, setFieldErrors] = useState<components['schemas']['Problem']['errors']>();
  useEffect(() => {
    if (current.data) setDraft(current.data);
  }, [current.data]);
  const save = useMutation({
    mutationFn: async (body: Settings) =>
      unwrap(await api.PUT('/api/v1/identity/admin/security-settings', { body })),
    onSuccess: (saved) => {
      setFieldErrors(undefined);
      queryClient.setQueryData(['identity', 'security-settings'], saved);
      toast({ title: t('saved') });
    },
    onError: (failure) => {
      if (failure instanceof ApiError && failure.problem.errors?.length)
        setFieldErrors(failure.problem.errors);
      else toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
  });

  if (!draft)
    return current.isError ? (
      <StatusBanner tone="danger" title={errorMessage(tc, current.error)} />
    ) : (
      <Skeleton className="h-96" />
    );
  const error = (field: string) => {
    const e = fieldErrors?.find((x) => x.field === field);
    return e ? translateMessageKey(tc, e.messageKey, e.params ?? undefined) : undefined;
  };
  const number = (key: NumberKey) => (
    <FormField key={key} label={t(`security.fields.${key}`)} error={error(key)}>
      <Input
        type="number"
        dir="ltr"
        value={draft[key]}
        onChange={(e) => setDraft({ ...draft, [key]: Number(e.target.value) })}
      />
    </FormField>
  );
  const flag = (key: FlagKey) => (
    <Switch
      key={key}
      checked={draft[key]}
      onCheckedChange={(value) => setDraft({ ...draft, [key]: value })}
      label={t(`security.fields.${key}`)}
    />
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('security.title')} description={t('security.description')} />
      <form
        className="flex flex-col gap-6"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(draft);
        }}
      >
        <fieldset className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <legend className="px-1 font-semibold">{t('security.password')}</legend>
          {number('passwordMinLength')}
          {number('passwordHistory')}
          <div className="flex flex-col gap-3 sm:col-span-2">
            {flag('passwordRequireUpper')}
            {flag('passwordRequireLower')}
            {flag('passwordRequireDigit')}
            {flag('passwordRequireSymbol')}
          </div>
        </fieldset>
        <fieldset className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <legend className="px-1 font-semibold">{t('security.lockoutAndSessions')}</legend>
          {number('lockoutThreshold')}
          {number('lockoutMinutes')}
          {number('sessionIdleMinutes')}
          {number('sessionAbsoluteHours')}
        </fieldset>
        <fieldset className="flex flex-col gap-4 rounded-lg border bg-card p-5">
          <legend className="px-1 font-semibold">{t('security.mfa')}</legend>
          <FormField label={t('security.fields.mfaPolicy')} error={error('mfaPolicy')} className="max-w-sm">
            <Select
              value={draft.mfaPolicy}
              onValueChange={(value) => setDraft({ ...draft, mfaPolicy: value })}
              options={(['optional', 'admins', 'all'] as const).map((value) => ({
                value,
                label: t(`security.mfaPolicies.${value}`),
              }))}
            />
          </FormField>
          {flag('mfaAllowTotp')}
          {flag('mfaAllowEmail')}
          {flag('requireLocalMfaAfterSso')}
          {error('mfaAllowTotp') && <p className="text-sm text-danger">{error('mfaAllowTotp')}</p>}
        </fieldset>
        <fieldset className="grid gap-4 rounded-lg border bg-card p-5 sm:grid-cols-2">
          <legend className="px-1 font-semibold">{t('security.tokens')}</legend>
          {number('tokenDefaultDays')}
          {number('tokenMaxDays')}
        </fieldset>
        <Button type="submit" loading={save.isPending} className="self-start">
          {tc('actions.save')}
        </Button>
      </form>
    </div>
  );
}
