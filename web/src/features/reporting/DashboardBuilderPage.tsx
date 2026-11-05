import { Link } from '@tanstack/react-router';
import { ArrowDown, ArrowUp, Eye, Plus, Send, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  useToast,
} from '@/ui';
import {
  useCatalogue,
  useDashboard,
  useDashboards,
  usePublishDashboard,
  useSaveDashboard,
  useSetDashboardRoles,
  type WidgetConfig,
} from './api';
import { moveItem } from './MyWorkCards';
import { useRoleOptions } from './shared';

const widths = [3, 4, 6, 8, 12];

/** Lays widgets out in reading order on the 12-column grid (pure; unit-tested): rows fill left to right, a widget too wide starts a new row. */
export function flowLayout(widgets: readonly WidgetConfig[]): WidgetConfig[] {
  let x = 0;
  let y = 0;
  let rowHeight = 0;
  return widgets.map((w) => {
    const width = Math.min(12, Math.max(1, w.w));
    if (x + width > 12) {
      x = 0;
      y += rowHeight;
      rowHeight = 0;
    }

    const placed = { ...w, x, y, w: width };
    x += width;
    rowHeight = Math.max(rowHeight, w.h);
    return placed;
  });
}

function newId(widgets: readonly WidgetConfig[]) {
  let i = widgets.length + 1;
  while (widgets.some((w) => w.id === `w${i}`)) i++;
  return `w${i}`;
}

