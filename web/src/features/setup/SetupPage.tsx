import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { CheckCircle2 } from 'lucide-react';
import { useState } from 'react';
import { Controller, useForm, type FieldPath } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateFieldMessage, translateMessageKey } from '@/api/problem';
import { PasswordPolicyHint } from '@/features/auth/PasswordPolicyHint';
import { dateFormats, timeZones } from '@/features/settings/regionalOptions';
import {
  Button,
  FormField,
  Input,
  Select,
  StatusBanner,
  Switch,
  Textarea,
  Wizard,
  type WizardStep,
} from '@/ui';
import { bootstrapSetup, setupStatusKey, verifySetupToken } from './api';

const text = (max: number) => z.string().trim().max(max);
const required = (max: number) => z.string().trim().min(1, 'validation.notEmpty').max(max);

const schema = z
  .object({
    token: required(128),
    organisationNameAr: required(200),
    organisationNameEn: required(200),
    organisationCode: required(20).regex(/^[A-Za-z0-9-]+$/, 'validation.codeFormat'),
    timeZone: required(100),
    currencyCode: required(3).regex(/^[A-Za-z]{3}$/, 'validation.unknownCode'),
    dateFormat: required(20),
    primaryColor: required(7).regex(/^#[0-9a-fA-F]{6}$/, 'settings.errors.colour'),
    emailHeaderAr: text(500),
    emailHeaderEn: text(500),
    emailFooterAr: text(500),
    emailFooterEn: text(500),
    defaultLanguage: z.enum(['ar', 'en']),
    hijriDisplay: z.boolean(),
    arabicIndicDigits: z.boolean(),
    adminNameAr: required(200),
    adminNameEn: required(200),
    adminEmployeeNo: required(50),
    adminJobTitleAr: text(200),
    adminJobTitleEn: text(200),
    adminEmail: required(256).refine((value) => z.email().safeParse(value).success, 'validation.email'),
    adminPassword: z.string().min(1, 'validation.notEmpty').max(1024),
    confirmPassword: z.string().max(1024),
  })
  .refine((v) => v.adminPassword === v.confirmPassword, {
    path: ['confirmPassword'],
    message: 'setup.administrator.mismatch',
  });

export type SetupFormValues = z.infer<typeof schema>;

const stepFields: Record<string, FieldPath<SetupFormValues>[]> = {
  token: ['token'],
  organisation: [
    'organisationNameAr',
    'organisationNameEn',
    'organisationCode',
    'timeZone',
    'currencyCode',
    'dateFormat',
  ],
  branding: ['primaryColor', 'emailHeaderAr', 'emailHeaderEn', 'emailFooterAr', 'emailFooterEn'],
  languages: ['defaultLanguage', 'hijriDisplay', 'arabicIndicDigits'],
  administrator: [
    'adminNameAr',
    'adminNameEn',
    'adminEmployeeNo',
    'adminJobTitleAr',
    'adminJobTitleEn',
    'adminEmail',
    'adminPassword',
    'confirmPassword',
  ],
};

/** Field name → label key, for server errors shown in the summary. */
const fieldLabels: Partial<Record<FieldPath<SetupFormValues>, string>> = {
  organisationNameAr: 'organisation.nameAr',
  organisationNameEn: 'organisation.nameEn',
  organisationCode: 'organisation.code',
  timeZone: 'organisation.timeZone',
  currencyCode: 'organisation.currencyCode',
  dateFormat: 'organisation.dateFormat',
  primaryColor: 'branding.primaryColor',
  defaultLanguage: 'languages.defaultLanguage',
  adminNameAr: 'administrator.nameAr',
  adminNameEn: 'administrator.nameEn',
  adminEmployeeNo: 'administrator.employeeNo',
  adminEmail: 'administrator.email',
  adminPassword: 'administrator.password',
};

/**
 * First-run setup, stage 1 (ADR 0150): only while no administrator exists, opened with the one-time setup token from the log.
 * Organisation profile, branding, languages and the first administrator are saved in one call at the end.
 */
export function SetupPage() {
  const { t, i18n } = useTranslation('setup');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const [created, setCreated] = useState<string>();
  const [serverErrors, setServerErrors] = useState<{ field: string; message: string }[]>([]);
  const [busy, setBusy] = useState(false);
  const policy = useQuery({
    queryKey: ['identity', 'password-policy'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/auth/password-policy', { signal })),
  });
  const form = useForm<SetupFormValues>({
    resolver: zodResolver(schema),
    mode: 'onTouched',
    defaultValues: {
      token: '',
      organisationNameAr: '',
      organisationNameEn: '',
      organisationCode: '',
      timeZone: 'Asia/Dubai',
      currencyCode: 'AED',
      dateFormat: 'dd/MM/yyyy',
      primaryColor: '#0f766e',
      emailHeaderAr: '',
      emailHeaderEn: '',
      emailFooterAr: '',
      emailFooterEn: '',
      defaultLanguage: i18n.language === 'en' ? 'en' : 'ar',
      hijriDisplay: false,
      arabicIndicDigits: false,
      adminNameAr: '',
      adminNameEn: '',
      adminEmployeeNo: '',
      adminJobTitleAr: '',
      adminJobTitleEn: '',
      adminEmail: '',
      adminPassword: '',
      confirmPassword: '',
    },
  });
  const { errors } = form.formState;
  const fieldError = (name: FieldPath<SetupFormValues>) => translateFieldMessage(tc, errors[name]?.message);
  const validateStep = (key: string) => () => form.trigger(stepFields[key]);

  if (created) {
    return (
      <div className="flex flex-col items-center gap-4 text-center" data-testid="setup-done">
        <CheckCircle2 className="size-12 text-success" aria-hidden />
        <h1 className="text-2xl font-semibold">{t('done.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('done.description', { email: created })}</p>
        <Button asChild>
          <Link to="/login" search={{ redirect: '/setup/configure' }}>
            {t('done.signIn')}
          </Link>
        </Button>
      </div>
    );
  }

  const steps: WizardStep[] = [
    {
      key: 'token',
      title: t('steps.token'),
      validate: async () => {
        if (!(await form.trigger('token'))) return false;
        try {
          if (await verifySetupToken(form.getValues('token'))) return true;
          form.setError('token', { message: 'setup.token.invalid' });
        } catch (failure) {
          form.setError('token', {
            message: failure instanceof ApiError ? failure.problem.messageKey : 'errors.network',
          });
        }
        return false;
      },
      content: (
        <div className="flex flex-col gap-4">
          <p className="text-sm">{t('token.intro')}</p>
          <p className="text-sm text-muted-foreground">{t('token.where')}</p>
          <FormField label={t('token.label')} error={fieldError('token')} required>
            <Input
              dir="ltr"
              autoComplete="off"
              spellCheck={false}
              className="font-mono"
              data-testid="setup-token"
              {...form.register('token')}
            />
          </FormField>
        </div>
      ),
    },
    {
      key: 'organisation',
      title: t('steps.organisation'),
      validate: validateStep('organisation'),
      content: (
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('organisation.nameAr')} error={fieldError('organisationNameAr')} required>
            <Input dir="rtl" lang="ar" {...form.register('organisationNameAr')} />
          </FormField>
          <FormField label={t('organisation.nameEn')} error={fieldError('organisationNameEn')} required>
            <Input dir="ltr" lang="en" {...form.register('organisationNameEn')} />
          </FormField>
          <FormField
            label={t('organisation.code')}
            error={fieldError('organisationCode')}
            hint={t('organisation.codeHint')}
            required
          >
            <Input dir="ltr" maxLength={20} {...form.register('organisationCode')} />
          </FormField>
          <Controller
            control={form.control}
            name="timeZone"
            render={({ field }) => (
              <FormField label={t('organisation.timeZone')} error={fieldError('timeZone')}>
                <Select
                  value={field.value}
                  onValueChange={field.onChange}
                  options={timeZones.map((zone) => ({ value: zone, label: zone }))}
                />
              </FormField>
            )}
          />
          <FormField label={t('organisation.currencyCode')} error={fieldError('currencyCode')}>
            <Input dir="ltr" maxLength={3} {...form.register('currencyCode')} />
          </FormField>
          <Controller
            control={form.control}
            name="dateFormat"
            render={({ field }) => (
              <FormField label={t('organisation.dateFormat')} error={fieldError('dateFormat')}>
                <Select
                  value={field.value}
                  onValueChange={field.onChange}
                  options={dateFormats.map((format) => ({ value: format, label: format }))}
                />
              </FormField>
            )}
          />
        </div>
      ),
    },
    {
      key: 'branding',
      title: t('steps.branding'),
      validate: validateStep('branding'),
      content: (
        <div className="flex flex-col gap-4">
          <FormField label={t('branding.primaryColor')} error={fieldError('primaryColor')}>
            <Input type="color" className="h-10 w-24 p-1" {...form.register('primaryColor')} />
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('branding.emailHeaderAr')} error={fieldError('emailHeaderAr')}>
              <Textarea dir="rtl" lang="ar" rows={2} {...form.register('emailHeaderAr')} />
            </FormField>
            <FormField label={t('branding.emailHeaderEn')} error={fieldError('emailHeaderEn')}>
              <Textarea dir="ltr" lang="en" rows={2} {...form.register('emailHeaderEn')} />
            </FormField>
            <FormField label={t('branding.emailFooterAr')} error={fieldError('emailFooterAr')}>
              <Textarea dir="rtl" lang="ar" rows={2} {...form.register('emailFooterAr')} />
            </FormField>
            <FormField label={t('branding.emailFooterEn')} error={fieldError('emailFooterEn')}>
              <Textarea dir="ltr" lang="en" rows={2} {...form.register('emailFooterEn')} />
            </FormField>
          </div>
          <p className="text-xs text-muted-foreground">{t('branding.logoLater')}</p>
        </div>
      ),
    },
    {
      key: 'languages',
      title: t('steps.languages'),
      validate: validateStep('languages'),
      content: (
        <div className="flex flex-col gap-4">
          <Controller
            control={form.control}
            name="defaultLanguage"
            render={({ field }) => (
              <FormField
                label={t('languages.defaultLanguage')}
                hint={t('languages.defaultLanguageHint')}
                error={fieldError('defaultLanguage')}
              >
                <Select
                  value={field.value}
                  onValueChange={field.onChange}
                  options={[
                    { value: 'ar', label: 'العربية' },
                    { value: 'en', label: 'English' },
                  ]}
                />
              </FormField>
            )}
          />
          <Controller
            control={form.control}
            name="hijriDisplay"
            render={({ field }) => (
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                label={t('languages.hijri')}
                description={t('languages.hijriHint')}
              />
            )}
          />
          <Controller
            control={form.control}
            name="arabicIndicDigits"
            render={({ field }) => (
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                label={t('languages.digits')}
                description={t('languages.digitsHint')}
              />
            )}
          />
        </div>
      ),
    },
    {
      key: 'administrator',
      title: t('steps.administrator'),
      validate: validateStep('administrator'),
      content: (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">{t('administrator.intro')}</p>
          {serverErrors.length > 0 && (
            <StatusBanner
              tone="danger"
              title={t('fieldErrors')}
              description={
                <ul className="list-disc ps-5" data-testid="setup-server-errors">
                  {serverErrors.map((error) => (
                    <li key={`${error.field}-${error.message}`}>{error.message}</li>
                  ))}
                </ul>
              }
            />
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('administrator.nameAr')} error={fieldError('adminNameAr')} required>
              <Input dir="rtl" lang="ar" {...form.register('adminNameAr')} />
            </FormField>
            <FormField label={t('administrator.nameEn')} error={fieldError('adminNameEn')} required>
              <Input dir="ltr" lang="en" {...form.register('adminNameEn')} />
            </FormField>
            <FormField label={t('administrator.employeeNo')} error={fieldError('adminEmployeeNo')} required>
              <Input dir="ltr" {...form.register('adminEmployeeNo')} />
            </FormField>
            <FormField label={t('administrator.email')} error={fieldError('adminEmail')} required>
              <Input type="email" dir="ltr" autoComplete="username" {...form.register('adminEmail')} />
            </FormField>
            <FormField label={t('administrator.jobTitleAr')} error={fieldError('adminJobTitleAr')}>
              <Input dir="rtl" lang="ar" {...form.register('adminJobTitleAr')} />
            </FormField>
            <FormField label={t('administrator.jobTitleEn')} error={fieldError('adminJobTitleEn')}>
              <Input dir="ltr" lang="en" {...form.register('adminJobTitleEn')} />
            </FormField>
            <FormField label={t('administrator.password')} error={fieldError('adminPassword')} required>
              <Input
                type="password"
                dir="ltr"
                autoComplete="new-password"
                {...form.register('adminPassword')}
              />
            </FormField>
            <FormField
              label={t('administrator.confirmPassword')}
              error={fieldError('confirmPassword')}
              required
            >
              <Input
                type="password"
                dir="ltr"
                autoComplete="new-password"
                {...form.register('confirmPassword')}
              />
            </FormField>
          </div>
          {policy.data && <PasswordPolicyHint policy={policy.data} />}
        </div>
      ),
    },
  ];

  async function finish() {
    setServerErrors([]);
    const values = form.getValues();
    setBusy(true);
    try {
      const result = await bootstrapSetup(values.token, {
        organisationNameAr: values.organisationNameAr,
        organisationNameEn: values.organisationNameEn,
        organisationCode: values.organisationCode,
        timeZone: values.timeZone,
        currencyCode: values.currencyCode,
        dateFormat: values.dateFormat,
        primaryColor: values.primaryColor,
        emailHeaderAr: values.emailHeaderAr || null,
        emailHeaderEn: values.emailHeaderEn || null,
        emailFooterAr: values.emailFooterAr || null,
        emailFooterEn: values.emailFooterEn || null,
        defaultLanguage: values.defaultLanguage,
        hijriDisplay: values.hijriDisplay,
        arabicIndicDigits: values.arabicIndicDigits,
        adminEmail: values.adminEmail,
        adminPassword: values.adminPassword,
        adminNameAr: values.adminNameAr,
        adminNameEn: values.adminNameEn,
        adminEmployeeNo: values.adminEmployeeNo,
        adminJobTitleAr: values.adminJobTitleAr || null,
        adminJobTitleEn: values.adminJobTitleEn || null,
      });
      void queryClient.invalidateQueries({ queryKey: setupStatusKey });
      setCreated(result.userName);
    } catch (failure) {
      if (failure instanceof ApiError && failure.problem.errors?.length) {
        setServerErrors(
          failure.problem.errors.map((error) => {
            const label = fieldLabels[error.field as FieldPath<SetupFormValues>];
            const message = translateMessageKey(tc, error.messageKey, error.params ?? undefined);
            return {
              field: error.field,
              message: label ? `${translateMessageKey(tc, `setup.${label}`)}: ${message}` : message,
            };
          }),
        );
      } else {
        setServerErrors([{ field: 'root', message: errorMessage(tc, failure) }]);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <p className="text-sm text-muted-foreground">{t('description')}</p>
      </div>
      <Wizard steps={steps} onFinish={finish} busy={busy} finishLabel={t('administrator.create')} />
    </div>
  );
}
