import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { waselLoginRoute, waselMfaRoute, waselSetPasswordRoute } from '@/app/router';
import { MfaSetup, RecoveryCodes, type MfaSetupApi } from '@/features/auth/MfaSetup';
import { PasswordPolicyHint } from '@/features/auth/PasswordPolicyHint';
import { Button, FormField, Input, OtpInput, StatusBanner, useToast } from '@/ui';
import { resetWaselAntiforgery, waselApi } from './waselApi';

type Method = 'totp' | 'email' | 'recovery';

function problemKey(failure: unknown): string {
  return failure instanceof ApiError ? failure.problem.messageKey : 'errors.network';
}

/** Only same-portal redirects are followed after sign-in. */
function safeRedirect(redirect: string | undefined): string {
  return redirect?.startsWith('/wasel') && !redirect.startsWith('/wasel/login') ? redirect : '/wasel';
}

/** Wasel sign-in, first step: e-mail and password. MFA always follows (ADR 0110). */
export function WaselLoginPage() {
  const { t } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const { redirect } = waselLoginRoute.useSearch();
  const navigate = useNavigate();
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    setError(undefined);
    try {
      resetWaselAntiforgery();
      const outcome = unwrap(await waselApi.POST('/api/wasel/v1/auth/login', { body: { login, password } }));
      const methods = outcome.methods.join(',');
      if (outcome.status === 'mfaEnrolmentRequired') {
        await navigate({ to: '/wasel/login/mfa', search: { redirect, methods, enrol: 1 } });
      } else if (outcome.status === 'mfaRequired') {
        await navigate({ to: '/wasel/login/mfa', search: { redirect, methods, enrol: undefined } });
      } else {
        window.location.assign(safeRedirect(redirect));
      }
    } catch (failure) {
      setError(problemKey(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('login.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('login.intro')}</p>
      </div>
      {error && <StatusBanner tone="danger" title={translateMessageKey(tc, error)} />}
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <FormField label={t('login.email')}>
          <Input
            dir="ltr"
            type="email"
            autoComplete="username"
            value={login}
            onChange={(event) => setLogin(event.target.value)}
            data-testid="wasel-login-email"
          />
        </FormField>
        <FormField label={t('login.password')}>
          <Input
            dir="ltr"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            data-testid="wasel-login-password"
          />
        </FormField>
        <Button type="submit" loading={busy} disabled={!login || !password} data-testid="wasel-login-submit">
          {t('login.submit')}
        </Button>
      </form>
      <p className="text-xs text-muted-foreground">{t('login.mfaNotice')}</p>
    </div>
  );
}

/** Second factor: verify a code, or enrol a first method (mandatory for vendors), then show the recovery codes once. */
export function WaselMfaPage() {
  const { t } = useTranslation('wasel');
  const { t: ta } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const { redirect, methods, enrol } = waselMfaRoute.useSearch();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [recovery, setRecovery] = useState<string[]>();

  const available = (methods ?? (enrol ? 'totp,email' : 'totp,recovery'))
    .split(',')
    .filter((m): m is Method => ['totp', 'email', 'recovery'].includes(m));

  async function done() {
    resetWaselAntiforgery();
    await queryClient.invalidateQueries({ queryKey: ['wasel'] });
    window.location.assign(safeRedirect(redirect));
  }

  if (enrol) {
    if (recovery) {
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-2xl font-semibold">{t('mfa.enrolledTitle')}</h1>
          <RecoveryCodes codes={recovery} />
          <Button onClick={() => void done()} data-testid="wasel-continue">
            {t('mfa.continue')}
          </Button>
        </div>
      );
    }

    const setupApi: MfaSetupApi = {
      methods: available.filter((m) => m !== 'recovery'),
      startTotp: async () => {
        try {
          return unwrap(await waselApi.POST('/api/wasel/v1/auth/mfa/enrol/totp'));
        } catch (failure) {
          toast({ tone: 'error', title: errorMessage(tc, failure) });
          return undefined;
        }
      },
      startEmail: async () => unwrap(await waselApi.POST('/api/wasel/v1/auth/mfa/enrol/email')),
      confirm: async (method, code) => {
        const result = unwrap(
          await waselApi.POST('/api/wasel/v1/auth/mfa/enrol/confirm', { body: { method, code } }),
        );
        setRecovery(result.recoveryCodes);
      },
    };
    return (
      <div className="flex flex-col gap-6" data-testid="wasel-mfa-enrol">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold">{t('mfa.enrolTitle')}</h1>
          <p className="text-sm text-muted-foreground">{t('mfa.enrolIntro')}</p>
        </div>
        <MfaSetup api={setupApi} />
      </div>
    );
  }

  return <VerifyForm available={available} onDone={done} title={ta('mfa.title')} />;
}

function VerifyForm({
  available,
  onDone,
  title,
}: {
  available: Method[];
  onDone: () => Promise<void>;
  title: string;
}) {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [method, setMethod] = useState<Method>(available[0] ?? 'totp');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [emailSent, setEmailSent] = useState(false);

  async function verify() {
    setBusy(true);
    setError(undefined);
    try {
      unwrap(await waselApi.POST('/api/wasel/v1/auth/mfa/verify', { body: { method, code } }));
      await onDone();
    } catch (failure) {
      setError(problemKey(failure));
      setCode('');
    } finally {
      setBusy(false);
    }
  }

  async function sendEmail() {
    try {
      unwrap(await waselApi.POST('/api/wasel/v1/auth/mfa/email-code'));
      setEmailSent(true);
      toast({ title: t('mfa.emailSent') });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    }
  }

  return (
    <div className="flex flex-col gap-6" data-testid="wasel-mfa-verify">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{title}</h1>
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
        <Button type="submit" loading={busy} disabled={code.trim().length < 6} data-testid="wasel-mfa-submit">
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

/** Invitation / reset link target: sets the vendor's password, then sign in (MFA enrolment follows). */
export function WaselSetPasswordPage() {
  const { t } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const { user, token, invite } = waselSetPasswordRoute.useSearch();
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const policy = useQuery({
    queryKey: ['identity', 'password-policy'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/auth/password-policy', { signal })),
  });

  async function submit() {
    if (password !== confirm) {
      setError('wasel.setPassword.mismatch');
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      unwrap(
        await waselApi.POST('/api/wasel/v1/auth/password/reset', {
          body: { userId: user, token, newPassword: password, invitation: invite === 1 },
        }),
      );
      await navigate({ to: '/wasel/login', search: { redirect: undefined } });
    } catch (failure) {
      if (failure instanceof ApiError && failure.problem.errors?.length) {
        setError(failure.problem.errors[0]?.messageKey);
      } else {
        setError(problemKey(failure));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">
          {invite === 1 ? t('setPassword.inviteTitle') : t('setPassword.title')}
        </h1>
        <p className="text-sm text-muted-foreground">{t('setPassword.intro')}</p>
      </div>
      {(!user || !token) && <StatusBanner tone="danger" title={t('setPassword.invalidLink')} />}
      {error && <StatusBanner tone="danger" title={translateMessageKey(tc, error)} />}
      {policy.data && <PasswordPolicyHint policy={policy.data} />}
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <FormField label={t('setPassword.password')}>
          <Input
            dir="ltr"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            data-testid="wasel-new-password"
          />
        </FormField>
        <FormField label={t('setPassword.confirm')}>
          <Input
            dir="ltr"
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(event) => setConfirm(event.target.value)}
            data-testid="wasel-confirm-password"
          />
        </FormField>
        <Button
          type="submit"
          loading={busy}
          disabled={!password || !user || !token}
          data-testid="wasel-set-password"
        >
          {t('setPassword.submit')}
        </Button>
      </form>
    </div>
  );
}
