import { useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Textarea,
  useToast,
} from '@/ui';
import {
  SCHEDULE_KINDS,
  useMappingPacks,
  useSyncMutation,
  useSyncSchedule,
  type SyncConnection,
  type SyncSchedule,
  type SyncScheduleView,
} from './api';
import { Code, Fact, SalesforceErrorDetails, ServerMessage, useMessage, useSyncT } from './shared';

interface TabProps {
  connection: SyncConnection;
  canManage: boolean;
}

const LOGIN_URLS = ['https://login.salesforce.com', 'https://test.salesforce.com'];

/** Login URL, consumer key, integration user, API version, limits, mapping pack; private key (never echoed); Test connection. */
export function ConnectionTab({ connection, canManage }: TabProps) {
  const { t, language } = useSyncT();
  const { t: tc } = useTranslation();
  const message = useMessage();
  const toast = useToast();
  const packs = useMappingPacks();
  const [form, setForm] = useState(() => ({
    loginUrl: connection.loginUrl,
    clientId: connection.clientId ?? '',
    username: connection.username ?? '',
    apiVersion: connection.apiVersion,
    bulkThreshold: String(connection.bulkThreshold),
    pageSize: String(connection.pageSize),
    mappingPackId: connection.mappingPackId ?? '-',
  }));
  const [pem, setPem] = useState('');
  const set = (key: keyof typeof form) => (e: ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const save = useSyncMutation(async () =>
    unwrap(
      await api.PUT('/api/v1/integration/sync/connections/{id}', {
        params: { path: { id: connection.id } },
        body: {
          loginUrl: form.loginUrl.trim(),
          clientId: form.clientId.trim() || null,
          username: form.username.trim() || null,
          apiVersion: form.apiVersion.trim() || null,
          bulkThreshold: Number(form.bulkThreshold) || null,
          pageSize: Number(form.pageSize) || null,
          mappingPackId: form.mappingPackId === '-' ? null : form.mappingPackId,
          version: connection.version,
        },
      }),
    ),
  );
  const storeKey = useSyncMutation(async (privateKeyPem: string) =>
    unwrap(
      await api.PUT('/api/v1/integration/sync/connections/{id}/key', {
        params: { path: { id: connection.id } },
        body: { privateKeyPem },
      }),
    ),
  );
  const test = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/test', {
        params: { path: { id: connection.id } },
      }),
    ),
  );
  const readFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) setPem(await file.text());
    e.target.value = '';
  };
  const last = connection.lastTest;
  return (
    <div className="flex flex-col gap-6 pt-4">
      <section
        className="flex flex-col gap-4 rounded-lg border bg-card p-4"
        aria-labelledby="sync-connection-title"
      >
        <h2 id="sync-connection-title" className="text-base font-semibold">
          {t('sync.connection.title')}
        </h2>
        <div className="grid gap-4 md:grid-cols-2">
          <FormField label={t('sync.connection.loginUrl')} hint={t('sync.connection.loginUrlHint')} required>
            <Input
              dir="ltr"
              name="loginUrl"
              list="sync-login-urls"
              value={form.loginUrl}
              onChange={set('loginUrl')}
              disabled={!canManage}
            />
          </FormField>
          <datalist id="sync-login-urls">
            {LOGIN_URLS.map((url) => (
              <option key={url} value={url} />
            ))}
          </datalist>
          <FormField label={t('sync.connection.clientId')} hint={t('sync.connection.clientIdHint')}>
            <Input
              dir="ltr"
              name="clientId"
              value={form.clientId}
              onChange={set('clientId')}
              disabled={!canManage}
            />
          </FormField>
          <FormField label={t('sync.connection.username')} hint={t('sync.connection.usernameHint')}>
            <Input
              dir="ltr"
              name="username"
              value={form.username}
              onChange={set('username')}
              disabled={!canManage}
            />
          </FormField>
          <FormField label={t('sync.connection.apiVersion')}>
            <Input
              dir="ltr"
              name="apiVersion"
              value={form.apiVersion}
              onChange={set('apiVersion')}
              disabled={!canManage}
            />
          </FormField>
          <FormField label={t('sync.connection.bulkThreshold')} hint={t('sync.connection.bulkThresholdHint')}>
            <Input
              type="number"
              min={1}
              name="bulkThreshold"
              value={form.bulkThreshold}
              onChange={set('bulkThreshold')}
              disabled={!canManage}
            />
          </FormField>
          <FormField label={t('sync.connection.pageSize')}>
            <Input
              type="number"
              min={200}
              max={2000}
              name="pageSize"
              value={form.pageSize}
              onChange={set('pageSize')}
              disabled={!canManage}
            />
          </FormField>
          <FormField label={t('sync.connection.pack')}>
            <Select
              value={form.mappingPackId}
              onValueChange={(mappingPackId) => setForm((f) => ({ ...f, mappingPackId }))}
              disabled={!canManage}
              options={[
                { value: '-', label: t('sync.connection.noPack') },
                ...(packs.data ?? []).map((p) => ({
                  value: p.id,
                  label: `${pickText(language, p.nameAr, p.nameEn) || p.code} (v${p.packVersion})`,
                })),
              ]}
            />
          </FormField>
          {connection.instanceUrl && (
            <dl>
              <Fact label={t('sync.connection.instanceUrl')}>
                <Code>{connection.instanceUrl}</Code>
              </Fact>
            </dl>
          )}
        </div>
        {canManage && (
          <div>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(undefined, {
                  onSuccess: () => toast({ tone: 'success', title: tc('status.saved') }),
                  onError,
                })
              }
            >
              {tc('actions.save')}
            </Button>
          </div>
        )}
      </section>

      <section className="flex flex-col gap-4 rounded-lg border bg-card p-4" aria-labelledby="sync-key-title">
        <h2 id="sync-key-title" className="text-base font-semibold">
          {t('sync.connection.keyTitle')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('sync.connection.keyHint')}</p>
        <div className="flex flex-wrap items-center gap-2" data-testid="key-state">
          {connection.keyStored ? (
            <>
              <StatusChip tone="success">{t('sync.connection.keyStored')}</StatusChip>
              {connection.keyThumbprint && (
                <span className="text-xs text-muted-foreground">
                  {t('sync.connection.thumbprint')} <Code>{connection.keyThumbprint}</Code>
                </span>
              )}
            </>
          ) : (
            <StatusChip tone="warning">{t('sync.connection.noKey')}</StatusChip>
          )}
        </div>
        {canManage && (
          <>
            <FormField label={t('sync.connection.pem')} hint={t('sync.connection.pemHint')}>
              <Textarea
                dir="ltr"
                name="privateKeyPem"
                rows={5}
                autoComplete="off"
                spellCheck={false}
                className="font-mono text-xs"
                placeholder="-----BEGIN PRIVATE KEY-----"
                value={pem}
                onChange={(e) => setPem(e.target.value)}
              />
            </FormField>
            <div className="flex flex-wrap items-center gap-2">
              <label className="inline-flex cursor-pointer items-center">
                <span className="sr-only">{t('sync.connection.keyFile')}</span>
                <input
                  type="file"
                  accept=".pem,.key,.txt"
                  className="text-sm file:me-3 file:rounded-md file:border file:bg-card file:px-3 file:py-1.5"
                  onChange={(e) => void readFile(e)}
                  aria-label={t('sync.connection.keyFile')}
                />
              </label>
              <Button
                disabled={!pem.trim()}
                loading={storeKey.isPending}
                onClick={() =>
                  storeKey.mutate(pem, {
                    onSuccess: () => {
                      setPem('');
                      toast({ tone: 'success', title: t('sync.connection.keySaved') });
                    },
                    onError,
                  })
                }
              >
                {t('sync.connection.storeKey')}
              </Button>
            </div>
          </>
        )}
      </section>

      <section
        className="flex flex-col gap-3 rounded-lg border bg-card p-4"
        aria-labelledby="sync-test-title"
      >
        <h2 id="sync-test-title" className="text-base font-semibold">
          {t('sync.connection.testTitle')}
        </h2>
        {last && (
          <StatusBanner
            tone={last.ok ? (last.warnings.length > 0 ? 'warning' : 'success') : 'danger'}
            title={
              last.ok
                ? last.warnings.length > 0
                  ? t('sync.connection.testOkWarnings')
                  : t('sync.connection.testOk')
                : t('sync.connection.testFailed')
            }
            description={
              <>
                {formatDateTime(last.at, language)}
                {!last.ok && last.messageKey && ` — ${message(last.messageKey)}`}
              </>
            }
          />
        )}
        {last && !last.ok && last.error && <SalesforceErrorDetails error={last.error} />}
        {last?.ok && last.warnings.length > 0 && (
          <ul className="flex flex-col gap-2 text-sm" data-testid="sync-test-warnings">
            {last.warnings.map((warning, i) => (
              <li key={i}>
                <ServerMessage messageKey={warning.messageKey} params={warning.params} />
              </li>
            ))}
          </ul>
        )}
        {canManage && (
          <div>
            <Button
              variant="secondary"
              loading={test.isPending}
              onClick={() => test.mutate(undefined, { onError })}
            >
              {t('sync.connection.test')}
            </Button>
          </div>
        )}
      </section>
    </div>
  );
}

