import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { enrolRoute } from '@/app/router';
import { Button, StatusBanner, useToast } from '@/ui';
import { MfaSetup, RecoveryCodes, type MfaSetupApi } from './MfaSetup';
import { safeRedirect, useCompleteSignIn } from './session';

/** The security policy requires a second factor and the user has none yet: set one up, then continue. */
export function EnrolPage() {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const { redirect, methods } = enrolRoute.useSearch();
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const toast = useToast();
  const [codes, setCodes] = useState<string[]>();
  const [expired, setExpired] = useState(false);

  const handle = async <T,>(call: () => Promise<T>): Promise<T | undefined> => {
    try {
      return await call();
    } catch (failure) {
      if (failure instanceof ApiError && failure.problem.code === 'auth.mfaExpired') setExpired(true);
      else toast({ tone: 'error', title: errorMessage(tc, failure) });
      return undefined;
    }
  };

  const setupApi: MfaSetupApi = {
    methods: (methods ?? 'totp,email').split(',').filter((m) => m === 'totp' || m === 'email'),
    startTotp: () => handle(async () => unwrap(await api.POST('/api/v1/identity/auth/mfa/enrol/totp'))),
    startEmail: () => handle(async () => unwrap(await api.POST('/api/v1/identity/auth/mfa/enrol/email'))),
    confirm: async (method, code) => {
      const result = unwrap(
        await api.POST('/api/v1/identity/auth/mfa/enrol/confirm', { body: { method, code } }),
      );
      await completeSignIn();
      setCodes(result.recoveryCodes);
    },
  };

  if (expired) {
    return (
      <StatusBanner
        tone="warning"
        title={translateMessageKey(tc, 'auth.errors.mfaExpired')}
        actions={
          <Button variant="outline" onClick={() => void navigate({ to: '/login', search: { redirect } })}>
            {t('login.backToLogin')}
          </Button>
        }
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('enrol.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('enrol.description')}</p>
      </div>
      {codes ? (
        <div className="flex flex-col gap-4">
          <RecoveryCodes codes={codes} />
          <Button onClick={() => void navigate({ href: safeRedirect(redirect) })}>
            {t('enrol.continue')}
          </Button>
        </div>
      ) : (
        <MfaSetup api={setupApi} />
      )}
    </div>
  );
}
