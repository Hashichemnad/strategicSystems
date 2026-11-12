import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import {
  CopyField,
  EmptyState,
  PageHeader,
  Select,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/ui';
import { useDeveloperPortal, usePortalMetadata, useWebhookEvents } from './clients';

const verifySample = `// Node.js — verify X-Strategy-Signature (HMAC-SHA256 of "<timestamp>.<body>")
import crypto from 'node:crypto';

function verify(secret, headers, rawBody) {
  const timestamp = headers['x-strategy-timestamp'];
  if (Math.abs(Date.now() / 1000 - Number(timestamp)) > 300) return false; // 5 minutes
  const expected = 'v1=' + crypto.createHmac('sha256', secret).update(\`\${timestamp}.\${rawBody}\`).digest('hex');
  const given = headers['x-strategy-signature'] ?? '';
  return given.length === expected.length && crypto.timingSafeEqual(Buffer.from(given), Buffer.from(expected));
}`;

const powerQuerySample = `let
    Source = OData.Feed("{url}", null, [Implementation = "2.0", Query = [lang = "{lang}"]]),
    Projects = Source{[Name = "Projects"]}[Data]
in
    Projects`;

const POWER_BI_STEPS = ['1', '2', '3', '4', '5', '6', '7'] as const;

/** Developer portal (SPEC-ADDENDUM §F, ADR 0195): API, OData feeds and metadata, extracts, webhook catalogue and the Power BI guide. */
export function DeveloperPortalPage() {
  const { t, i18n } = useTranslation('integration');
  const portal = useDeveloperPortal();
  const events = useWebhookEvents();
  const [system, setSystem] = useState<string | undefined>();
  const chosen = system ?? portal.data?.systems[0]?.code;
  const metadata = usePortalMetadata(chosen);
  const [event, setEvent] = useState<string | undefined>();
  const chosenEvent = events.data?.find((e) => e.code === event) ?? events.data?.[0];
  if (portal.isPending) return <Skeleton className="h-64 rounded-lg" />;
  const data = portal.data;
  const firstFeed = data?.systems.find((s) => s.code === 'portfolio') ?? data?.systems[0];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('portal.title')} description={t('portal.description')} />
      <Tabs defaultValue="feeds">
        <TabsList>
          <TabsTrigger value="feeds">{t('portal.tabs.feeds')}</TabsTrigger>
          <TabsTrigger value="extracts">{t('portal.tabs.extracts')}</TabsTrigger>
          <TabsTrigger value="webhooks">{t('portal.tabs.webhooks')}</TabsTrigger>
          <TabsTrigger value="powerbi">{t('portal.tabs.powerbi')}</TabsTrigger>
          <TabsTrigger value="api">{t('portal.tabs.api')}</TabsTrigger>
        </TabsList>

        <TabsContent value="feeds" className="flex flex-col gap-4 pt-4">
          <p className="text-sm text-muted-foreground">{t('portal.feedsIntro')}</p>
          {(data?.systems ?? []).length === 0 ? (
            <EmptyState title={t('portal.noSystems')} />
          ) : (
            <>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('portal.feedUrl')}</TableHeaderCell>
                    <TableHeaderCell>{t('portal.entitySets')}</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {data?.systems.map((s) => (
                    <TableRow key={s.code}>
                      <TableCell dir="ltr" className="text-start font-mono text-xs">
                        {s.oDataUrl}
                      </TableCell>
                      <TableCell>
                        {s.recordTypes.map((r) => (
                          <div key={r.code}>
                            <span dir="ltr" className="font-mono text-xs">
                              {r.entitySet}
                            </span>{' '}
                            — {pickText(i18n.language, r.labelAr, r.labelEn)}
                            {r.relationships.length > 0 && (
                              <span className="text-xs text-muted-foreground">
                                {' '}
                                ($expand: <span dir="ltr">{r.relationships.join(', ')}</span>)
                              </span>
                            )}
                          </div>
                        ))}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <div className="flex flex-col gap-2">
                <div className="flex items-center gap-3">
                  <h2 className="text-base font-semibold">{t('portal.metadata')}</h2>
                  <div className="w-56">
                    <Select
                      value={chosen}
                      onValueChange={setSystem}
                      aria-label={t('portal.system')}
                      options={(data?.systems ?? []).map((s) => ({ value: s.code, label: s.code }))}
                    />
                  </div>
                </div>
                <pre
                  dir="ltr"
                  data-testid="odata-metadata"
                  className="max-h-96 overflow-auto rounded-md bg-muted p-3 text-start text-xs"
                >
                  {metadata.data ?? ''}
                </pre>
              </div>
              <ul className="list-disc ps-6 text-sm">
                <li>{t('portal.query.select')}</li>
                <li>{t('portal.query.filter')}</li>
                <li>{t('portal.query.expand')}</li>
                <li>{t('portal.query.paging')}</li>
                <li>{t('portal.query.labels')}</li>
                <li>{t('portal.query.scope')}</li>
              </ul>
            </>
          )}
        </TabsContent>

        <TabsContent value="extracts" className="flex flex-col gap-3 pt-4">
          <p className="text-sm text-muted-foreground">{t('portal.extractsIntro')}</p>
          <CopyField
            value={`${data?.baseUrl ?? ''}/api/v1/integration/extracts/{system}/{EntitySet}?changedSince=2026-11-01T00:00:00Z&format=json`}
            label={t('portal.changed')}
          />
          <CopyField
            value={`${data?.baseUrl ?? ''}/api/v1/integration/extracts/{system}/{EntitySet}/deleted?changedSince=2026-11-01T00:00:00Z`}
            label={t('portal.deleted')}
          />
          <ul className="list-disc ps-6 text-sm">
            <li>{t('portal.extracts.cursor')}</li>
            <li>{t('portal.extracts.watermark')}</li>
            <li>{t('portal.extracts.deleted')}</li>
            <li>{t('portal.extracts.csv')}</li>
          </ul>
        </TabsContent>

        <TabsContent value="webhooks" className="flex flex-col gap-4 pt-4">
          <p className="text-sm text-muted-foreground">{t('portal.webhooksIntro')}</p>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('webhooks.event')}</TableHeaderCell>
                <TableHeaderCell>{t('portal.description_')}</TableHeaderCell>
                <TableHeaderCell>{t('portal.resource')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {events.data?.map((e) => (
                <TableRow key={e.code}>
                  <TableCell dir="ltr" className="text-start font-mono text-xs">
                    {e.code}
                  </TableCell>
                  <TableCell>{pickText(i18n.language, e.labelAr, e.labelEn)}</TableCell>
                  <TableCell dir="ltr" className="text-start font-mono text-xs">
                    {e.resourceType}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {chosenEvent && (
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-3">
                <h2 className="text-base font-semibold">{t('portal.samplePayload')}</h2>
                <div className="w-72">
                  <Select
                    value={chosenEvent.code}
                    onValueChange={setEvent}
                    aria-label={t('webhooks.event')}
                    options={(events.data ?? []).map((e) => ({ value: e.code, label: e.code }))}
                  />
                </div>
              </div>
              <pre
                dir="ltr"
                data-testid="sample-payload"
                className="max-h-80 overflow-auto rounded-md bg-muted p-3 text-start text-xs"
              >
                {JSON.stringify(chosenEvent.samplePayload, null, 2)}
              </pre>
            </div>
          )}
          <h2 className="text-base font-semibold">{t('portal.signature')}</h2>
          <ul className="list-disc ps-6 text-sm">
            <li>{t('portal.signatureHeaders')}</li>
            <li>{t('portal.signatureRetry')}</li>
          </ul>
          <pre dir="ltr" className="overflow-auto rounded-md bg-muted p-3 text-start text-xs">
            {verifySample}
          </pre>
        </TabsContent>

        <TabsContent value="powerbi" className="flex flex-col gap-4 pt-4" data-testid="power-bi-guide">
          <p className="text-sm">{t('portal.powerbi.intro')}</p>
          <ol className="flex list-decimal flex-col gap-2 ps-6 text-sm">
            {POWER_BI_STEPS.map((step) => (
              <li key={step}>{t(`portal.powerbi.steps.${step}`, { url: firstFeed?.oDataUrl ?? '' })}</li>
            ))}
          </ol>
          <h2 className="text-base font-semibold">{t('portal.powerbi.advanced')}</h2>
          <pre dir="ltr" className="overflow-auto rounded-md bg-muted p-3 text-start text-xs">
            {powerQuerySample
              .replace('{url}', firstFeed?.oDataUrl ?? '')
              .replace('{lang}', i18n.language === 'ar' ? 'ar' : 'en')}
          </pre>
          <p className="text-sm text-muted-foreground">{t('portal.powerbi.other')}</p>
        </TabsContent>

        <TabsContent value="api" className="flex flex-col gap-3 pt-4">
          <p className="text-sm text-muted-foreground">{t('portal.apiIntro')}</p>
          <CopyField value={data?.openApiUrl ?? ''} label="OpenAPI" />
          <CopyField value={data?.tokenUrl ?? ''} label={t('portal.tokenUrl')} />
          <ul className="list-disc ps-6 text-sm">
            <li>{t('portal.auth.apiKey')}</li>
            <li>{t('portal.auth.basic')}</li>
            <li>{t('portal.auth.oauth')}</li>
            <li>{t('portal.auth.limits')}</li>
          </ul>
          <a
            className="text-sm text-primary hover:underline"
            href="/openapi"
            target="_blank"
            rel="noreferrer"
          >
            {t('portal.openOpenApi')}
          </a>
        </TabsContent>
      </Tabs>
    </div>
  );
}
