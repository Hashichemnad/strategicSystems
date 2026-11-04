import { Plus, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { components } from '@/api/schema';
import { pickText } from '@/lib/format';
import { Button, Checkbox, FormField, Input, Select, Switch } from '@/ui';
import type { PortfolioSettingsValue } from '../api';
import { usePortfolioOptions } from '../api';
import type { FeatureCode } from '../featuresApi';
import { methodologies } from './viewsApi';

type Features = components['schemas']['PortfolioFeatures'];
type Thresholds = components['schemas']['HealthThresholds'];
type Checklists = components['schemas']['GateChecklists'];
type ChecklistItem = components['schemas']['ChecklistItemDefinition'];

/** Standards switches in the order of docs/design/portfolio.md §32 (defaults: all on except earned value). */
export const featureCodes: readonly FeatureCode[] = [
  'portfolios',
  'prioritisation',
  'health',
  'baselines',
  'earnedValue',
  'raid',
  'riskMatrix',
  'raci',
  'communicationPlan',
  'benefits',
  'quality',
  'procurement',
  'statusReport',
  'gateChecklists',
  'templates',
];

export function featureOn(value: PortfolioSettingsValue, code: FeatureCode): boolean {
  return value.features?.[code] ?? code !== 'earnedValue';
}

export const defaultThresholds: Thresholds = {
  scheduleAmberGap: 0.05,
  scheduleRedGap: 0.15,
  pastDueRedDays: 30,
  costAmberVariance: 0.05,
  costRedVariance: 0.15,
  scopeWindowDays: 180,
  scopeRedChanges: 2,
};

interface SectionProps {
  value: PortfolioSettingsValue;
  update: (patch: Partial<PortfolioSettingsValue>) => void;
}

const card = 'flex flex-col gap-4 rounded-lg border bg-card p-6';
const toNumber = (text: string) => {
  const n = Number(text);
  return Number.isFinite(n) ? n : 0;
};
const percentText = (ratio: number | undefined) => String(Math.round((ratio ?? 0) * 10000) / 100);

/** Settings → Portfolio → Features: one switch per standards enhancement (§32). */
export function FeaturesSection({ value, update }: SectionProps) {
  const { t } = useTranslation('portfolioViews');
  const features: Features = value.features ?? {};
  return (
    <section className={card} data-testid="portfolio-settings-features">
      <div>
        <h2 className="font-semibold">{t('settings.features.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.features.description')}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        {featureCodes.map((code) => (
          <div key={code} data-testid={`feature-${code}`}>
            <Switch
              checked={featureOn(value, code)}
              onCheckedChange={(checked) => update({ features: { ...features, [code]: checked } })}
              label={t(`settings.features.items.${code}.label`)}
              description={t(`settings.features.items.${code}.description`)}
            />
          </div>
        ))}
      </div>
    </section>
  );
}

/** Agile delivery (§31): approval mode, default sprint length, methodology suggestion per program type, story creation. */
export function AgileSection({ value, update }: SectionProps) {
  const { t, i18n } = useTranslation('portfolioViews');
  const options = usePortfolioOptions();
  const suggestions = value.methodologySuggestions ?? {};
  const NONE = 'none';
  return (
    <section className={card} data-testid="portfolio-settings-agile">
      <h2 className="font-semibold">{t('settings.agile.title')}</h2>
      <div className="grid gap-4 md:grid-cols-2">
        <FormField label={t('settings.agile.approvalMode')} hint={t('settings.agile.approvalModeHint')}>
          <Select
            value={value.agileApprovalMode ?? 'sprint'}
            onValueChange={(v) => update({ agileApprovalMode: v })}
            options={(['sprint', 'monthly'] as const).map((m) => ({
              value: m,
              label: t(`settings.agile.modes.${m}`),
            }))}
          />
        </FormField>
        <FormField label={t('settings.agile.sprintLength')}>
          <Input
            dir="ltr"
            inputMode="numeric"
            value={String(value.defaultSprintLengthDays ?? 14)}
            onChange={(e) => update({ defaultSprintLengthDays: Math.round(toNumber(e.target.value)) })}
          />
        </FormField>
        <div className="md:col-span-2">
          <Switch
            checked={value.teamMembersCreateStories ?? true}
            onCheckedChange={(checked) => update({ teamMembersCreateStories: checked })}
            label={t('settings.agile.membersCreateStories')}
            description={t('settings.agile.membersCreateStoriesHint')}
          />
        </div>
      </div>
      <div className="flex flex-col gap-2">
        <h3 className="font-medium">{t('settings.agile.suggestions')}</h3>
        <p className="text-sm text-muted-foreground">{t('settings.agile.suggestionsHint')}</p>
        <div className="grid gap-3 md:grid-cols-2">
          {(options.data?.programTypes ?? []).map((type) => (
            <FormField
              key={type.code}
              label={pickText(i18n.language, type.labelAr, type.labelEn) || type.code}
            >
              <Select
                value={suggestions[type.code] ?? NONE}
                onValueChange={(v) => {
                  const next = Object.fromEntries(
                    Object.entries(suggestions).filter(([code]) => code !== type.code),
                  );
                  update({ methodologySuggestions: v === NONE ? next : { ...next, [type.code]: v } });
                }}
                options={[
                  { value: NONE, label: t('settings.agile.noSuggestion') },
                  ...methodologies.map((m) => ({ value: m, label: t(`methodology.${m}`) })),
                ]}
              />
            </FormField>
          ))}
        </div>
      </div>
    </section>
  );
}

/** Cost visibility (§39). */
export function CostSection({ value, update }: SectionProps) {
  const { t } = useTranslation('portfolioViews');
  return (
    <section className={card} data-testid="portfolio-settings-costs">
      <h2 className="font-semibold">{t('settings.costs.title')}</h2>
      <Switch
        checked={value.teamMembersSeeCosts ?? false}
        onCheckedChange={(checked) => update({ teamMembersSeeCosts: checked })}
        label={t('settings.costs.membersSeeCosts')}
        description={t('settings.costs.membersSeeCostsHint')}
      />
    </section>
  );
}

/** Health thresholds (§33). Ratios are edited as percentages. */
export function HealthSection({ value, update }: SectionProps) {
  const { t } = useTranslation('portfolioViews');
  const h: Thresholds = { ...defaultThresholds, ...(value.health ?? {}) };
  const set = (patch: Partial<Thresholds>) => update({ health: { ...h, ...patch } });
  const ratio = (key: 'scheduleAmberGap' | 'scheduleRedGap' | 'costAmberVariance' | 'costRedVariance') => (
    <FormField key={key} label={t(`settings.health.${key}`)}>
      <Input
        dir="ltr"
        inputMode="decimal"
        data-testid={`health-${key}`}
        value={percentText(h[key])}
        onChange={(e) => set({ [key]: toNumber(e.target.value) / 100 })}
      />
    </FormField>
  );
  const whole = (key: 'pastDueRedDays' | 'scopeWindowDays' | 'scopeRedChanges') => (
    <FormField key={key} label={t(`settings.health.${key}`)}>
      <Input
        dir="ltr"
        inputMode="numeric"
        data-testid={`health-${key}`}
        value={String(h[key])}
        onChange={(e) => set({ [key]: Math.round(toNumber(e.target.value)) })}
      />
    </FormField>
  );
  return (
    <section className={card} data-testid="portfolio-settings-health">
      <div>
        <h2 className="font-semibold">{t('settings.health.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.health.description')}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {ratio('scheduleAmberGap')}
        {ratio('scheduleRedGap')}
        {ratio('costAmberVariance')}
        {ratio('costRedVariance')}
        {whole('pastDueRedDays')}
        {whole('scopeWindowDays')}
        {whole('scopeRedChanges')}
      </div>
    </section>
  );
}

const gates = ['charter', 'planning', 'closure'] as const;

/** Stage-gate checklists (§35): per gate the items a project ticks; required ones join the gate. */
export function GateChecklistSection({ value, update }: SectionProps) {
  const { t } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const lists: Checklists = value.gateChecklists ?? { charter: [], planning: [], closure: [] };
  const setGate = (gate: (typeof gates)[number], items: ChecklistItem[]) =>
    update({ gateChecklists: { ...lists, [gate]: items } });
  return (
    <section className={card} data-testid="portfolio-settings-checklists">
      <div>
        <h2 className="font-semibold">{t('settings.checklists.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.checklists.description')}</p>
      </div>
      {gates.map((gate) => {
        const items = lists[gate];
        const edit = (i: number, patch: Partial<ChecklistItem>) =>
          setGate(
            gate,
            items.map((x, j) => (j === i ? { ...x, ...patch } : x)),
          );
        return (
          <fieldset key={gate} className="flex flex-col gap-2" data-testid={`checklist-${gate}`}>
            <legend className="font-medium">{t(`settings.checklists.gates.${gate}`)}</legend>
            {items.length === 0 && (
              <p className="text-sm text-muted-foreground">{t('settings.checklists.empty')}</p>
            )}
            {items.map((item, i) => (
              <div key={i} className="grid items-center gap-2 md:grid-cols-[10rem_1fr_1fr_auto_auto]">
                <Input
                  dir="ltr"
                  aria-label={t('settings.checklists.code')}
                  placeholder={t('settings.checklists.code')}
                  value={item.code}
                  onChange={(e) => edit(i, { code: e.target.value })}
                />
                <Input
                  dir="rtl"
                  aria-label={t('settings.checklists.labelAr')}
                  placeholder={t('settings.checklists.labelAr')}
                  value={item.labelAr ?? ''}
                  onChange={(e) => edit(i, { labelAr: e.target.value })}
                />
                <Input
                  dir="ltr"
                  aria-label={t('settings.checklists.labelEn')}
                  placeholder={t('settings.checklists.labelEn')}
                  value={item.labelEn ?? ''}
                  onChange={(e) => edit(i, { labelEn: e.target.value })}
                />
                <Checkbox
                  label={t('settings.checklists.required')}
                  checked={item.required}
                  onChange={(e) => edit(i, { required: e.target.checked })}
                />
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={tc('actions.clear')}
                  onClick={() =>
                    setGate(
                      gate,
                      items.filter((_, j) => j !== i),
                    )
                  }
                >
                  <Trash2 aria-hidden />
                </Button>
              </div>
            ))}
            <div>
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  setGate(gate, [...items, { code: '', labelAr: '', labelEn: '', required: true }])
                }
              >
                <Plus aria-hidden />
                {t('settings.checklists.add')}
              </Button>
            </div>
          </fieldset>
        );
      })}
    </section>
  );
}
