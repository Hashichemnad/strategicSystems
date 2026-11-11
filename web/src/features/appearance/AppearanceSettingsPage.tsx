import { Link } from '@tanstack/react-router';
import { ArrowDown, ArrowUp, Check, Eye, EyeOff, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { uiThemes, type UiTheme } from '@/app/theme';
import { useDashboards } from '@/features/reporting/api';
import { useRoleOptions } from '@/features/reporting/shared';
import { useServerErrors } from '@/features/settings/common';
import {
  apps,
  appIcons,
  contrastWithWhite,
  type AppDefinition,
  type AppOverride,
  type AppOverrides,
  type LandingRule,
} from '@/layout/apps';
import { useTabLabel } from '@/layout/WorkspaceShell';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogContent,
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
  useToast,
} from '@/ui';
import { useAppearanceSettings, useSaveAppearance, type AppearanceSettings } from './api';

const DEFAULT = '__default';

interface Draft {
  enabledThemes: UiTheme[];
  defaultTheme: UiTheme;
  defaultMode: string;
  allowUserTheme: boolean;
  allowUserMode: boolean;
  landingRules: LandingRule[];
  apps: AppOverrides;
}

function toDraft(value: AppearanceSettings | null | undefined): Draft {
  const enabled = (value?.enabledThemes?.length ? value.enabledThemes : uiThemes).filter(
    (theme): theme is UiTheme => (uiThemes as readonly string[]).includes(theme),
  );
  return {
    enabledThemes: enabled,
    defaultTheme: (value?.defaultTheme ?? 'classic') as UiTheme,
    defaultMode: value?.defaultMode ?? 'system',
    allowUserTheme: value?.allowUserTheme ?? false,
    allowUserMode: value?.allowUserMode ?? true,
    landingRules: (value?.landingRules ?? []).map((rule) => ({ roleCode: rule.roleCode, app: rule.app })),
    apps: value?.apps ?? {},
  };
}

/** Settings → Appearance (SPEC-ADDENDUM §C1–C2, docs/design/themes.md §7, ADR 0171). */
export function AppearanceSettingsPage() {
  const { t } = useTranslation('appearance');
  const toast = useToast();
  const errors = useServerErrors();
  const current = useAppearanceSettings();
  const save = useSaveAppearance();
  const [draft, setDraft] = useState<Draft>();
  const [editing, setEditing] = useState<AppDefinition>();
  useEffect(() => setDraft(toDraft(current.data?.value)), [current.data]);

  if (current.isError) return <StatusBanner tone="danger" title={t('home.notFound')} />;
  if (!draft) return <Skeleton className="h-96" />;

  const submit = () =>
    save.mutate(
      {
        value: {
          enabledThemes: draft.enabledThemes,
          defaultTheme: draft.defaultTheme,
          defaultMode: draft.defaultMode,
          allowUserTheme: draft.allowUserTheme,
          allowUserMode: draft.allowUserMode,
          landingRules: draft.landingRules,
          apps: draft.apps as AppearanceSettings['apps'],
        },
        version: current.data?.version ?? 0,
      },
      {
        onSuccess: () => {
          errors.clear();
          toast({ title: t('settings.saved') });
        },
        onError: errors.fail,
      },
    );

  return (
    <div className="flex flex-col gap-6" data-testid="appearance-settings">
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <form
        className="flex flex-col gap-6"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <ThemesSection draft={draft} setDraft={setDraft} errorFor={errors.errorFor} />
        <LandingSection draft={draft} setDraft={setDraft} errorFor={errors.errorFor} />
        <AppsSection draft={draft} onEdit={setEditing} />
        <div className="sticky bottom-0 z-10 -mx-1 flex justify-end gap-2 border-t bg-background/95 px-1 py-3 backdrop-blur md:bottom-10">
          <Button type="submit" loading={save.isPending} data-testid="appearance-save">
            <Check aria-hidden />
            {t('settings.save')}
          </Button>
        </div>
      </form>
      {editing && (
        <AppDialog
          app={editing}
          override={draft.apps[editing.code]}
          onClose={() => setEditing(undefined)}
          onSave={(override) => {
            setDraft({ ...draft, apps: { ...draft.apps, [editing.code]: override } });
            setEditing(undefined);
          }}
        />
      )}
    </div>
  );
}

