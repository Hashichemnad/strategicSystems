import { useMutation } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { Building2, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { unitsRoute } from '@/app/router';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  DatePicker,
  EmptyState,
  FormField,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  TreeView,
  useToast,
} from '@/ui';
import {
  todayIso,
  useInvalidateOrg,
  useLevels,
  useUnit,
  useUnitHistory,
  useUnitTree,
  type OrgTreeNode,
  type OrgUnit,
} from './api';
import { MoveUnitDialog, UnitDialog } from './UnitDialogs';

export function UnitsPage() {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const { unit: selectedId } = unitsRoute.useSearch();
  const navigate = useNavigate();
  const admin = useCan('org.unit.manage');
  const [includeInactive, setIncludeInactive] = useState(false);
  const tree = useUnitTree(undefined, includeInactive);
  const levels = useLevels();
  const [creating, setCreating] = useState<{ parent?: OrgTreeNode } | null>(null);

  const levelName = useMemo(() => {
    const map = new Map(
      levels.data?.map((level) => [level.code, pickText(i18n.language, level.nameAr, level.nameEn)]),
    );
    return (code: string) => map.get(code) ?? code;
  }, [levels.data, i18n.language]);

  const select = (id: string) => void navigate({ to: '/org/units', search: { unit: id } });
  const selectedNode = tree.data?.find((node) => node.id === selectedId);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('units.title')}
        description={t('units.description')}
        actions={
          admin && (
            <Button onClick={() => setCreating({})}>
              <Plus aria-hidden />
              {t('units.createRoot')}
            </Button>
          )
        }
      />
      <div className="grid gap-6 lg:grid-cols-[minmax(18rem,1fr)_2fr]">
        <div className="flex flex-col gap-3 rounded-lg border bg-card p-3">
          <Switch
            checked={includeInactive}
            onCheckedChange={setIncludeInactive}
            label={t('units.showInactive')}
            className="px-1"
          />
          {tree.isPending ? (
            <div className="flex flex-col gap-2">
              {Array.from({ length: 6 }, (_, i) => (
                <Skeleton key={i} className="h-7" />
              ))}
            </div>
          ) : tree.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, tree.error)} />
          ) : tree.data.length === 0 ? (
            <EmptyState title={t('units.empty')} icon={Building2} />
          ) : (
            <TreeView
              items={tree.data}
              label={t('units.treeLabel')}
              selectedId={selectedId}
              onSelect={(node) => select(node.id)}
              renderLabel={(node) => (
                <span className="flex min-w-0 flex-1 items-center justify-between gap-2">
                  <span className={node.active ? 'truncate' : 'truncate text-muted-foreground line-through'}>
                    {pickText(i18n.language, node.nameAr, node.nameEn)}
                  </span>
                  <span className="shrink-0 text-xs text-muted-foreground" dir="ltr">
                    {node.code}
                  </span>
                </span>
              )}
            />
          )}
        </div>
        {selectedId ? (
          <UnitDetails
            id={selectedId}
            admin={admin}
            levelName={levelName}
            onAddChild={() => setCreating({ parent: selectedNode })}
          />
        ) : (
          <EmptyState
            title={t('units.selectPrompt')}
            description={t('units.selectHint')}
            icon={Building2}
            className="rounded-lg border bg-card"
          />
        )}
      </div>
      {creating && (
        <UnitDialog
          open
          onOpenChange={(open) => !open && setCreating(null)}
          parent={creating.parent}
          onSaved={(unit) => {
            setCreating(null);
            select(unit.id);
          }}
        />
      )}
    </div>
  );
}

