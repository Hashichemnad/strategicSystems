import QRCode from 'qrcode';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { Button, CopyField, OtpInput, StatusBanner, Tabs, TabsContent, TabsList, TabsTrigger } from '@/ui';

type TotpSetup = components['schemas']['TotpSetupDto'];

export interface MfaSetupApi {
  methods: string[];
  startTotp: () => Promise<TotpSetup | undefined>;
  startEmail: () => Promise<unknown>;
  /** Throws ApiError on a wrong code. */
  confirm: (method: 'totp' | 'email', code: string) => Promise<void>;
}

/** Set up an authenticator app (QR code + manual key) or email codes. Shared by sign-in enrolment and the profile. */
export function MfaSetup({ api }: { api: MfaSetupApi }) {
  const { t } = useTranslation('auth');
  const first = api.methods[0] === 'email' ? 'email' : 'totp';
  return (
    <Tabs defaultValue={first}>
      {api.methods.length > 1 && (
        <TabsList>
          {api.methods.includes('totp') && <TabsTrigger value="totp">{t('setup.totpTab')}</TabsTrigger>}
          {api.methods.includes('email') && <TabsTrigger value="email">{t('setup.emailTab')}</TabsTrigger>}
        </TabsList>
      )}
      {api.methods.includes('totp') && (
        <TabsContent value="totp">
          <TotpSetupPanel api={api} />
        </TabsContent>
      )}
      {api.methods.includes('email') && (
        <TabsContent value="email">
          <EmailSetupPanel api={api} />
        </TabsContent>
      )}
    </Tabs>
  );
}

function TotpSetupPanel({ api }: { api: MfaSetupApi }) {
  const { t } = useTranslation('auth');
  const [setup, setSetup] = useState<TotpSetup>();
  const [qr, setQr] = useState<string>();

  useEffect(() => {
    if (!setup) return;
    QRCode.toDataURL(setup.authenticatorUri, { margin: 1, width: 192 })
      .then(setQr)
      .catch(() => setQr(undefined));
  }, [setup]);

  if (!setup) {
    return (
      <div className="flex flex-col gap-3 pt-4">
        <p className="text-sm text-muted-foreground">{t('setup.totpIntro')}</p>
        <Button onClick={() => void api.startTotp().then(setSetup)}>{t('setup.totpStart')}</Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 pt-4">
      <ol className="flex list-decimal flex-col gap-1 ps-5 text-sm">
        <li>{t('setup.totpStep1')}</li>
        <li>{t('setup.totpStep2')}</li>
        <li>{t('setup.totpStep3')}</li>
      </ol>
      {qr && (
        <img src={qr} alt={t('setup.qrAlt')} className="size-48 self-center rounded-md border bg-white p-2" />
      )}
      <CopyField value={setup.sharedKey} label={t('setup.manualKey')} />
      <ConfirmCode method="totp" api={api} />
    </div>
  );
}

function EmailSetupPanel({ api }: { api: MfaSetupApi }) {
  const { t } = useTranslation('auth');
  const [sent, setSent] = useState(false);
  return (
    <div className="flex flex-col gap-3 pt-4">
      <p className="text-sm text-muted-foreground">{t('setup.emailIntro')}</p>
      <Button
        variant={sent ? 'outline' : 'primary'}
        onClick={() => void api.startEmail().then(() => setSent(true))}
      >
        {sent ? t('mfa.resendEmail') : t('mfa.sendEmail')}
      </Button>
      {sent && <ConfirmCode method="email" api={api} />}
    </div>
  );
}

function ConfirmCode({ method, api }: { method: 'totp' | 'email'; api: MfaSetupApi }) {
  const { t } = useTranslation('auth');
  const { t: tc } = useTranslation();
  const [code, setCode] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setBusy(true);
        setError(undefined);
        api
          .confirm(method, code)
          .catch((failure: unknown) => {
            setError(failure instanceof ApiError ? failure.problem.messageKey : 'errors.network');
            setCode('');
          })
          .finally(() => setBusy(false));
      }}
    >
      {error && <StatusBanner tone="danger" title={translateMessageKey(tc, error)} />}
      <OtpInput value={code} onChange={setCode} label={t('mfa.code')} invalid={!!error} />
      <Button type="submit" loading={busy} disabled={code.length < 6}>
        {t('setup.confirm')}
      </Button>
    </form>
  );
}

/** Recovery codes, shown once after MFA is set up or the codes are regenerated. */
export function RecoveryCodes({ codes }: { codes: readonly string[] }) {
  const { t } = useTranslation('auth');
  return (
    <div className="flex flex-col gap-3">
      <StatusBanner tone="warning" title={t('recovery.title')} description={t('recovery.description')} />
      <CopyField value={codes.join('\n')} label={t('recovery.title')} />
    </div>
  );
}
