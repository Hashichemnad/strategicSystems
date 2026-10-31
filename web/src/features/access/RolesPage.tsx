import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Lock, Plus } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { useServerErrors } from '@/features/settings/common';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  useToast,
} from '@/ui';
import { useSystems } from './pickers';
import { accessKey, useCan } from './session';

type Matrix = components['schemas']['PermissionMatrixDto'];
type Role = components['schemas']['RoleDto'];

export const scopeCodes = ['own', 'team', 'unit', 'unit_and_below', 'all'] as const;
const routeApi = getRouteApi('/app/settings/access/roles');

/** Settings → Access → Roles & permissions: the permission matrix of one system (roles × permissions × scope). */
export function RolesPage() {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const search = routeApi.useSearch();
  const navigate = routeApi.useNavigate();
  const systems = useSystems();
  const system = search.system ?? systems.data?.[0]?.code;
  const matrix = useQuery({
    queryKey: ['access', 'matrix', system],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/matrix', {
          params: { path: { code: system ?? '' } },
          signal,
        }),
      ),
    enabled: !!system,
  });
  const canManage = useCan('access.role.manage');
  const [creating, setCreating] = useState(false);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('roles.title')}
        description={t('roles.description')}
        actions={
          canManage && system ? (
            <Button variant="outline" onClick={() => setCreating(true)}>
              <Plus aria-hidden />
              {t('roles.new')}
            </Button>
          ) : undefined
        }
      />
      <div className="max-w-xs">
        <Select
          aria-label={t('roles.system')}
          value={system}
          onValueChange={(code) => void navigate({ search: { system: code } })}
          options={(systems.data ?? []).map((s) => ({
            value: s.code,
            label: pickText(i18n.language, s.nameAr, s.nameEn),
          }))}
        />
      </div>
      {matrix.isError && <StatusBanner tone="danger" title={errorMessage(tc, matrix.error)} />}
      {!matrix.data ? (
        <Skeleton className="h-96" />
      ) : (
        <Tabs defaultValue="roles">
          <TabsList>
            <TabsTrigger value="roles">{t('roles.systemRoles')}</TabsTrigger>
            <TabsTrigger value="record">{t('roles.recordRoles')}</TabsTrigger>
          </TabsList>
          <TabsContent value="roles">
            <RoleMatrix matrix={matrix.data} canManage={canManage} />
          </TabsContent>
          <TabsContent value="record">
            <RecordRoleMatrix matrix={matrix.data} canManage={canManage} />
          </TabsContent>
        </Tabs>
      )}
      {creating && system && <CreateRoleDialog system={system} onClose={() => setCreating(false)} />}
    </div>
  );
}