const DAYS = [1, 2, 3, 4, 5, 6, 7] as const;

/** Schedule (every N hours / daily / weekly, installation time zone), late threshold and the next runs. */
export function ScheduleTab({ connection, canManage }: TabProps) {
  const schedule = useSyncSchedule(connection.id);
  if (schedule.isPending) return <Skeleton className="mt-4 h-48 rounded-lg" />;
  if (!schedule.data) return null;
  return (
    <ScheduleForm
      key={connection.version}
      view={schedule.data}
      connection={connection}
      canManage={canManage}
    />
  );
}

function ScheduleForm({ view, connection, canManage }: TabProps & { view: SyncScheduleView }) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [value, setValue] = useState<Required<SyncSchedule>>({
    kind: view.schedule.kind ?? 'interval',
    everyHours: view.schedule.everyHours ?? 4,
    at: view.schedule.at ?? '00:00',
    days: view.schedule.days ?? [1, 2, 3, 4, 5],
    businessHoursOnly: view.schedule.businessHoursOnly ?? false,
  });
  const [late, setLate] = useState(view.lateAfterMinutes === null ? '' : String(view.lateAfterMinutes));
  const save = useSyncMutation(async () =>
    unwrap(
      await api.PUT('/api/v1/integration/sync/connections/{id}/schedule', {
        params: { path: { id: connection.id } },
        body: {
          schedule: value,
          lateAfterMinutes: late === '' ? null : Number(late),
          version: connection.version,
        },
      }),
    ),
  );
  const weekday = (day: number) =>
    new Intl.DateTimeFormat(language === 'ar' ? 'ar-AE' : 'en-GB', {
      weekday: 'long',
      timeZone: 'UTC',
    }).format(
      new Date(Date.UTC(2024, 0, day)), // 1 January 2024 was a Monday (ISO day 1)
    );
  return (
    <div className="flex flex-col gap-4 pt-4">
      <section className="flex flex-col gap-4 rounded-lg border bg-card p-4">
        <div className="grid gap-4 md:grid-cols-3">
          <FormField label={t('sync.schedule.kind')}>
            <Select
              value={value.kind}
              disabled={!canManage}
              onValueChange={(kind) => setValue((v) => ({ ...v, kind }))}
              options={SCHEDULE_KINDS.map((kind) => ({
                value: kind,
                label: tx(`sync.schedule.kinds.${kind}`),
              }))}
            />
          </FormField>
          {value.kind === 'interval' && (
            <FormField label={t('sync.schedule.everyHours')}>
              <Input
                type="number"
                min={1}
                max={24}
                name="everyHours"
                disabled={!canManage}
                value={value.everyHours}
                onChange={(e) => setValue((v) => ({ ...v, everyHours: Number(e.target.value) }))}
              />
            </FormField>
          )}
          <FormField
            label={value.kind === 'interval' ? t('sync.schedule.anchor') : t('sync.schedule.at')}
            hint={t('sync.schedule.timeZone', { zone: view.timeZone })}
          >
            <Input
              type="time"
              name="at"
              dir="ltr"
              disabled={!canManage}
              value={value.at}
              onChange={(e) => setValue((v) => ({ ...v, at: e.target.value }))}
            />
          </FormField>
        </div>
        {value.kind === 'weekly' && (
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-medium">{t('sync.schedule.days')}</legend>
            <div className="flex flex-wrap gap-4">
              {DAYS.map((day) => (
                <Checkbox
                  key={day}
                  label={weekday(day)}
                  disabled={!canManage}
                  checked={value.days.includes(day)}
                  onChange={(e) =>
                    setValue((v) => ({
                      ...v,
                      days: e.target.checked
                        ? [...v.days, day].sort((a, b) => a - b)
                        : v.days.filter((d) => d !== day),
                    }))
                  }
                />
              ))}
            </div>
          </fieldset>
        )}
        <Switch
          checked={value.businessHoursOnly}
          disabled={!canManage}
          onCheckedChange={(businessHoursOnly) => setValue((v) => ({ ...v, businessHoursOnly }))}
          label={t('sync.schedule.businessHours')}
          description={t('sync.schedule.businessHoursHint')}
        />
        <div className="max-w-xs">
          <FormField label={t('sync.schedule.lateAfter')} hint={t('sync.schedule.lateAfterHint')}>
            <Input
              type="number"
              min={5}
              name="lateAfterMinutes"
              disabled={!canManage}
              value={late}
              onChange={(e) => setLate(e.target.value)}
            />
          </FormField>
        </div>
        {canManage && (
          <div>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(undefined, {
                  onSuccess: () => toast({ tone: 'success', title: tc('status.saved') }),
                  onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                })
              }
            >
              {tc('actions.save')}
            </Button>
          </div>
        )}
      </section>
      <section className="flex flex-col gap-2 rounded-lg border bg-card p-4" aria-labelledby="sync-next-runs">
        <h2 id="sync-next-runs" className="text-base font-semibold">
          {t('sync.schedule.nextRuns')}
        </h2>
        {view.nextRuns.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('sync.schedule.noRuns')}</p>
        ) : (
          <ol className="flex list-decimal flex-col gap-1 ps-6 text-sm" data-testid="next-runs">
            {view.nextRuns.slice(0, 5).map((at) => (
              <li key={at}>{formatDateTime(at, language)}</li>
            ))}
          </ol>
        )}
        <p className="text-xs text-muted-foreground">{t('sync.schedule.previewHint')}</p>
      </section>
    </div>
  );
}
