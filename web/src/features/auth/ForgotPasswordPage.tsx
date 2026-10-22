import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { Button, FormField, Input, StatusBanner } from '@/ui';

/** Always shows the same confirmation, whether or not the account exists (no account enumeration). */
export function ForgotPasswordPage() {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const [login, setLogin] = useState('');
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<unknown>();
  const [busy, setBusy] = useState(false);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('forgot.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('forgot.description')}</p>
      </div>
      {sent ? (
        <StatusBanner tone="success" title={t('forgot.sent')} description={t('forgot.sentDescription')} />
      ) : (
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (!login.trim()) return;
            setBusy(true);
            void api
              .POST('/api/v1/identity/auth/password/forgot', { body: { login } })
              .then((result) => {
                unwrap(result);
                setSent(true);
              })
              .catch(setError)
              .finally(() => setBusy(false));
          }}
        >
          {error !== undefined && <StatusBanner tone="danger" title={errorMessage(tc, error)} />}
          <FormField label={t('login.userName')}>
            <Input
              dir="ltr"
              autoComplete="username"
              value={login}
              onChange={(event) => setLogin(event.target.value)}
            />
          </FormField>
          <Button type="submit" loading={busy}>
            {t('forgot.submit')}
          </Button>
        </form>
      )}
      <Link to="/login" search={{}} className="text-sm text-primary hover:underline">
        {t('login.backToLogin')}
      </Link>
    </div>
  );
}
