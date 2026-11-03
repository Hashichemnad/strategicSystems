import { ShieldCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { consentRoute } from '@/app/router';
import { Button, Skeleton, StatusBanner } from '@/ui';
import { useGrantMcpConsent, useMcpConsent } from './api';

/**
 * OAuth consent for MCP clients (docs/design/mcp.md §2): the signed-in user allows or denies a client. Allow asks the server for a short-lived
 * ticket bound to this request (user, client, redirect URI, PKCE challenge); the browser then continues the authorization request with it.
 */
export function ConsentPage() {
  const { t } = useTranslation('mcp');
  const { t: tc } = useTranslation();
  const search = consentRoute.useSearch();
  const clientId = search.client_id ?? '';
  const redirectUri = search.redirect_uri ?? '';
  const info = useMcpConsent(clientId, redirectUri);
  const grant = useGrantMcpConsent();
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(search)) if (typeof value === 'string') query.set(key, value);
  const continueWith = (extra: Record<string, string>) => {
    for (const [key, value] of Object.entries(extra)) query.set(key, value);
    window.location.assign(`/connect/authorize?${query.toString()}`);
  };

  if (info.isPending) return <Skeleton className="h-64" />;
  if (info.isError) return <StatusBanner tone="danger" title={t('consent.notFound')} />;
  if (!info.data.enabled) return <StatusBanner tone="warning" title={t('consent.disabled')} />;
  const client = info.data.clientName ?? info.data.clientId;
  return (
    <div className="flex flex-col gap-5" data-testid="mcp-consent">
      <div className="flex items-center gap-3">
        <ShieldCheck className="size-8 text-primary" aria-hidden />
        <h1 className="text-xl font-semibold">{t('consent.title', { client })}</h1>
      </div>
      <p className="text-sm text-muted-foreground">{t('consent.description', { client })}</p>
      <div className="rounded-lg border bg-muted/40 p-4 text-sm">
        <p className="font-medium">{t('consent.can')}</p>
        <ul className="ms-5 mt-1 list-disc">
          <li>{t('consent.canRead')}</li>
          <li>{t('consent.canAct')}</li>
        </ul>
        <p className="mt-3">{t('consent.cannot')}</p>
      </div>
      <p className="text-xs text-muted-foreground">
        {t('consent.redirect', { host: info.data.redirectHost ?? '' })}
      </p>
      {grant.isError && <StatusBanner tone="danger" title={errorMessage(tc, grant.error)} />}
      <div className="flex gap-3">
        <Button
          loading={grant.isPending}
          onClick={() =>
            grant.mutate(
              { clientId, redirectUri, codeChallenge: search.code_challenge ?? '' },
              { onSuccess: (result) => continueWith({ consent_ticket: result.ticket }) },
            )
          }
        >
          {t('consent.allow')}
        </Button>
        <Button variant="outline" onClick={() => continueWith({ consent: 'deny' })}>
          {t('consent.deny')}
        </Button>
      </div>
    </div>
  );
}