function UnitDetails({
  id,
  admin,
  levelName,
  onAddChild,
}: {
  id: string;
  admin: boolean;
  levelName: (code: string) => string;
  onAddChild: () => void;
}) {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const unit = useUnit(id);
  const history = useUnitHistory(id);
  const invalidate = useInvalidateOrg();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [moving, setMoving] = useState(false);
  const [toggling, setToggling] = useState(false);
  const [effective, setEffective] = useState<string | undefined>(todayIso());
  const toggle = useMutation({
    mutationFn: async (current: OrgUnit) =>
      unwrap(
        await api.POST(
          current.active ? '/api/v1/org/units/{id}/deactivate' : '/api/v1/org/units/{id}/activate',
          {
            params: { path: { id: current.id } },
            body: { effectiveDate: effective ?? null, version: current.version },
          },
        ),
      ),
    onSuccess: () => {
      setToggling(false);
      void invalidate();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  if (unit.isPending) return <Skeleton className="h-64 rounded-lg" />;
  if (unit.isError) return <StatusBanner tone="danger" title={errorMessage(tc, unit.error)} />;
  const data = unit.data;
  const name = (ref?: { nameAr?: string | null; nameEn?: string | null } | null) =>
    ref ? pickText(i18n.language, ref.nameAr, ref.nameEn) : '—';

  return (
    <div className="flex flex-col gap-4 rounded-lg border bg-card p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <nav aria-label={t('units.path')} className="text-xs text-muted-foreground">
            {data.path
              .slice(0, -1)
              .map((p) => name(p))
              .join(' › ')}
          </nav>
          <h2 className="text-xl font-semibold">{name(data)}</h2>
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <StatusChip tone={data.active ? 'success' : 'neutral'}>
              {data.active ? t('status.active') : t('status.inactive')}
            </StatusChip>
            <span className="text-muted-foreground">{levelName(data.levelCode)}</span>
            <span className="font-mono text-xs text-muted-foreground" dir="ltr">
              {data.code}
            </span>
          </div>
        </div>
        {admin && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={onAddChild} disabled={!data.active}>
              <Plus aria-hidden />
              {t('units.addChild')}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
              {tc('actions.edit')}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setMoving(true)}>
              {t('units.move')}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setToggling(true)}>
              {data.active ? t('units.deactivate') : t('units.activate')}
            </Button>
          </div>
        )}
      </div>
      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">{t('units.tabs.overview')}</TabsTrigger>
          <TabsTrigger value="history">{t('units.tabs.history')}</TabsTrigger>
        </TabsList>
        <TabsContent value="overview" className="pt-4">
          <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
            <Detail label={t('fields.nameAr')} value={<span lang="ar">{data.nameAr ?? '—'}</span>} />
            <Detail label={t('fields.nameEn')} value={<span lang="en">{data.nameEn ?? '—'}</span>} />
            <Detail
              label={t('fields.head')}
              value={
                data.head ? (
                  <Link
                    to="/org/employees/$employeeId"
                    params={{ employeeId: data.head.id }}
                    className="text-primary hover:underline"
                  >
                    {name(data.head)}
                  </Link>
                ) : (
                  '—'
                )
              }
            />
            <Detail label={t('fields.deputy')} value={name(data.deputy)} />
            <Detail label={t('fields.costCentre')} value={data.costCentre ?? '—'} />
            <Detail label={t('fields.effectiveFrom')} value={formatDate(data.effectiveFrom, i18n.language)} />
            <Detail
              label={t('fields.currentVersionFrom')}
              value={formatDate(data.currentVersionFrom, i18n.language)}
            />
            <Detail
              label={t('units.employees')}
              value={
                <Link
                  to="/org/employees"
                  search={{ page: 1, unitId: data.id }}
                  className="text-primary hover:underline"
                >
                  {t('units.viewEmployees')}
                </Link>
              }
            />
          </dl>
        </TabsContent>
        <TabsContent value="history" className="pt-4">
          <ol className="flex flex-col gap-3" aria-label={t('units.tabs.history')}>
            {history.data?.map((version) => (
              <li key={version.validFrom} className="rounded-md border p-3 text-sm">
                <p className="font-medium">
                  {formatDate(version.validFrom, i18n.language)} –{' '}
                  {version.validTo ? formatDate(version.validTo, i18n.language) : t('units.current')}
                </p>
                <p>
                  {name(version)}{' '}
                  <span className="text-muted-foreground">({levelName(version.levelCode)})</span>
                  {!version.active && <StatusChip className="ms-2">{t('status.inactive')}</StatusChip>}
                </p>
                <p className="text-xs text-muted-foreground">
                  {version.path.map((p) => name(p)).join(' › ')}
                </p>
                {version.head && (
                  <p className="text-xs">
                    {t('fields.head')}: {name(version.head)}
                  </p>
                )}
              </li>
            ))}
          </ol>
        </TabsContent>
      </Tabs>
      {editing && <UnitDialog open onOpenChange={setEditing} unit={data} onSaved={() => setEditing(false)} />}
      {moving && <MoveUnitDialog open onOpenChange={setMoving} unit={data} />}
      <ConfirmDialog
        open={toggling}
        onOpenChange={setToggling}
        title={data.active ? t('units.deactivateTitle') : t('units.activateTitle')}
        description={
          <span className="flex flex-col gap-3">
            <span>{data.active ? t('units.deactivateDescription') : t('units.activateDescription')}</span>
            <FormField label={t('fields.effectiveDate')}>
              <DatePicker value={effective} onChange={setEffective} />
            </FormField>
          </span>
        }
        confirmLabel={data.active ? t('units.deactivate') : t('units.activate')}
        destructive={data.active}
        loading={toggle.isPending}
        onConfirm={() => toggle.mutate(data)}
      />
    </div>
  );
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
