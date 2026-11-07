import { Plus, Trash2, Upload } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { contentUrl } from '@/features/files/api';
import {
  Button,
  FileDrop,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
  Switch,
  useToast,
} from '@/ui';
import {
  usePortfolioSettings,
  useSavePortfolioSettings,
  useUploadTemplate,
  type PortfolioSettingsValue,
} from './api';
import { featureKeys, useFeature } from './featuresApi';
import {
  AgileSection,
  CostSection,
  FeaturesSection,
  GateChecklistSection,
  HealthSection,
  featureOn,
} from './views/SettingsSections';
import { TemplatesEditor } from './views/TemplatesEditor';
import { WaselSettingsSection } from './wasel/WaselSettingsSection';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';
import { ResponsibilitiesLink } from '@/features/access/ResponsibilitiesLink';

/** Settings → Portfolio (docs/design/portfolio.md §2.1): threshold, criteria and weights, bands, date windows, budget step, template. */
export function PortfolioSettingsPage() {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const { t: tv } = useTranslation('portfolioViews');
  const toast = useToast();
  const queryClient = useQueryClient();
  const templatesOn = useFeature('templates');
  const settings = usePortfolioSettings();
  const save = useSavePortfolioSettings();
  const upload = useUploadTemplate();
  const [value, setValue] = useState<PortfolioSettingsValue>();
  const [file, setFile] = useState<File | null>(null);
  useEffect(() => {
    if (settings.data?.value) setValue(settings.data.value);
  }, [settings.data]);
  if (settings.isPending || !value) return <Skeleton className="h-96 rounded-lg" />;
  if (settings.isError) return <StatusBanner tone="danger" title={errorMessage(tc, settings.error)} />;
  const criteria = value.priorityCriteria ?? [];
  const bands = value.priorityBands ?? [];
  const errors = save.error instanceof ApiError ? (save.error.problem.errors ?? []) : [];
  const update = (patch: Partial<PortfolioSettingsValue>) =>
    setValue((current) => (current ? { ...current, ...patch } : current));
  // Prioritisation off: the default criteria / bands apply and the editors are read-only (§32).
  const prioritisationOn = featureOn(value, 'prioritisation');

  return (
    <div className="flex flex-col gap-6" data-testid="portfolio-settings">
      <ResponsibilitiesLink system="portfolio" />
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <SystemSettingsLinks system="portfolio" />
      {errors.length > 0 && (
        <StatusBanner
          tone="danger"
          title={errorMessage(tc, save.error)}
          description={errors
            .map((e) => `${e.field}: ${translateMessageKey(tc, e.messageKey, e.params)}`)
            .join(' · ')}
        />
      )}
      <section className="grid gap-4 rounded-lg border bg-card p-6 md:grid-cols-2">
        <FormField label={t('settings.threshold')} hint={t('settings.thresholdHint')}>
          <Input
            dir="ltr"
            inputMode="decimal"
            value={String(value.thresholdAmount ?? '')}
            onChange={(e) => update({ thresholdAmount: Number(e.target.value) })}
          />
        </FormField>
        <FormField label={t('settings.budgetStepCode')}>
          <Input
            dir="ltr"
            value={value.budgetStepCode ?? ''}
            onChange={(e) => update({ budgetStepCode: e.target.value })}
          />
        </FormField>
        <FormField label={t('settings.startYearsAhead')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            value={String(value.startYearsAhead ?? '')}
            onChange={(e) => update({ startYearsAhead: Number(e.target.value) })}
          />
        </FormField>
        <FormField label={t('settings.endYearsAhead')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            value={String(value.endYearsAhead ?? '')}
            onChange={(e) => update({ endYearsAhead: Number(e.target.value) })}
          />
        </FormField>
        <div className="md:col-span-2">
          <Switch
            checked={value.assessmentOnCreate ?? true}
            onCheckedChange={(checked) => update({ assessmentOnCreate: checked })}
            label={t('settings.assessmentOnCreate')}
          />
        </div>
      </section>
      <section
        className="flex flex-col gap-4 rounded-lg border bg-card p-6"
        data-testid="portfolio-settings-lifecycle"
      >
        <h2 className="font-semibold">{t('settings.lifecycle')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.lifecycleHint')}</p>
        <div className="grid gap-6 md:grid-cols-2">
          {(['projectTemplate', 'initiativeTemplate'] as const).map((key) => {
            const template = value[key] ?? {
              charter: false,
              lpo: false,
              teamFormation: false,
              closure: false,
              impact: false,
            };
            return (
              <fieldset key={key} className="flex flex-col gap-2">
                <legend className="font-medium">{t(`settings.${key}`)}</legend>
                {(['charter', 'lpo', 'teamFormation', 'closure', 'impact'] as const).map((step) => (
                  <Switch
                    key={step}
                    checked={template[step]}
                    onCheckedChange={(checked) => update({ [key]: { ...template, [step]: checked } })}
                    label={t(`settings.steps.${step}`)}
                  />
                ))}
              </fieldset>
            );
          })}
        </div>
        <div className="grid gap-4 md:grid-cols-2">
          <FormField label={t('settings.monthlyDueWorkingDays')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={String(value.monthlyDueWorkingDays ?? '')}
              onChange={(e) => update({ monthlyDueWorkingDays: Number(e.target.value) })}
            />
          </FormField>
          <FormField label={t('settings.monthlyReminderWorkingDays')}>
            <Input
              dir="ltr"
              inputMode="numeric"
              value={String(value.monthlyReminderWorkingDays ?? '')}
              onChange={(e) => update({ monthlyReminderWorkingDays: Number(e.target.value) })}
            />
          </FormField>
          <FormField label={t('settings.progressTolerance')} hint={t('settings.progressToleranceHint')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={String(Math.round((value.progressTolerance ?? 0) * 10000) / 100)}
              onChange={(e) => update({ progressTolerance: Number(e.target.value) / 100 })}
            />
          </FormField>
          <FormField label={t('settings.gmThreshold')} hint={t('settings.gmThresholdHint')}>
            <Input
              dir="ltr"
              inputMode="decimal"
              value={String(value.gmThreshold ?? '')}
              onChange={(e) => update({ gmThreshold: Number(e.target.value) })}
            />
          </FormField>
        </div>
      </section>
      <FeaturesSection value={value} update={update} />
      {!prioritisationOn && (
        <StatusBanner
          tone="info"
          title={tv('settings.prioritisationOff')}
          description={tv('settings.prioritisationOffHint')}
        />
      )}
      <fieldset
        disabled={!prioritisationOn}
        className="flex flex-col gap-3 rounded-lg border bg-card p-6 disabled:opacity-70"
        data-testid="portfolio-settings-criteria"
      >
        <h2 className="font-semibold">{t('settings.criteria')}</h2>
        {criteria.map((c, i) => (
          <div
            key={i}
            className="grid gap-2 md:grid-cols-[1fr_2fr_2fr_6rem_auto_auto]"
            data-testid="criterion-row"
          >
            <Input
              dir="ltr"
              aria-label={t('settings.code')}
              value={c.code}
              onChange={(e) =>
                update({
                  priorityCriteria: criteria.map((x, j) => (j === i ? { ...x, code: e.target.value } : x)),
                })
              }
            />
            <Input
              dir="rtl"
              aria-label={t('settings.labelAr')}
              value={c.labelAr}
              onChange={(e) =>
                update({
                  priorityCriteria: criteria.map((x, j) => (j === i ? { ...x, labelAr: e.target.value } : x)),
                })
              }
            />
            <Input
              dir="ltr"
              aria-label={t('settings.labelEn')}
              value={c.labelEn}
              onChange={(e) =>
                update({
                  priorityCriteria: criteria.map((x, j) => (j === i ? { ...x, labelEn: e.target.value } : x)),
                })
              }
            />
            <Input
              dir="ltr"
              inputMode="decimal"
              aria-label={t('settings.weight')}
              value={String(c.weight)}
              onChange={(e) =>
                update({
                  priorityCriteria: criteria.map((x, j) =>
                    j === i ? { ...x, weight: Number(e.target.value) } : x,
                  ),
                })
              }
            />
            <Switch
              checked={c.active}
              onCheckedChange={(checked) =>
                update({
                  priorityCriteria: criteria.map((x, j) => (j === i ? { ...x, active: checked } : x)),
                })
              }
              label={t('settings.active')}
            />
            <Button
              variant="ghost"
              size="sm"
              aria-label={tc('actions.clear')}
              onClick={() => update({ priorityCriteria: criteria.filter((_, j) => j !== i) })}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <div>
          <Button
            variant="outline"
            onClick={() =>
              update({
                priorityCriteria: [
                  ...criteria,
                  { code: '', labelAr: '', labelEn: '', weight: 1, active: true },
                ],
              })
            }
          >
            <Plus aria-hidden />
            {t('settings.addCriterion')}
          </Button>
        </div>
      </fieldset>
      <fieldset
        disabled={!prioritisationOn}
        className="flex flex-col gap-3 rounded-lg border bg-card p-6 disabled:opacity-70"
        data-testid="portfolio-settings-bands"
      >
        <h2 className="font-semibold">{t('settings.bands')}</h2>
        {bands.map((b, i) => (
          <div key={i} className="grid gap-2 md:grid-cols-[1fr_2fr_2fr_6rem_auto]" data-testid="band-row">
            <Input
              dir="ltr"
              aria-label={t('settings.code')}
              value={b.code}
              onChange={(e) =>
                update({ priorityBands: bands.map((x, j) => (j === i ? { ...x, code: e.target.value } : x)) })
              }
            />
            <Input
              dir="rtl"
              aria-label={t('settings.labelAr')}
              value={b.labelAr}
              onChange={(e) =>
                update({
                  priorityBands: bands.map((x, j) => (j === i ? { ...x, labelAr: e.target.value } : x)),
                })
              }
            />
            <Input
              dir="ltr"
              aria-label={t('settings.labelEn')}
              value={b.labelEn}
              onChange={(e) =>
                update({
                  priorityBands: bands.map((x, j) => (j === i ? { ...x, labelEn: e.target.value } : x)),
                })
              }
            />
            <Input
              dir="ltr"
              inputMode="decimal"
              aria-label={t('settings.minimum')}
              value={String(Math.round(b.minimum * 10000) / 100)}
              onChange={(e) =>
                update({
                  priorityBands: bands.map((x, j) =>
                    j === i ? { ...x, minimum: Number(e.target.value) / 100 } : x,
                  ),
                })
              }
            />
            <Button
              variant="ghost"
              size="sm"
              aria-label={tc('actions.clear')}
              onClick={() => update({ priorityBands: bands.filter((_, j) => j !== i) })}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <div>
          <Button
            variant="outline"
            onClick={() =>
              update({ priorityBands: [...bands, { code: '', labelAr: '', labelEn: '', minimum: 0 }] })
            }
          >
            <Plus aria-hidden />
            {t('settings.addBand')}
          </Button>
        </div>
      </fieldset>
      <AgileSection value={value} update={update} />
      <CostSection value={value} update={update} />
      <WaselSettingsSection value={value} update={update} />
      {featureOn(value, 'health') && <HealthSection value={value} update={update} />}
      {featureOn(value, 'gateChecklists') && <GateChecklistSection value={value} update={update} />}
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-6">
        <h2 className="font-semibold">{t('settings.template')}</h2>
        {value.projectScopeTemplateFileId ? (
          <a
            className="text-primary hover:underline"
            href={contentUrl(value.projectScopeTemplateFileId, undefined, true)}
          >
            {t('actions.downloadTemplate')}
          </a>
        ) : (
          <p className="text-sm text-muted-foreground">{t('settings.templateNone')}</p>
        )}
        <FileDrop
          label={t('settings.templateUpload')}
          file={file}
          onFile={setFile}
          accept=".docx,.pdf,.xlsx"
        />
        <div>
          <Button
            variant="outline"
            disabled={!file}
            loading={upload.isPending}
            onClick={() =>
              file &&
              upload.mutate(file, {
                onSuccess: (doc) => {
                  setFile(null);
                  if (doc.value) setValue(doc.value);
                  toast({ title: t('settings.saved') });
                },
                onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
              })
            }
          >
            <Upload aria-hidden />
            {t('settings.templateUpload')}
          </Button>
        </div>
      </section>
      <div className="flex justify-end">
        <Button
          loading={save.isPending}
          onClick={() =>
            save.mutate(
              { settings: value, version: settings.data.version },
              {
                onSuccess: () => {
                  toast({ title: t('settings.saved') });
                  void queryClient.invalidateQueries({ queryKey: featureKeys.all });
                },
              },
            )
          }
        >
          {tc('actions.save')}
        </Button>
      </div>
      {templatesOn && <TemplatesEditor />}
    </div>
  );
}
