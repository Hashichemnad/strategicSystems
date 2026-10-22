import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { resetRoute } from '@/app/router';
import { Button, FormField, Input, StatusBanner } from '@/ui';
import { PasswordPolicyHint } from './PasswordPolicyHint';

/** Set a new password from an emailed link (reset, or first password from an invitation). */
export function ResetPasswordPage() {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const { t: tv } = useTranslation('validation');
  const { user, token, invite } = resetRoute.useSearch();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  const policy = useQuery({
    queryKey: ['identity', 'password-policy'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/auth/password-policy', { signal })),
  });
  const invitation = invite === 1;

  async function submit() {
    if (password !== confirm) {
      setErrors(['auth.errors.passwordsDiffer']);
      return;
    }

    setBusy(true);
    try {
      unwrap(
        await api.POST('/api/v1/identity/auth/password/reset', {
          body: { userId: user, token, newPassword: password, invitation },
        }),
      );
      setDone(true);
    } catch (failure) {
      if (failure instanceof ApiError) {
        const fields = failure.problem.errors?.map((e) => e.messageKey) ?? [];
        setErrors(fields.length ? fields : [failure.problem.messageKey]);
      } else {
        setErrors([errorMessage(tc, failure)]);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold">{invitation ? t('reset.inviteTitle') : t('reset.title')}</h1>
      {!user || !token ? (
        <StatusBanner tone="danger" title={translateMessageKey(tc, 'auth.errors.resetTokenInvalid')} />
      ) : done ? (
        <StatusBanner
          tone="success"
          title={t('reset.done')}
          actions={
            <Button asChild>
              <Link to="/login" search={{}}>
                {t('login.submit')}
              </Link>
            </Button>
          }
        />
      ) : (
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          {errors.length > 0 && (
            <StatusBanner
              tone="danger"
              title={tv('title')}
              description={
                <ul className="list-disc ps-5">
                  {errors.map((key) => (
                    <li key={key}>
                      {translateMessageKey(tc, key, {
                        length: policy.data?.minLength,
                        count: policy.data?.history,
                      })}
                    </li>
                  ))}
                </ul>
              }
            />
          )}
          {policy.data && <PasswordPolicyHint policy={policy.data} />}
          <FormField label={t('reset.newPassword')}>
            <Input
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </FormField>
          <FormField label={t('reset.confirmPassword')}>
            <Input
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={confirm}
              onChange={(event) => setConfirm(event.target.value)}
            />
          </FormField>
          <Button type="submit" loading={busy} disabled={!password}>
            {t('reset.submit')}
          </Button>
        </form>
      )}
    </div>
  );
}
