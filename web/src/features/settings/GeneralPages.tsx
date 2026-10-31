import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { accessKey, useCan } from '@/features/access/session';
import { UnitSelect, useSystems } from '@/features/access/pickers';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  FileDrop,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { useServerErrors } from './common';
import { publicSettingsKey } from './publicSettings';

type Regional = components['schemas']['RegionalSettings'];
type Branding = components['schemas']['BrandingSettings'];

const timeZones = [
  'Asia/Dubai',
  'Asia/Riyadh',
  'Asia/Qatar',
  'Asia/Kuwait',
  'Asia/Bahrain',
  'Asia/Muscat',
  'Africa/Cairo',
  'Europe/London',
  'UTC',
];
const dateFormats = ['dd/MM/yyyy', 'yyyy-MM-dd', 'MM/dd/yyyy'];

/** Settings → Regional: time zone, Hijri display (off by default), Arabic-Indic digits, currency, date format. */
export function RegionalPage() {
  const { t } = useTranslation('settings');
  const { i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const current = useQuery({
    queryKey: ['settings', 'regional'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/regional', { signal })),
  });
  const [draft, setDraft] = useState<Regional>();
  useEffect(() => setDraft(current.data?.value ?? undefined), [current.data]);
  const save = useMutation({
    mutationFn: async (value: Regional) =>
      unwrap(
        await api.PUT('/api/v1/settings/regional', { body: { value, version: current.data?.version ?? 0 } }),
      ),
    onSuccess: (saved) => {
      errors.clear();
      queryClient.setQueryData(['settings', 'regional'], saved);
      void queryClient.invalidateQueries({ queryKey: publicSettingsKey });
      toast({ title: t('saved') });
    },
    onError: errors.fail,
  });
  if (!draft) return <Skeleton className="h-96" />;
  const sample = new Date(Date.UTC(2026, 2, 21, 9, 30));
  const preview = {
    arabicIndicDigits: draft.arabicIndicDigits,
    hijri: draft.hijriDisplay,
    timeZone: draft.timeZone,
  };
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.regional.title')} description={t('pages.regional.description')} />
      <form
        className="flex max-w-2xl flex-col gap-5 rounded-lg border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(draft);
        }}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('regional.timeZone')} error={errors.errorFor('timeZone')}>
            <Select
              value={draft.timeZone}
              onValueChange={(timeZone) => setDraft({ ...draft, timeZone })}
              options={[...new Set([draft.timeZone, ...timeZones])].map((zone) => ({
                value: zone,
                label: zone,
              }))}
            />
          </FormField>
          <FormField
            label={t('regional.currencyCode')}
            error={errors.errorFor('currencyCode')}
            hint={t('regional.currencyHint')}
          >
            <Input
              dir="ltr"
              maxLength={3}
              value={draft.currencyCode}
              onChange={(e) => setDraft({ ...draft, currencyCode: e.target.value.toUpperCase() })}
            />
          </FormField>
          <FormField label={t('regional.dateFormat')} error={errors.errorFor('dateFormat')}>
            <Select
              value={draft.dateFormat}
              onValueChange={(dateFormat) => setDraft({ ...draft, dateFormat })}
              options={dateFormats.map((format) => ({ value: format, label: format }))}
            />
          </FormField>
        </div>
        <Switch
          checked={draft.hijriDisplay}
          onCheckedChange={(hijriDisplay) => setDraft({ ...draft, hijriDisplay })}
          label={t('regional.hijriDisplay')}
          description={t('regional.hijriHint')}
        />
        <Switch
          checked={draft.arabicIndicDigits}
          onCheckedChange={(arabicIndicDigits) => setDraft({ ...draft, arabicIndicDigits })}
          label={t('regional.arabicIndicDigits')}
          description={t('regional.arabicIndicDigitsHint')}
        />
        <div className="rounded-md bg-muted p-3 text-sm" aria-live="polite">
          <span className="font-medium">{t('regional.preview')}: </span>
          {formatDate(sample, i18n.language, preview)} · {formatNumber(1234567.5, i18n.language, preview)}{' '}
          {draft.currencyCode}
        </div>
        <div>
          <Button type="submit" loading={save.isPending}>
            {t('save')}
          </Button>
        </div>
      </form>
    </div>
  );
}