function RoleMatrix({ matrix, canManage }: { matrix: Matrix; canManage: boolean }) {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const initial = useMemo(
    () =>
      Object.fromEntries(
        matrix.roles.map((role) => [
          role.id,
          Object.fromEntries(role.permissions.map((p) => [p.permission, p.scope])),
        ]),
      ),
    [matrix.roles],
  );
  const [draft, setDraft] = useState<Record<string, Record<string, string>>>(initial);
  useEffect(() => setDraft(initial), [initial]);
  const dirty = matrix.roles.filter(
    (role) => JSON.stringify(draft[role.id]) !== JSON.stringify(initial[role.id]),
  );
  const save = useMutation({
    mutationFn: async () => {
      for (const role of dirty)
        unwrap(
          await api.PUT('/api/v1/access/roles/{id}/permissions', {
            params: { path: { id: role.id } },
            body: {
              permissions: Object.entries(draft[role.id] ?? {}).map(([permission, scope]) => ({
                permission,
                scope,
              })),
              version: role.version,
            },
          }),
        );
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      void queryClient.invalidateQueries({ queryKey: accessKey });
      toast({ title: t('saved') });
    },
    onError: (failure) => {
      void queryClient.invalidateQueries({ queryKey: ['access', 'matrix'] });
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
  });
  const set = (role: Role, permission: string, scope: string) =>
    setDraft((current) => {
      const next = Object.fromEntries(
        Object.entries(current[role.id] ?? {}).filter(([key]) => key !== permission),
      );
      if (scope !== '-') next[permission] = scope;
      return { ...current, [role.id]: next };
    });
  return (
    <div className="flex flex-col gap-4">
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full min-w-max text-sm">
          <caption className="sr-only">{t('roles.matrixCaption')}</caption>
          <thead className="bg-muted/60">
            <tr>
              <th scope="col" className="sticky start-0 bg-muted p-3 text-start font-semibold">
                {t('roles.permission')}
              </th>
              {matrix.roles.map((role) => (
                <th key={role.id} scope="col" className="p-3 text-start font-semibold">
                  <span className="flex items-center gap-1">
                    {role.locked && <Lock className="size-3.5" aria-label={t('roles.locked')} />}
                    {pickText(i18n.language, role.nameAr, role.nameEn)}
                  </span>
                  <span className="flex flex-wrap gap-1 pt-1 text-xs font-normal">
                    <span dir="ltr" className="text-muted-foreground">
                      {role.code}
                    </span>
                    {role.requiresMfa && <StatusChip tone="info">{t('roles.mfa')}</StatusChip>}
                    {!role.active && <StatusChip tone="neutral">{t('roles.inactive')}</StatusChip>}
                    <StatusChip tone="neutral">
                      {t('roles.holders', { count: role.activeAssignments })}
                    </StatusChip>
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {matrix.permissions.map((permission) => (
              <tr key={permission.code} className="border-t">
                <th scope="row" className="sticky start-0 bg-card p-3 text-start font-normal">
                  <span className="block font-medium">
                    {pickText(i18n.language, permission.labelAr, permission.labelEn)}
                  </span>
                  <span dir="ltr" className="block text-xs text-muted-foreground">
                    {permission.code}
                  </span>
                </th>
                {matrix.roles.map((role) => {
                  const value = draft[role.id]?.[permission.code] ?? '-';
                  const options = [
                    { value: '-', label: t('roles.notGranted') },
                    ...(permission.scopable ? scopeCodes : (['all'] as const)).map((scope) => ({
                      value: scope,
                      label: permission.scopable ? t(`scopes.${scope}`) : t('roles.granted'),
                    })),
                  ];
                  return (
                    <td key={role.id} className="p-2">
                      <Select
                        aria-label={t('roles.cellLabel', {
                          role: pickText(i18n.language, role.nameAr, role.nameEn),
                          permission: pickText(i18n.language, permission.labelAr, permission.labelEn),
                        })}
                        value={value}
                        onValueChange={(scope) => set(role, permission.code, scope)}
                        options={options}
                        disabled={!canManage || role.locked}
                        className="min-w-40"
                      />
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {canManage && (
        <div className="flex items-center gap-3">
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={dirty.length === 0}>
            {t('roles.save')}
          </Button>
          {dirty.length > 0 && (
            <span className="text-sm text-muted-foreground">
              {t('roles.unsaved', { count: dirty.length })}
            </span>
          )}
        </div>
      )}
      <p className="text-xs text-muted-foreground">{t('scopes.hint')}</p>
    </div>
  );
}

function RecordRoleMatrix({ matrix, canManage }: { matrix: Matrix; canManage: boolean }) {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const save = useMutation({
    mutationFn: async (input: { id: string; permissions: string[]; version: number }) =>
      unwrap(
        await api.PUT('/api/v1/access/record-roles/{id}/permissions', {
          params: { path: { id: input.id } },
          body: { permissions: input.permissions, version: input.version },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access', 'matrix'] });
      toast({ title: t('saved') });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  if (matrix.recordRoles.length === 0)
    return <p className="text-sm text-muted-foreground">{t('roles.noRecordRoles')}</p>;
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted-foreground">{t('roles.recordRolesHint')}</p>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full min-w-max text-sm">
          <thead className="bg-muted/60">
            <tr>
              <th scope="col" className="p-3 text-start font-semibold">
                {t('roles.permission')}
              </th>
              {matrix.recordRoles.map((role) => (
                <th key={role.id} scope="col" className="p-3 text-start font-semibold">
                  {pickText(i18n.language, role.nameAr, role.nameEn)}
                  <span dir="ltr" className="block text-xs font-normal text-muted-foreground">
                    {role.resourceType} / {role.roleCode}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {matrix.permissions.map((permission) => (
              <tr key={permission.code} className="border-t">
                <th scope="row" className="p-3 text-start font-normal">
                  {pickText(i18n.language, permission.labelAr, permission.labelEn)}
                </th>
                {matrix.recordRoles.map((role) => {
                  const granted = role.permissions.includes(permission.code);
                  return (
                    <td key={role.id} className="p-3">
                      <Checkbox
                        label={<span className="sr-only">{permission.code}</span>}
                        checked={granted}
                        disabled={!canManage || save.isPending}
                        onChange={(e) =>
                          save.mutate({
                            id: role.id,
                            version: role.version,
                            permissions: e.target.checked
                              ? [...role.permissions, permission.code]
                              : role.permissions.filter((p) => p !== permission.code),
                          })
                        }
                      />
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CreateRoleDialog({ system, onClose }: { system: string; onClose: () => void }) {
  const { t } = useTranslation('access');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const errors = useServerErrors();
  const [code, setCode] = useState('');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [requiresMfa, setRequiresMfa] = useState(false);
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/access/roles', {
          body: {
            systemCode: system,
            code,
            nameAr: nameAr || null,
            nameEn: nameEn || null,
            descriptionAr: null,
            descriptionEn: null,
            requiresMfa,
            active: true,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      onClose();
    },
    onError: errors.fail,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('roles.new')}
        description={t('roles.newDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending} onClick={() => create.mutate()}>
              {tc('actions.create')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField
            label={t('roles.code')}
            error={errors.errorFor('code')}
            hint={t('roles.codeHint')}
            required
          >
            <Input dir="ltr" value={code} onChange={(e) => setCode(e.target.value)} />
          </FormField>
          <FormField label={t('roles.nameAr')} error={errors.errorFor('nameAr')}>
            <Input dir="rtl" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('roles.nameEn')} error={errors.errorFor('nameEn')}>
            <Input dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <Checkbox
            label={t('roles.requiresMfa')}
            checked={requiresMfa}
            onChange={(e) => setRequiresMfa(e.target.checked)}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}