function Editor({ dashboardId }: { dashboardId?: string }) {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const manage = useCan('reporting.dashboard.manage');
  const existing = useDashboard(dashboardId ?? '');
  const catalogue = useCatalogue();
  const save = useSaveDashboard(dashboardId);
  const publish = usePublishDashboard();
  const setRoles = useSetDashboardRoles();
  const roleOptions = useRoleOptions(manage);
  const [titleAr, setTitleAr] = useState('');
  const [titleEn, setTitleEn] = useState('');
  const [level, setLevel] = useState<'custom' | 'role' | 'system' | 'executive'>(manage ? 'role' : 'custom');
  const [widgets, setWidgets] = useState<WidgetConfig[]>([]);
  const [roles, setRolesState] = useState<string[]>([]);
  const [savedId, setSavedId] = useState(dashboardId);
  useEffect(() => {
    const d = existing.data;
    if (dashboardId && d) {
      setTitleAr(d.titleAr);
      setTitleEn(d.titleEn);
      setLevel(d.level as typeof level);
      setWidgets(((d.draft ?? d.definition).widgets ?? []) as WidgetConfig[]);
      setRolesState(d.roleKeys);
    }
  }, [existing.data, dashboardId]);

  const metrics = catalogue.data?.metrics ?? [];
  const metricOptions = metrics.map((m) => ({
    value: m.code,
    label: `${pickText(i18n.language, m.labelAr, m.labelEn)} (${m.systemCode})`,
  }));
  const listOptions = (catalogue.data?.lists ?? []).map((l) => ({
    value: l.code,
    label: pickText(i18n.language, l.labelAr, l.labelEn),
  }));
  const update = (index: number, patch: Partial<WidgetConfig>) =>
    setWidgets((ws) => ws.map((w, i) => (i === index ? { ...w, ...patch } : w)));
  const laidOut = useMemo(() => flowLayout(widgets), [widgets]);

  const submit = (then?: (id: string) => void) =>
    save.mutate(
      {
        input: {
          titleAr: titleAr || titleEn,
          titleEn: titleEn || titleAr,
          descriptionAr: null,
          descriptionEn: null,
          level,
          systemCode: existing.data?.systemCode ?? null,
          definition: { widgets: laidOut },
        },
        version: existing.data?.version,
      },
      {
        onSuccess: (d) => {
          setSavedId(d.id);
          toast({ title: t('builder2.saved') });
          then?.(d.id);
        },
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      },
    );

  if (catalogue.isPending || (dashboardId && existing.isPending)) return <Skeleton className="h-96" />;
  return (
    <div className="flex flex-col gap-4" data-testid="dashboard-builder">
      {save.isError && <StatusBanner tone="danger" title={errorMessage(tc, save.error)} />}
      <div className="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-3">
        <FormField label={t('builder2.titleAr')}>
          <Input
            dir="rtl"
            value={titleAr}
            onChange={(e) => setTitleAr(e.target.value)}
            data-testid="builder-title-ar"
          />
        </FormField>
        <FormField label={t('builder2.titleEn')}>
          <Input
            dir="ltr"
            value={titleEn}
            onChange={(e) => setTitleEn(e.target.value)}
            data-testid="builder-title-en"
          />
        </FormField>
        <FormField label={t('builder2.level')}>
          <Select
            value={level}
            disabled={!!dashboardId}
            onValueChange={(v) => setLevel(v as typeof level)}
            options={(manage ? ['role', 'custom'] : ['custom']).map((l) => ({
              value: l,
              label: t(`builder2.levels.${l}` as 'builder2.levels.role'),
            }))}
          />
        </FormField>
      </div>
      <div className="flex flex-col gap-2">
        <h2 className="font-semibold">{t('builder2.widgets')}</h2>
        {widgets.length === 0 && <EmptyState title={t('builder2.noWidgets')} />}
        {widgets.map((w, index) => {
          const metric = metrics.find((m) => m.code === w.metric);
          const dims = metric?.dimensions ?? [];
          const dimOptions = [
            { value: '-', label: t('builder2.none') },
            { value: 'unit', label: t('dimensions.unit') },
            ...dims.map((d) => ({
              value: d.code,
              label: t(`dimensions.${d.code}` as 'dimensions.status', { defaultValue: d.code }),
            })),
          ];
          return (
            <div
              key={w.id}
              className="grid gap-2 rounded-lg border bg-card p-3 md:grid-cols-6"
              data-testid="builder-widget"
            >
              <FormField label={t('builder2.type')}>
                <Select
                  value={w.type}
                  onValueChange={(type) =>
                    update(index, {
                      type,
                      metric: type === 'list' || type === 'scorecard' || type === 'brief' ? null : w.metric,
                      list: type === 'list' ? w.list : null,
                    })
                  }
                  options={(catalogue.data?.widgetTypes ?? [])
                    .filter((type) => type !== 'bubble')
                    .map((type) => ({ value: type, label: t(`widgetTypes.${type}` as 'widgetTypes.tile') }))}
                />
              </FormField>
              {w.type === 'list' ? (
                <FormField label={t('builder2.list')} className="md:col-span-2">
                  <Select
                    value={w.list ?? undefined}
                    onValueChange={(list) => update(index, { list })}
                    options={listOptions}
                    placeholder={t('builder2.chooseList')}
                  />
                </FormField>
              ) : w.type === 'scorecard' || w.type === 'brief' ? (
                <div className="md:col-span-2" />
              ) : (
                <FormField label={t('builder2.metric')} className="md:col-span-2">
                  <Select
                    value={w.metric ?? undefined}
                    onValueChange={(m) =>
                      update(index, { metric: m, dimension: null, series: null, filters: null })
                    }
                    options={metricOptions}
                    placeholder={t('builder2.chooseMetric')}
                  />
                </FormField>
              )}
              {['bar', 'table', 'heatmap'].includes(w.type) && (
                <>
                  <FormField label={t('builder2.dimension')}>
                    <Select
                      value={w.dimension ?? '-'}
                      onValueChange={(d) => update(index, { dimension: d === '-' ? null : d })}
                      options={dimOptions}
                    />
                  </FormField>
                  <FormField label={t('builder2.series')}>
                    <Select
                      value={w.series ?? '-'}
                      onValueChange={(d) => update(index, { series: d === '-' ? null : d })}
                      options={dimOptions}
                    />
                  </FormField>
                </>
              )}
              {!['bar', 'table', 'heatmap'].includes(w.type) && <div className="md:col-span-2" />}
              <FormField label={t('builder2.width')}>
                <Select
                  value={String(w.w)}
                  onValueChange={(v) => update(index, { w: Number(v) })}
                  options={widths.map((n) => ({ value: String(n), label: `${n}/12` }))}
                />
              </FormField>
              <FormField label={t('builder2.titleEn')} className="md:col-span-2">
                <Input
                  value={w.titleEn ?? ''}
                  onChange={(e) => update(index, { titleEn: e.target.value || null })}
                />
              </FormField>
              <FormField label={t('builder2.titleAr')} className="md:col-span-2">
                <Input
                  dir="rtl"
                  value={w.titleAr ?? ''}
                  onChange={(e) => update(index, { titleAr: e.target.value || null })}
                />
              </FormField>
              <div className="flex items-end gap-1 md:col-span-2">
                <Button
                  size="sm"
                  variant="ghost"
                  aria-label={t('builder2.moveUp')}
                  disabled={index === 0}
                  onClick={() => setWidgets((ws) => moveItem(ws, index, index - 1))}
                >
                  <ArrowUp className="size-4" aria-hidden />
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  aria-label={t('builder2.moveDown')}
                  disabled={index === widgets.length - 1}
                  onClick={() => setWidgets((ws) => moveItem(ws, index, index + 1))}
                >
                  <ArrowDown className="size-4" aria-hidden />
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  aria-label={t('builder2.remove')}
                  onClick={() => setWidgets((ws) => ws.filter((_, i) => i !== index))}
                >
                  <Trash2 className="size-4" aria-hidden />
                </Button>
              </div>
            </div>
          );
        })}
        <Button
          variant="outline"
          className="self-start"
          data-testid="builder-add-widget"
          onClick={() =>
            setWidgets((ws) => [
              ...ws,
              {
                id: newId(ws),
                type: 'tile',
                metric: metrics[0]?.code ?? null,
                x: 0,
                y: 0,
                w: 3,
                h: 2,
              },
            ])
          }
        >
          <Plus className="size-4" aria-hidden />
          {t('builder2.addWidget')}
        </Button>
      </div>
      {level === 'role' && manage && (
        <div className="flex flex-col gap-2 rounded-lg border bg-card p-4" data-testid="builder-roles">
          <h2 className="font-semibold">{t('builder2.roles')}</h2>
          <p className="text-sm text-muted-foreground">{t('builder2.rolesHint')}</p>
          <div className="grid max-h-64 gap-1 overflow-y-auto sm:grid-cols-2">
            {roleOptions.map((role) => (
              <Checkbox
                key={role.value}
                label={role.label}
                checked={roles.includes(role.value)}
                onChange={(e) => {
                  const checked = e.target.checked;
                  setRolesState((rs) => (checked ? [...rs, role.value] : rs.filter((r) => r !== role.value)));
                }}
              />
            ))}
          </div>
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          disabled={save.isPending || !(titleAr || titleEn)}
          onClick={() => submit()}
          data-testid="builder-save"
        >
          {t('builder2.saveDraft')}
        </Button>
        <Button
          disabled={save.isPending || publish.isPending || !(titleAr || titleEn)}
          data-testid="builder-publish"
          onClick={() =>
            submit((id) =>
              publish.mutate(id, {
                onSuccess: () => {
                  if (level === 'role' && manage)
                    setRoles.mutate({
                      id,
                      roles: roles.map((roleKey, sort) => ({ roleKey, sort, isDefault: false })),
                    });
                  toast({ title: t('builder2.published') });
                },
              }),
            )
          }
        >
          <Send className="size-4" aria-hidden />
          {t('builder2.publish')}
        </Button>
        {savedId && (
          <Button asChild variant="ghost">
            <Link to="/dashboards/$key" params={{ key: savedId }}>
              <Eye className="size-4" aria-hidden />
              {t('builder2.open')}
            </Link>
          </Button>
        )}
      </div>
    </div>
  );
}

/** Settings → Dashboards (SPEC §0.4 dashboard builder, ADR 0143): arrange role defaults and personal dashboards from the widget catalogue. */
export function DashboardBuilderPage({ dashboardId }: { dashboardId?: string }) {
  const { t, i18n } = useTranslation('reporting');
  const dashboards = useDashboards();
  const [editing, setEditing] = useState<string | undefined>(dashboardId);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('builder2.title')}
        description={t('builder2.description')}
        actions={
          <Button onClick={() => setEditing('new')} data-testid="builder-new">
            <Plus className="size-4" aria-hidden />
            {t('builder2.new')}
          </Button>
        }
      />
      {editing ? (
        <Editor key={editing} dashboardId={editing === 'new' ? undefined : editing} />
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {(dashboards.data ?? [])
            .filter((d) => d.canEdit)
            .map((d) => (
              <li
                key={d.id}
                className="flex items-center justify-between gap-2 rounded-lg border bg-card p-3"
              >
                <div className="flex flex-col">
                  <span className="font-medium">{pickText(i18n.language, d.titleAr, d.titleEn)}</span>
                  <span className="flex gap-2 text-xs text-muted-foreground">
                    <StatusChip>{t(`builder2.levels.${d.level}` as 'builder2.levels.role')}</StatusChip>
                    {d.status !== 'published' && (
                      <StatusChip tone="warning">{t('dashboards.draft')}</StatusChip>
                    )}
                  </span>
                </div>
                <Button size="sm" variant="outline" onClick={() => setEditing(d.id)}>
                  {t('builder2.edit')}
                </Button>
              </li>
            ))}
        </ul>
      )}
    </div>
  );
}