/** Settings → Branding: organisation name, colour, logos and the e-mail header / footer. */
export function BrandingPage() {
  const { t } = useTranslation('settings');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const [stamp, setStamp] = useState(() => Date.now());
  const current = useQuery({
    queryKey: ['settings', 'branding'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/branding', { signal })),
  });
  const publicSettings = useQuery({
    queryKey: publicSettingsKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/public', { signal })),
  });
  const [draft, setDraft] = useState<Branding>();
  useEffect(() => setDraft(current.data?.value ?? undefined), [current.data]);
  const save = useMutation({
    mutationFn: async (value: Branding) =>
      unwrap(
        await api.PUT('/api/v1/settings/branding', { body: { value, version: current.data?.version ?? 0 } }),
      ),
    onSuccess: (saved) => {
      errors.clear();
      queryClient.setQueryData(['settings', 'branding'], saved);
      void queryClient.invalidateQueries({ queryKey: publicSettingsKey });
      toast({ title: t('saved') });
    },
    onError: errors.fail,
  });
  const logo = useMutation({
    mutationFn: async ({ kind, file }: { kind: 'logo-light' | 'logo-dark'; file: File | undefined }) => {
      if (!file) {
        unwrap(await api.DELETE('/api/v1/settings/branding/logo/{kind}', { params: { path: { kind } } }));
        return;
      }
      const body = new FormData();
      body.append('file', file);
      unwrap(
        await api.PUT('/api/v1/settings/branding/logo/{kind}', {
          params: { path: { kind } },
          // The generated type describes the form fields; the browser sends the FormData as multipart.
          body: body as unknown as { file: string },
          bodySerializer: (value: unknown) => value as FormData,
        }),
      );
    },
    onSuccess: () => {
      setStamp(Date.now());
      void queryClient.invalidateQueries({ queryKey: publicSettingsKey });
      toast({ title: t('saved') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  if (!draft) return <Skeleton className="h-96" />;
  const text = (
    key:
      | 'organisationNameAr'
      | 'organisationNameEn'
      | 'emailHeaderAr'
      | 'emailHeaderEn'
      | 'emailFooterAr'
      | 'emailFooterEn',
    multiline = false,
  ) => (
    <FormField label={t(`branding.${key}`)} error={errors.errorFor(key)}>
      {multiline ? (
        <Textarea
          dir={key.endsWith('Ar') ? 'rtl' : 'ltr'}
          rows={2}
          value={draft[key] ?? ''}
          onChange={(e) => setDraft({ ...draft, [key]: e.target.value || null })}
        />
      ) : (
        <Input
          dir={key.endsWith('Ar') ? 'rtl' : 'ltr'}
          value={draft[key] ?? ''}
          onChange={(e) => setDraft({ ...draft, [key]: e.target.value || null })}
        />
      )}
    </FormField>
  );
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.branding.title')} description={t('pages.branding.description')} />
      <form
        className="flex flex-col gap-5 rounded-lg border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(draft);
        }}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          {text('organisationNameAr')}
          {text('organisationNameEn')}
          <FormField
            label={t('branding.primaryColor')}
            error={errors.errorFor('primaryColor')}
            hint={t('branding.primaryColorHint')}
          >
            <Input
              dir="ltr"
              value={draft.primaryColor}
              onChange={(e) => setDraft({ ...draft, primaryColor: e.target.value })}
            />
          </FormField>
          <div className="flex items-end gap-3">
            <span
              className="size-10 rounded-md border"
              style={{ background: draft.primaryColor }}
              aria-hidden
            />
            <input
              type="color"
              aria-label={t('branding.primaryColor')}
              value={/^#[0-9a-f]{6}$/iu.test(draft.primaryColor) ? draft.primaryColor : '#0f766e'}
              onChange={(e) => setDraft({ ...draft, primaryColor: e.target.value })}
              className="h-10 w-16 cursor-pointer rounded-md border bg-card"
            />
          </div>
          {text('emailHeaderAr', true)}
          {text('emailHeaderEn', true)}
          {text('emailFooterAr', true)}
          {text('emailFooterEn', true)}
        </div>
        <div>
          <Button type="submit" loading={save.isPending}>
            {t('save')}
          </Button>
        </div>
      </form>
      <section className="grid gap-4 sm:grid-cols-2">
        {(['logo-light', 'logo-dark'] as const).map((kind) => {
          const has =
            kind === 'logo-light' ? publicSettings.data?.hasLogoLight : publicSettings.data?.hasLogoDark;
          return (
            <div key={kind} className="flex flex-col gap-3 rounded-lg border bg-card p-5">
              <h2 className="font-semibold">
                {t(`branding.${kind === 'logo-light' ? 'logoLight' : 'logoDark'}`)}
              </h2>
              <div
                className={kind === 'logo-dark' ? 'rounded-md bg-slate-900 p-4' : 'rounded-md bg-white p-4'}
              >
                {has ? (
                  <img
                    src={`/api/v1/settings/branding/logo/${kind}?t=${stamp}`}
                    alt={t('branding.logoPreview')}
                    className="h-16 object-contain"
                  />
                ) : (
                  <p className="text-sm text-slate-500">{t('branding.noLogo')}</p>
                )}
              </div>
              <FileDrop
                accept="image/png,image/jpeg,image/webp"
                label={t('branding.upload')}
                hint={t('branding.logoHint')}
                file={null}
                onFile={(file) => file && logo.mutate({ kind, file })}
              />
              {has && (
                <div>
                  <Button variant="outline" size="sm" onClick={() => logo.mutate({ kind, file: undefined })}>
                    {t('branding.remove')}
                  </Button>
                </div>
              )}
            </div>
          );
        })}
      </section>
    </div>
  );
}

/** Settings → Systems: switch modules on / off and set each system's owning unit (SPEC §0.2 system ownership). */
export function SystemsPage() {
  const { t } = useTranslation('settings');
  const { t: tc, i18n } = useTranslation();
  const { t: ta } = useTranslation('access');
  const canManage = useCan('access.system.manage');
  const queryClient = useQueryClient();
  const toast = useToast();
  const systems = useSystems();
  const update = useMutation({
    mutationFn: async (input: {
      code: string;
      enabled: boolean;
      owningUnitId: string | undefined;
      version: number;
    }) =>
      unwrap(
        await api.PUT('/api/v1/access/systems/{code}', {
          params: { path: { code: input.code } },
          body: { enabled: input.enabled, owningUnitId: input.owningUnitId ?? null, version: input.version },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      void queryClient.invalidateQueries({ queryKey: accessKey });
      toast({ title: t('saved') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.systems.title')} description={t('pages.systems.description')} />
      {systems.isError && <StatusBanner tone="danger" title={errorMessage(tc, systems.error)} />}
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('systems.system')}</TableHeaderCell>
            <TableHeaderCell>{t('systems.kind')}</TableHeaderCell>
            <TableHeaderCell>{t('systems.enabled')}</TableHeaderCell>
            <TableHeaderCell>{t('systems.owningUnit')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {systems.data?.map((system) => (
            <TableRow key={system.code}>
              <TableCell>
                <span className="font-medium">{pickText(i18n.language, system.nameAr, system.nameEn)}</span>
                <span className="block text-xs text-muted-foreground" dir="ltr">
                  {system.code}
                </span>
              </TableCell>
              <TableCell>
                <StatusChip tone={system.kind === 'platform' ? 'neutral' : 'info'}>
                  {t(`systems.kinds.${system.kind === 'platform' ? 'platform' : 'module'}`)}
                </StatusChip>
              </TableCell>
              <TableCell>
                <Switch
                  label={
                    <span className="sr-only">
                      {t('systems.enabledFor', {
                        name: pickText(i18n.language, system.nameAr, system.nameEn),
                      })}
                    </span>
                  }
                  description={system.kind === 'platform' ? t('systems.platformAlwaysOn') : undefined}
                  checked={system.enabled}
                  disabled={!canManage || system.kind === 'platform' || update.isPending}
                  onCheckedChange={(enabled) =>
                    update.mutate({
                      code: system.code,
                      enabled,
                      owningUnitId: system.owningUnitId ?? undefined,
                      version: system.version,
                    })
                  }
                />
              </TableCell>
              <TableCell className="min-w-56">
                {canManage ? (
                  <UnitSelect
                    allowEmpty
                    value={system.owningUnitId ?? undefined}
                    onChange={(owningUnitId) =>
                      update.mutate({
                        code: system.code,
                        enabled: system.enabled,
                        owningUnitId,
                        version: system.version,
                      })
                    }
                  />
                ) : (
                  pickText(i18n.language, system.owningUnitNameAr, system.owningUnitNameEn) ||
                  ta('pickers.noUnit')
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

/** Settings → Features: switchable features declared by each system. */
export function FeaturesPage() {
  const { t } = useTranslation('settings');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const features = useQuery({
    queryKey: ['settings', 'features'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/features', { signal })),
  });
  const set = useMutation({
    mutationFn: async (input: { code: string; enabled: boolean; version: number }) =>
      unwrap(
        await api.PUT('/api/v1/settings/features/{code}', {
          params: { path: { code: input.code } },
          body: { enabled: input.enabled, version: input.version },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['settings', 'features'] });
      toast({ title: t('saved') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.features.title')} description={t('pages.features.description')} />
      <div className="flex max-w-2xl flex-col divide-y rounded-lg border bg-card">
        {features.data?.map((feature) => (
          <div key={feature.code} className="p-4">
            <Switch
              checked={feature.enabled}
              onCheckedChange={(enabled) =>
                set.mutate({ code: feature.code, enabled, version: feature.version })
              }
              label={pickText(i18n.language, feature.nameAr, feature.nameEn)}
              description={<span dir="ltr">{feature.code}</span>}
              disabled={set.isPending}
            />
          </div>
        ))}
      </div>
    </div>
  );
}