type SectionProps = {
  draft: Draft;
  setDraft: (draft: Draft) => void;
  errorFor: (field: string) => string | undefined;
};

function Section({
  id,
  title,
  description,
  children,
}: {
  id: string;
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-4 rounded-lg border bg-card p-5">
      <div className="flex flex-col gap-1">
        <h2 id={id} className="text-lg font-semibold">
          {title}
        </h2>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}

function ThemesSection({ draft, setDraft, errorFor }: SectionProps) {
  const { t } = useTranslation('appearance');
  const toggleTheme = (theme: UiTheme, on: boolean) => {
    const enabled = on
      ? uiThemes.filter((x) => x === theme || draft.enabledThemes.includes(x))
      : draft.enabledThemes.filter((x) => x !== theme);
    if (enabled.length === 0) return;
    setDraft({ ...draft, enabledThemes: enabled });
  };
  return (
    <Section id="appearance-themes" title={t('settings.themesSection')}>
      <div className="grid gap-4 md:grid-cols-3" role="radiogroup" aria-label={t('settings.default')}>
        {uiThemes.map((theme) => {
          const enabled = draft.enabledThemes.includes(theme);
          const isDefault = draft.defaultTheme === theme;
          return (
            <div
              key={theme}
              className={cn(
                'flex flex-col gap-3 rounded-lg border p-3',
                isDefault && 'border-primary ring-1 ring-primary',
              )}
              data-testid={`theme-card-${theme}`}
            >
              <ThemePreview theme={theme} />
              <div className="flex flex-col gap-1">
                <p className="font-semibold">{t(`themes.${theme}`)}</p>
                <p className="text-xs text-muted-foreground">{t(`themes.${theme}Description`)}</p>
              </div>
              <Switch
                checked={enabled}
                disabled={isDefault}
                onCheckedChange={(on) => toggleTheme(theme, on)}
                label={t('settings.enabled')}
              />
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  name="default-theme"
                  className="size-4 accent-primary"
                  checked={isDefault}
                  disabled={!enabled}
                  onChange={() => setDraft({ ...draft, defaultTheme: theme })}
                  data-testid={`default-theme-${theme}`}
                />
                {t('settings.default')}
              </label>
            </div>
          );
        })}
      </div>
      {errorFor('defaultTheme') && (
        <p className="text-sm text-danger" role="alert">
          {errorFor('defaultTheme')}
        </p>
      )}
      <div className="grid gap-4 md:grid-cols-2">
        <FormField label={t('settings.defaultMode')} error={errorFor('defaultMode')}>
          <Select
            value={draft.defaultMode}
            onValueChange={(value) => setDraft({ ...draft, defaultMode: value })}
            options={(['light', 'dark', 'system'] as const).map((mode) => ({
              value: mode,
              label: t(`modes.${mode}`),
            }))}
          />
        </FormField>
      </div>
      <Switch
        checked={draft.allowUserTheme}
        onCheckedChange={(on) => setDraft({ ...draft, allowUserTheme: on })}
        label={t('settings.allowUserTheme')}
        description={t('settings.allowUserThemeHint')}
        id="allow-user-theme"
      />
      <Switch
        checked={draft.allowUserMode}
        onCheckedChange={(on) => setDraft({ ...draft, allowUserMode: on })}
        label={t('settings.allowUserMode')}
        description={t('settings.allowUserModeHint')}
        id="allow-user-mode"
      />
      <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        {t('settings.brandingNote')}
        <Link to="/settings/branding" className="text-primary underline underline-offset-2">
          {t('settings.brandingLink')}
        </Link>
      </p>
    </Section>
  );
}

/** A miniature of the theme drawn with its own tokens (the `data-theme` scope), not a picture. */
function ThemePreview({ theme }: { theme: UiTheme }) {
  const { t } = useTranslation('appearance');
  return (
    <div
      data-theme={theme}
      className="overflow-hidden rounded-[var(--radius)] border bg-background text-foreground"
      role="img"
      aria-label={t('settings.preview', { theme: t(`themes.${theme}`) })}
    >
      <div className="flex h-6 items-center gap-1 border-b bg-header px-2">
        <span className="size-2.5 rounded-sm bg-primary" />
        <span className="h-1.5 w-10 rounded bg-muted-foreground/40" />
      </div>
      <div className="flex">
        {theme === 'classic' && <div className="w-8 shrink-0 border-e bg-sidebar" />}
        {theme === 'modern' && <div className="w-10 shrink-0 bg-sidebar" />}
        <div className="flex flex-1 flex-col gap-1.5 p-2">
          {theme === 'enterprise' && (
            <div className="flex gap-2 border-b pb-1">
              <span className="h-1.5 w-6 rounded bg-primary" />
              <span className="h-1.5 w-6 rounded bg-muted-foreground/30" />
              <span className="h-1.5 w-6 rounded bg-muted-foreground/30" />
            </div>
          )}
          <div className="flex flex-col gap-1 rounded-[var(--radius)] border bg-card p-2 shadow-[var(--shadow-card)]">
            <span className="text-[10px] font-semibold">{t('settings.previewSample')}</span>
            <span className="h-1.5 w-3/4 rounded bg-muted-foreground/30" />
            <span className="mt-1 inline-flex w-fit rounded-[calc(var(--radius)-2px)] bg-primary px-1.5 text-[9px] text-primary-foreground">
              {t('settings.previewButton')}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

function LandingSection({ draft, setDraft, errorFor }: SectionProps) {
  const { t } = useTranslation('appearance');
  const roles = useRoleOptions();
  const translate = t as unknown as (key: string) => string;
  const appOptions = apps.map((app) => ({ value: app.code, label: translate(`apps.${app.code}.name`) }));
  const rules = draft.landingRules;
  const update = (next: LandingRule[]) => setDraft({ ...draft, landingRules: next });
  const move = (index: number, delta: number) => {
    const next = [...rules];
    const [rule] = next.splice(index, 1);
    if (!rule) return;
    next.splice(index + delta, 0, rule);
    update(next);
  };
  return (
    <Section
      id="appearance-landing"
      title={t('settings.landingSection')}
      description={t('settings.landingDescription')}
    >
      {rules.length === 0 && <p className="text-sm text-muted-foreground">{t('settings.noRules')}</p>}
      <ol className="flex flex-col gap-3" data-testid="landing-rules">
        {rules.map((rule, index) => (
          <li key={index} className="flex flex-col gap-2 rounded-md border p-3 sm:flex-row sm:items-end">
            <FormField
              className="flex-1"
              label={t('settings.role')}
              error={errorFor(`landingRules[${index}].roleCode`)}
            >
              <Select
                value={rule.roleCode || undefined}
                onValueChange={(value) =>
                  update(rules.map((r, i) => (i === index ? { ...r, roleCode: value } : r)))
                }
                options={roles}
                placeholder={t('settings.role')}
              />
            </FormField>
            <FormField
              className="flex-1"
              label={t('settings.app')}
              error={errorFor(`landingRules[${index}].app`)}
            >
              <Select
                value={rule.app}
                onValueChange={(value) =>
                  update(rules.map((r, i) => (i === index ? { ...r, app: value } : r)))
                }
                options={appOptions}
              />
            </FormField>
            <div className="flex gap-1">
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={index === 0}
                onClick={() => move(index, -1)}
                aria-label={t('settings.moveUp')}
              >
                <ArrowUp aria-hidden />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={index === rules.length - 1}
                onClick={() => move(index, 1)}
                aria-label={t('settings.moveDown')}
              >
                <ArrowDown aria-hidden />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onClick={() => update(rules.filter((_, i) => i !== index))}
                aria-label={t('settings.removeRule')}
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          </li>
        ))}
      </ol>
      <div>
        <Button
          type="button"
          variant="outline"
          onClick={() => update([...rules, { roleCode: '', app: 'mywork' }])}
          data-testid="add-landing-rule"
        >
          <Plus aria-hidden />
          {t('settings.addRule')}
        </Button>
      </div>
    </Section>
  );
}

function AppsSection({ draft, onEdit }: { draft: Draft; onEdit: (app: AppDefinition) => void }) {
  const { t, i18n } = useTranslation('appearance');
  const translate = t as unknown as (key: string) => string;
  return (
    <Section
      id="appearance-apps"
      title={t('settings.appsSection')}
      description={t('settings.appsDescription')}
    >
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('settings.app')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.accent')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.tabs')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('settings.edit', { name: '' })}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {apps.map((app) => {
            const override = draft.apps[app.code];
            const Icon = (override?.icon && appIcons[override.icon]) || appIcons[app.icon];
            const accent = override?.accent ?? app.accent;
            const name =
              pickText(i18n.language, override?.nameAr, override?.nameEn) ||
              translate(`apps.${app.code}.name`);
            const hidden = override?.hiddenTabs?.length ?? 0;
            return (
              <TableRow key={app.code} data-testid={`appearance-app-${app.code}`}>
                <TableCell>
                  <span className="flex items-center gap-2 font-medium">
                    <span
                      className="flex size-7 items-center justify-center rounded-md text-white [&_svg]:size-4"
                      style={{ backgroundColor: accent }}
                      aria-hidden
                    >
                      {Icon && <Icon />}
                    </span>
                    {name}
                  </span>
                </TableCell>
                <TableCell>
                  <bdi className="font-mono text-xs">{accent}</bdi>
                </TableCell>
                <TableCell>
                  {formatNumber(app.tabs.length - hidden, i18n.language)}
                  {hidden > 0 && (
                    <StatusChip tone="neutral" className="ms-2">
                      {t('settings.hidden')}: {formatNumber(hidden, i18n.language)}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell className="text-end">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => onEdit(app)}
                    aria-label={t('settings.edit', { name })}
                    data-testid={`edit-app-${app.code}`}
                  >
                    <Pencil aria-hidden />
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </Section>
  );
}

function AppDialog({
  app,
  override,
  onClose,
  onSave,
}: {
  app: AppDefinition;
  override: AppOverride | undefined;
  onClose: () => void;
  onSave: (override: AppOverride) => void;
}) {
  const { t, i18n } = useTranslation('appearance');
  const translate = t as unknown as (key: string) => string;
  const tabLabel = useTabLabel();
  const dashboards = useDashboards();
  const [nameAr, setNameAr] = useState(override?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(override?.nameEn ?? '');
  const [icon, setIcon] = useState(override?.icon ?? app.icon);
  const [accent, setAccent] = useState(override?.accent ?? app.accent);
  const [dashboard, setDashboard] = useState(override?.dashboard ?? DEFAULT);
  const initialOrder = useMemo(() => {
    const order = override?.tabOrder ?? [];
    return [...app.tabs].sort((a, b) => {
      const ia = order.indexOf(a.key);
      const ib = order.indexOf(b.key);
      return (ia < 0 ? 1000 + app.tabs.indexOf(a) : ia) - (ib < 0 ? 1000 + app.tabs.indexOf(b) : ib);
    });
  }, [app, override]);
  const [tabs, setTabs] = useState(initialOrder);
  const [hidden, setHidden] = useState<string[]>([...(override?.hiddenTabs ?? [])]);
  const ratio = contrastWithWhite(accent);
  const name = translate(`apps.${app.code}.name`);
  const validColour = /^#[0-9a-fA-F]{6}$/.test(accent);

  const move = (index: number, delta: number) => {
    const next = [...tabs];
    const [tab] = next.splice(index, 1);
    if (!tab) return;
    next.splice(index + delta, 0, tab);
    setTabs(next);
  };

  const dashboardOptions = [
    {
      value: DEFAULT,
      label: app.dashboard
        ? t('settings.defaultDashboard', { code: app.dashboard })
        : t('settings.noDashboardOption'),
    },
    ...(dashboards.data ?? []).map((d) => ({
      value: d.code,
      label: pickText(i18n.language, d.titleAr, d.titleEn) || d.code,
    })),
  ];

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('settings.edit', { name })}
        className="max-w-2xl"
        footer={
          <>
            <Button
              variant="ghost"
              onClick={() => {
                onSave({});
              }}
            >
              {t('settings.resetApp')}
            </Button>
            <Button variant="outline" onClick={onClose}>
              {t('settings.cancel')}
            </Button>
            <Button
              disabled={!validColour}
              onClick={() =>
                onSave({
                  nameAr: nameAr.trim() || null,
                  nameEn: nameEn.trim() || null,
                  icon: icon === app.icon ? null : icon,
                  accent: accent.toLowerCase() === app.accent.toLowerCase() ? null : accent,
                  dashboard: dashboard === DEFAULT ? null : dashboard,
                  tabOrder: tabs.map((tab) => tab.key),
                  hiddenTabs: hidden.filter((key) => key !== 'home'),
                })
              }
              data-testid="app-dialog-done"
            >
              {t('settings.done')}
            </Button>
          </>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('settings.nameAr')}>
            <Input
              dir="rtl"
              value={nameAr}
              maxLength={60}
              placeholder={name}
              onChange={(e) => setNameAr(e.target.value)}
            />
          </FormField>
          <FormField label={t('settings.nameEn')}>
            <Input
              dir="ltr"
              value={nameEn}
              maxLength={60}
              placeholder={name}
              onChange={(e) => setNameEn(e.target.value)}
            />
          </FormField>
          <FormField label={t('settings.icon')}>
            <Select
              value={icon}
              onValueChange={setIcon}
              options={Object.keys(appIcons).map((code) => ({ value: code, label: code }))}
            />
          </FormField>
          <FormField
            label={t('settings.accent')}
            error={ratio < 3 ? t('settings.lowContrast', { ratio: ratio.toFixed(1) }) : undefined}
          >
            <div className="flex gap-2">
              <input
                type="color"
                value={validColour ? accent : '#000000'}
                onChange={(e) => setAccent(e.target.value)}
                className="h-[var(--control-h)] w-12 shrink-0 cursor-pointer rounded-md border border-input bg-card p-1"
                aria-label={t('settings.accent')}
              />
              <Input
                dir="ltr"
                value={accent}
                onChange={(e) => setAccent(e.target.value)}
                aria-label={`${t('settings.accent')} (hex)`}
              />
            </div>
          </FormField>
          <FormField label={t('settings.dashboard')} className="sm:col-span-2">
            <Select value={dashboard} onValueChange={setDashboard} options={dashboardOptions} />
          </FormField>
        </div>
        <fieldset className="flex flex-col gap-2">
          <legend className="pb-2 text-sm font-medium">{t('settings.editTabs', { name })}</legend>
          <ol className="flex flex-col gap-1">
            {tabs.map((tab, index) => {
              const isHidden = hidden.includes(tab.key);
              return (
                <li key={tab.key} className="flex items-center gap-2 rounded-md border px-3 py-1.5">
                  <span className={cn('flex-1 text-sm', isHidden && 'text-muted-foreground line-through')}>
                    {tabLabel(app.code, tab)}
                  </span>
                  {tab.key !== 'home' && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      aria-pressed={!isHidden}
                      onClick={() =>
                        setHidden(isHidden ? hidden.filter((key) => key !== tab.key) : [...hidden, tab.key])
                      }
                      aria-label={`${tabLabel(app.code, tab)}: ${t(isHidden ? 'settings.hidden' : 'settings.visible')}`}
                    >
                      {isHidden ? <EyeOff aria-hidden /> : <Eye aria-hidden />}
                      <span className="hidden sm:inline">
                        {t(isHidden ? 'settings.hidden' : 'settings.visible')}
                      </span>
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8"
                    disabled={index === 0}
                    onClick={() => move(index, -1)}
                    aria-label={`${tabLabel(app.code, tab)}: ${t('settings.moveUp')}`}
                  >
                    <ArrowUp aria-hidden />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8"
                    disabled={index === tabs.length - 1}
                    onClick={() => move(index, 1)}
                    aria-label={`${tabLabel(app.code, tab)}: ${t('settings.moveDown')}`}
                  >
                    <ArrowDown aria-hidden />
                  </Button>
                </li>
              );
            })}
          </ol>
        </fieldset>
      </DialogContent>
    </Dialog>
  );
}
