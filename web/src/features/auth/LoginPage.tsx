import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { KeyRound } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { api, ApiError, resetAntiforgeryToken, unwrap } from '@/api/client';
import { errorMessage, translateFieldMessage, translateMessageKey } from '@/api/problem';
import { loginRoute } from '@/app/router';
import { pickText } from '@/lib/format';
import { Button, FormField, Input, StatusBanner } from '@/ui';
import { safeRedirect, useCompleteSignIn } from './session';

const schema = z.object({
  login: z.string().trim().min(1, 'validation.notEmpty').max(256),
  password: z.string().min(1, 'validation.notEmpty').max(1024),
});

type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const { redirect, error: externalError } = loginRoute.useSearch();
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const providers = useQuery({
    queryKey: ['identity', 'providers'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/auth/providers', { signal })),
  });
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { login: '', password: '' },
  });
  const { errors, isSubmitting } = form.formState;

  const submit = form.handleSubmit(async (values) => {
    try {
      const outcome = unwrap(await api.POST('/api/v1/identity/auth/login', { body: values }));
      const methods = outcome.methods.join(',');
      if (outcome.status === 'mfaRequired') {
        await navigate({ to: '/login/mfa', search: { redirect, methods } });
      } else if (outcome.status === 'mfaEnrolmentRequired') {
        await navigate({ to: '/login/enrol', search: { redirect, methods } });
      } else {
        await completeSignIn();
        await navigate({ href: safeRedirect(redirect) });
      }
    } catch (failure) {
      resetAntiforgeryToken();
      form.setError('root', {
        message: failure instanceof ApiError ? failure.problem.messageKey : 'errors.network',
      });
    }
  });

  const rootError = errors.root?.message;
  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('login.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('login.description')}</p>
      </div>

      {externalError && <StatusBanner tone="danger" title={translateMessageKey(tc, externalError)} />}
      {rootError && <StatusBanner tone="danger" title={translateMessageKey(tc, rootError)} />}

      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        <FormField label={t('login.userName')} error={translateFieldMessage(tc, errors.login?.message)}>
          <Input
            dir="ltr"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            {...form.register('login')}
          />
        </FormField>
        <FormField label={t('login.password')} error={translateFieldMessage(tc, errors.password?.message)}>
          <Input type="password" dir="ltr" autoComplete="current-password" {...form.register('password')} />
        </FormField>
        <div className="flex justify-end">
          <Link to="/forgot-password" className="text-sm text-primary hover:underline">
            {t('login.forgot')}
          </Link>
        </div>
        <Button type="submit" loading={isSubmitting} className="w-full">
          {t('login.submit')}
        </Button>
      </form>

      {(providers.data?.length ?? 0) > 0 && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center gap-3 text-xs text-muted-foreground">
            <span className="h-px flex-1 bg-border" />
            {t('login.or')}
            <span className="h-px flex-1 bg-border" />
          </div>
          {providers.data?.map((provider) => (
            <Button key={provider.code} variant="outline" className="w-full" asChild>
              <a href={`${provider.challengeUrl}?returnUrl=${encodeURIComponent(safeRedirect(redirect))}`}>
                <KeyRound aria-hidden />
                {t('login.continueWith', {
                  name:
                    pickText(document.documentElement.lang, provider.displayNameAr, provider.displayNameEn) ||
                    provider.code,
                })}
              </a>
            </Button>
          ))}
        </div>
      )}
      {providers.isError && (
        <p className="text-xs text-muted-foreground">{errorMessage(tc, providers.error)}</p>
      )}
    </div>
  );
}
