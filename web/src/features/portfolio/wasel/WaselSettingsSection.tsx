import { useTranslation } from 'react-i18next';
import type { components } from '@/api/schema';
import { FormField, Input, Select, Switch } from '@/ui';
import type { PortfolioSettingsValue } from '../api';
import { expiryRules, updateModes } from './waselApi';

type WaselSettings = components['schemas']['WaselSettings'];
type VisibleFields = components['schemas']['VendorVisibleFields'];

const visibleKeys = ['description', 'progress', 'owner', 'budget', 'approvalStatus', 'milestoneWeight', 'milestoneCost'] as const;
const visibleDefaults: Record<(typeof visibleKeys)[number], boolean> = {
  description: true,
  progress: true,
  owner: false,
  budget: false,
  approvalStatus: false,
  milestoneWeight: true,
  milestoneCost: false,
};

/** Settings → Portfolio → Wasel (docs/design/portfolio.md §49) and the capacity threshold (§58). */
export function WaselSettingsSection({
  value,
  update,
}: {
  value: PortfolioSettingsValue;
  update: (patch: Partial<PortfolioSettingsValue>) => void;
}) {
  const { t } = useTranslation('vendors');
  const w: WaselSettings = value.wasel ?? {};
  const set = (patch: Partial<WaselSettings>) => update({ wasel: { ...w, ...patch } });
  const fields: VisibleFields = w.visibleFields ?? {};
  const setField = (key: (typeof visibleKeys)[number], checked: boolean) => set({ visibleFields: { ...fields, [key]: checked } });
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-6" data-testid="portfolio-settings-wasel">
      <div>
        <h2 className="font-semibold">{t('settings.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.hint')}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <FormField label={t('settings.defaultUpdateMode')}>
          <Select
            value={w.defaultUpdateMode ?? 'pm-only'}
            onValueChange={(defaultUpdateMode) => set({ defaultUpdateMode })}
            options={updateModes.map((m) => ({ value: m, label: t(`modes.${m}`) }))}
          />
        </FormField>
        <FormField label={t('settings.expiryRule')} hint={t('panel.expiryHint')}>
          <Select
            value={w.expiryRule ?? 'contract-end'}
            onValueChange={(expiryRule) => set({ expiryRule })}
            options={expiryRules.map((r) => ({ value: r, label: t(`expiry.${r}`) }))}
          />
        </FormField>
        <FormField label={t('settings.dormantDays')} hint={t('settings.dormantDaysHint')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            data-testid="wasel-dormant-days"
            value={String(w.dormantDays ?? 90)}
            onChange={(e) => set({ dormantDays: Number(e.target.value) })}
          />
        </FormField>
        <FormField label={t('settings.capacityThreshold')} hint={t('settings.capacityThresholdHint')}>
          <Input
            dir="ltr"
            inputMode="decimal"
            data-testid="capacity-threshold"
            value={String(Math.round((value.capacityThreshold ?? 1) * 10000) / 100)}
            onChange={(e) => update({ capacityThreshold: Number(e.target.value) / 100 })}
          />
        </FormField>
      </div>
      <Switch
        checked={w.endAtClosure ?? true}
        onCheckedChange={(endAtClosure) => set({ endAtClosure })}
        label={t('settings.endAtClosure')}
        description={t('settings.endAtClosureHint')}
      />
      <Switch
        checked={w.uaePassForVendors ?? false}
        onCheckedChange={(uaePassForVendors) => set({ uaePassForVendors })}
        label={t('settings.uaePass')}
        description={t('settings.uaePassHint')}
      />
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium">{t('settings.visibleFields')}</legend>
        <p className="text-xs text-muted-foreground">{t('settings.visibleFieldsHint')}</p>
        <div className="grid gap-2 md:grid-cols-2">
          {visibleKeys.map((key) => (
            <Switch
              key={key}
              checked={fields[key] ?? visibleDefaults[key]}
              onCheckedChange={(checked) => setField(key, checked)}
              label={t(`settings.visible.${key}`)}
            />
          ))}
        </div>
      </fieldset>
    </section>
  );
}
