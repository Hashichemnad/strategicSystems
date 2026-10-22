import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { mfaRoute } from '@/app/router';
import { Button, FormField, Input, OtpInput, StatusBanner, useToast } from '@/ui';
import { safeRedirect, useCompleteSignIn } from './session';

type Method = 'totp' | 'email' | 'recovery';

/** Second step of sign-in: authenticator code, emailed code or a recovery code. */
export function MfaPage() {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const { redirect, methods } = mfaRoute.useSearch();
  const available = (methods ?? 'totp,recovery')
    .split(',')
    .filter((m): m is Method => ['totp', 'email', 'recovery'].includes(m));
  const [method, setMethod] = useState<Method>(available[0] ?? 'totp');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [emailSent, setEmailSent] = useState(false);
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const toast = useToast();

  async function verify() {
    setBusy(true);
    setError(undefined);
    try {
      unwrap(await api.POST('/api/v1/identity/auth/mfa/verify', { body: { method, code } }));
      await completeSignIn();
      await navigate({ href: safeRedirect(redirect) });
    } catch (failure) {
      if (failure instanceof ApiError && failure.problem.code === 'auth.mfaExpired') {
        await navigate({ to: '/login', search: { redirect, error: failure.problem.messageKey } });
        return;
      }
      setError(failure instanceof ApiError ? failure.problem.messageKey : 'errors.network');
      setCode('');
    } finally {
      setBusy(false);
    }
  }

  async function sendEmail() {
    try {
      unwrap(await api.POST('/api/v1/identity/auth/mfa/email-code'));
      setEmailSent(true);
      toast({ title: t('mfa.emailSent') });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('mfa.title')}</h1>
        <p className="text-sm text-muted-foreground">{t(`mfa.instructions.${method}`)}</p>
      </div>
      {error && <StatusBanner tone="danger" title={translateMessageKey(tc, error)} />}
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          void verify();
        }}
      >
        {method === 'recovery' ? (
          <FormField label={t('mfa.recoveryCode')}>
            <Input
              dir="ltr"
              autoComplete="off"
              value={code}
              onChange={(event) => setCode(event.target.value)}
            />
          </FormField>
        ) : (
          <div className="flex flex-col gap-2">
            {method === 'email' && (
              <Button type="button" variant="outline" onClick={() => void sendEmail()}>
                {emailSent ? t('mfa.resendEmail') : t('mfa.sendEmail')}
              </Button>
            )}
            <OtpInput value={code} onChange={setCode} label={t('mfa.code')} invalid={!!error} />
          </div>
        )}
        <Button type="submit" loading={busy} disabled={code.trim().length < 6}>
          {t('mfa.verify')}
        </Button>
      </form>
      {available.length > 1 && (
        <div className="flex flex-wrap gap-2 border-t pt-4">
          {available
            .filter((m) => m !== method)
            .map((m) => (
              <Button
                key={m}
                variant="link"
                className="h-auto p-0"
                onClick={() => {
                  setMethod(m);
                  setCode('');
                  setError(undefined);
                }}
              >
                {t(`mfa.use.${m}`)}
              </Button>
            ))}
        </div>
      )}
    </div>
  );
}
