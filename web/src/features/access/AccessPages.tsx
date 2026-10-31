import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Download, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { useServerErrors } from '@/features/settings/common';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { UnitSelect, UserPicker, usePrincipalName, useSystems, type Principal } from './pickers';
import { accessKey, useCan } from './session';

type Assignment = components['schemas']['AssignmentDto'];
type Role = components['schemas']['RoleDto'];
type PrincipalKind = 'user' | 'unit' | 'everyone';

function useRoles() {
  return useQuery({
    queryKey: ['access', 'roles'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/roles', { signal })),
  });
}

function useRoleName() {
  const { i18n } = useTranslation();
  const systems = useSystems();
  return (role: {
    systemCode: string;
    nameAr?: string | null;
    nameEn?: string | null;
    roleNameAr?: string | null;
    roleNameEn?: string | null;
  }) => {
    const system = systems.data?.find((s) => s.code === role.systemCode);
    const name = pickText(i18n.language, role.nameAr ?? role.roleNameAr, role.nameEn ?? role.roleNameEn);
    return system ? `${pickText(i18n.language, system.nameAr, system.nameEn)} — ${name}` : name;
  };
}

/** Principal chooser shared by assignments and system access: one user, a unit (and below) or everyone. */
function PrincipalFields({
  kind,
  onKind,
  user,
  onUser,
  unit,
  onUnit,
  error,
}: {
  kind: PrincipalKind;
  onKind: (kind: PrincipalKind) => void;
  user: Principal | undefined;
  onUser: (user: Principal | undefined) => void;
  unit: string | undefined;
  onUnit: (unit: string | undefined) => void;
  error: string | undefined;
}) {
  const { t } = useTranslation('access');
  return (
    <>
      <FormField label={t('assignments.principalKind')}>
        <Select
          value={kind}
          onValueChange={(value) => onKind(value as PrincipalKind)}
          options={(['user', 'unit', 'everyone'] as const).map((value) => ({
            value,
            label: t(`principals.${value}`),
          }))}
        />
      </FormField>
      {kind === 'user' && (
        <FormField label={t('principals.user')} error={error} required>
          <UserPicker value={user} onChange={onUser} />
        </FormField>
      )}
      {kind === 'unit' && (
        <FormField label={t('principals.unit')} error={error} hint={t('principals.unitHint')} required>
          <UnitSelect value={unit} onChange={onUnit} />
        </FormField>
      )}
    </>
  );
}

// ---- Assignments ------------------------------------------------------------------------------------------

const assignmentsApi = getRouteApi('/app/settings/access/assignments');

/** Settings → Access → Assignments: who holds which role (user, unit and below, everyone), from when to when. */
export function AssignmentsPage() {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const search = assignmentsApi.useSearch();
  const navigate = assignmentsApi.useNavigate();
  const canManage = useCan('access.assignment.manage');
  const roles = useRoles();
  const roleName = useRoleName();
  const principalName = usePrincipalName();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [creating, setCreating] = useState(false);
  const [ending, setEnding] = useState<Assignment>();
  const assignments = useQuery({
    queryKey: ['access', 'assignments', search],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/assignments', {
          params: {
            query: { roleId: search.roleId, userId: search.userId, includeEnded: search.includeEnded },
          },
          signal,
        }),
      ),
  });
  const end = useMutation({
    mutationFn: async (assignment: Assignment) =>
      unwrap(
        await api.POST('/api/v1/access/assignments/{id}/end', {
          params: { path: { id: assignment.id } },
          body: { lastDay: null, version: assignment.version },
        }),
      ),
    onSuccess: () => {
      setEnding(undefined);
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      void queryClient.invalidateQueries({ queryKey: accessKey });
      toast({ title: t('assignments.ended') });
    },
    onError: (failure) => {
      setEnding(undefined);
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    },
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('assignments.title')}
        description={t('assignments.description')}
        actions={
          canManage ? (
            <Button onClick={() => setCreating(true)}>
              <Plus aria-hidden />
              {t('assignments.new')}
            </Button>
          ) : undefined
        }
      />
      <div className="flex flex-wrap items-end gap-4">
        <FormField label={t('assignments.role')} className="w-72">
          <Select
            value={search.roleId ?? '-'}
            onValueChange={(roleId) =>
              void navigate({ search: (s) => ({ ...s, roleId: roleId === '-' ? undefined : roleId }) })
            }
            options={[
              { value: '-', label: t('assignments.allRoles') },
              ...(roles.data ?? []).map((r) => ({ value: r.id, label: roleName(r) })),
            ]}
          />
        </FormField>
        <Checkbox
          label={t('assignments.includeEnded')}
          checked={search.includeEnded ?? false}
          onChange={(e) =>
            void navigate({ search: (s) => ({ ...s, includeEnded: e.target.checked || undefined }) })
          }
        />
        {search.userId && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => void navigate({ search: (s) => ({ ...s, userId: undefined }) })}
          >
            {t('assignments.clearUser')}
          </Button>
        )}
      </div>
      {assignments.isError && <StatusBanner tone="danger" title={errorMessage(tc, assignments.error)} />}
      {!assignments.data ? (
        <Skeleton className="h-64" />
      ) : assignments.data.length === 0 ? (
        <EmptyState title={t('assignments.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('assignments.role')}</TableHeaderCell>
              <TableHeaderCell>{t('assignments.principal')}</TableHeaderCell>
              <TableHeaderCell>{t('assignments.period')}</TableHeaderCell>
              <TableHeaderCell>{t('assignments.state')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('assignments.end')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {assignments.data.map((assignment) => (
              <TableRow key={assignment.id}>
                <TableCell className="font-medium">{roleName(assignment)}</TableCell>
                <TableCell>
                  <StatusChip tone="neutral" className="me-2">
                    {t(`principals.${assignment.principal.kind as PrincipalKind}`)}
                  </StatusChip>
                  {assignment.principal.kind !== 'everyone' && principalName(assignment.principal)}
                </TableCell>
                <TableCell>
                  {formatDate(assignment.validFrom, i18n.language)} —{' '}
                  {assignment.validTo ? formatDate(assignment.validTo, i18n.language) : t('assignments.open')}
                </TableCell>
                <TableCell>
                  <StatusChip tone={assignment.active ? 'success' : 'neutral'}>
                    {assignment.active ? t('assignments.active') : t('assignments.notActive')}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  {canManage && !assignment.validTo && (
                    <Button variant="ghost" size="sm" onClick={() => setEnding(assignment)}>
                      {t('assignments.end')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {creating && <AssignmentDialog roles={roles.data ?? []} onClose={() => setCreating(false)} />}
      <ConfirmDialog
        open={!!ending}
        onOpenChange={(open) => !open && setEnding(undefined)}
        title={t('assignments.endTitle')}
        description={t('assignments.endDescription')}
        confirmLabel={t('assignments.end')}
        destructive
        loading={end.isPending}
        onConfirm={() => ending && end.mutate(ending)}
      />
    </div>
  );
}

function AssignmentDialog({ roles, onClose }: { roles: Role[]; onClose: () => void }) {
  const { t } = useTranslation('access');
  const { t: tc } = useTranslation();
  const roleName = useRoleName();
  const queryClient = useQueryClient();
  const errors = useServerErrors();
  const [roleId, setRoleId] = useState<string>();
  const [kind, setKind] = useState<PrincipalKind>('user');
  const [user, setUser] = useState<Principal>();
  const [unit, setUnit] = useState<string>();
  const [validFrom, setValidFrom] = useState<string>();
  const [validTo, setValidTo] = useState<string>();
  const [note, setNote] = useState('');
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/access/assignments', {
          body: {
            roleId: roleId ?? null,
            principalKind: kind,
            principalId: kind === 'user' ? (user?.id ?? null) : kind === 'unit' ? (unit ?? null) : null,
            validFrom: validFrom ?? null,
            validTo: validTo ?? null,
            note: note || null,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      void queryClient.invalidateQueries({ queryKey: accessKey });
      onClose();
    },
    onError: errors.fail,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('assignments.new')}
        description={t('assignments.newDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending} onClick={() => create.mutate()}>
              {t('assignments.assign')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField label={t('assignments.role')} error={errors.errorFor('roleId')} required>
            <Select
              value={roleId}
              onValueChange={setRoleId}
              placeholder={t('assignments.chooseRole')}
              options={roles.filter((r) => r.active).map((r) => ({ value: r.id, label: roleName(r) }))}
            />
          </FormField>
          <PrincipalFields
            kind={kind}
            onKind={setKind}
            user={user}
            onUser={setUser}
            unit={unit}
            onUnit={setUnit}
            error={errors.errorFor('principalId')}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('assignments.from')} error={errors.errorFor('validFrom')}>
              <DatePicker value={validFrom} onChange={setValidFrom} />
            </FormField>
            <FormField label={t('assignments.to')} error={errors.errorFor('validTo')}>
              <DatePicker value={validTo} onChange={setValidTo} />
            </FormField>
          </div>
          <FormField label={t('assignments.note')}>
            <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ---- System access ----------------------------------------------------------------------------------------

/** Settings → Access → System access (layer 1): who may open each module at all. */
export function SystemAccessPage() {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const canManage = useCan('access.assignment.manage');
  const systems = useSystems();
  const modules = (systems.data ?? []).filter((s) => s.kind === 'module');
  const [system, setSystem] = useState<string>();
  const selected = system ?? modules[0]?.code;
  const principalName = usePrincipalName();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const [kind, setKind] = useState<PrincipalKind>('everyone');
  const [user, setUser] = useState<Principal>();
  const [unit, setUnit] = useState<string>();
  const grants = useQuery({
    queryKey: ['access', 'system-access', selected],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/system-access', { params: { query: { system: selected } }, signal }),
      ),
    enabled: !!selected,
  });
  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['access'] });
    void queryClient.invalidateQueries({ queryKey: accessKey });
  };
  const grant = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/access/system-access', {
          body: {
            systemCode: selected ?? null,
            principalKind: kind,
            principalId: kind === 'user' ? (user?.id ?? null) : kind === 'unit' ? (unit ?? null) : null,
          },
        }),
      ),
    onSuccess: () => {
      errors.clear();
      setUser(undefined);
      done();
      toast({ title: t('systemAccess.granted') });
    },
    onError: errors.fail,
  });
  const revoke = useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/v1/access/system-access/{id}/revoke', { params: { path: { id } } })),
    onSuccess: done,
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('systemAccess.title')} description={t('systemAccess.description')} />
      <div className="max-w-xs">
        <Select
          aria-label={t('roles.system')}
          value={selected}
          onValueChange={setSystem}
          options={modules.map((s) => ({
            value: s.code,
            label: pickText(i18n.language, s.nameAr, s.nameEn),
          }))}
        />
      </div>
      {grants.data?.length === 0 ? (
        <EmptyState title={t('systemAccess.empty')} />
      ) : (
        <ul className="flex max-w-2xl flex-col divide-y rounded-lg border bg-card">
          {grants.data?.map((g) => (
            <li key={g.id} className="flex items-center justify-between gap-3 p-3">
              <span>
                <StatusChip tone="neutral" className="me-2">
                  {t(`principals.${g.principal.kind as PrincipalKind}`)}
                </StatusChip>
                {g.principal.kind !== 'everyone' && principalName(g.principal)}
              </span>
              {canManage && (
                <Button variant="ghost" size="sm" onClick={() => revoke.mutate(g.id)}>
                  {t('systemAccess.revoke')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {canManage && selected && (
        <form
          className="flex max-w-2xl flex-col gap-4 rounded-lg border bg-card p-5"
          onSubmit={(event) => {
            event.preventDefault();
            grant.mutate();
          }}
        >
          <h2 className="font-semibold">{t('systemAccess.grant')}</h2>
          <PrincipalFields
            kind={kind}
            onKind={setKind}
            user={user}
            onUser={setUser}
            unit={unit}
            onUnit={setUnit}
            error={errors.errorFor('principalId')}
          />
          <div>
            <Button type="submit" loading={grant.isPending}>
              {t('systemAccess.grant')}
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}

// ---- Bulk transfer ----------------------------------------------------------------------------------------

/** Settings → Access → Bulk transfer: move roles from people or a unit to someone else in one audited action. */
export function TransferPage() {
  const { t } = useTranslation('access');
  const { i18n } = useTranslation();
  const roles = useRoles();
  const roleName = useRoleName();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [fromKind, setFromKind] = useState<'users' | 'unit'>('users');
  const [fromUsers, setFromUsers] = useState<Principal[]>([]);
  const [fromUnit, setFromUnit] = useState<string>();
  const [toKind, setToKind] = useState<'user' | 'unit'>('user');
  const [toUser, setToUser] = useState<Principal>();
  const [toUnit, setToUnit] = useState<string>();
  const [effectiveDate, setEffectiveDate] = useState<string>();
  const [reason, setReason] = useState('');
  const history = useQuery({
    queryKey: ['access', 'transfers'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/transfers', { signal })),
  });
  const transfer = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/access/transfers', {
          body: {
            roleIds,
            fromUserIds: fromKind === 'users' ? fromUsers.flatMap((u) => (u.id ? [u.id] : [])) : null,
            fromUnitId: fromKind === 'unit' ? (fromUnit ?? null) : null,
            toUserId: toKind === 'user' ? (toUser?.id ?? null) : null,
            toUnitId: toKind === 'unit' ? (toUnit ?? null) : null,
            effectiveDate: effectiveDate ?? null,
            reason: reason || null,
          },
        }),
      ),
    onSuccess: (result) => {
      errors.clear();
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      toast({ title: t('transfer.done', { count: result.movedCount }) });
      setRoleIds([]);
      setFromUsers([]);
      setReason('');
    },
    onError: errors.fail,
  });
  const grouped = useMemo(() => roles.data ?? [], [roles.data]);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('transfer.title')} description={t('transfer.description')} />
      <form
        className="flex max-w-3xl flex-col gap-5 rounded-lg border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          transfer.mutate();
        }}
      >
        <fieldset className="flex flex-col gap-2">
          <legend className="pb-2 text-sm font-medium">{t('transfer.roles')}</legend>
          {errors.errorFor('roleIds') && <p className="text-sm text-danger">{errors.errorFor('roleIds')}</p>}
          <div className="grid max-h-56 gap-2 overflow-y-auto sm:grid-cols-2">
            {grouped.map((role) => (
              <Checkbox
                key={role.id}
                label={roleName(role)}
                checked={roleIds.includes(role.id)}
                onChange={(e) =>
                  setRoleIds((ids) =>
                    e.target.checked ? [...ids, role.id] : ids.filter((id) => id !== role.id),
                  )
                }
              />
            ))}
          </div>
        </fieldset>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-3">
            <FormField
              label={t('transfer.from')}
              error={errors.errorFor('fromUserIds') ?? errors.errorFor('fromUnitId')}
            >
              <Select
                value={fromKind}
                onValueChange={(value) => setFromKind(value as 'users' | 'unit')}
                options={[
                  { value: 'users', label: t('transfer.fromUsers') },
                  { value: 'unit', label: t('transfer.fromUnit') },
                ]}
              />
            </FormField>
            {fromKind === 'users' ? (
              <>
                <UserPicker
                  value={undefined}
                  onChange={(u) =>
                    u && setFromUsers((list) => (list.some((x) => x.id === u.id) ? list : [...list, u]))
                  }
                />
                <ul className="flex flex-wrap gap-2">
                  {fromUsers.map((u) => (
                    <li key={u.id}>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => setFromUsers((list) => list.filter((x) => x.id !== u.id))}
                      >
                        {pickText(i18n.language, u.nameAr, u.nameEn) || u.detail} ×
                      </Button>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <UnitSelect value={fromUnit} onChange={setFromUnit} />
            )}
          </div>
          <div className="flex flex-col gap-3">
            <FormField
              label={t('transfer.to')}
              error={errors.errorFor('toUserId') ?? errors.errorFor('principalId')}
            >
              <Select
                value={toKind}
                onValueChange={(value) => setToKind(value as 'user' | 'unit')}
                options={[
                  { value: 'user', label: t('principals.user') },
                  { value: 'unit', label: t('principals.unit') },
                ]}
              />
            </FormField>
            {toKind === 'user' ? (
              <UserPicker value={toUser} onChange={setToUser} />
            ) : (
              <UnitSelect value={toUnit} onChange={setToUnit} />
            )}
          </div>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('transfer.effectiveDate')}>
            <DatePicker value={effectiveDate} onChange={setEffectiveDate} />
          </FormField>
        </div>
        <FormField label={t('transfer.reason')} error={errors.errorFor('reason')} required>
          <Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        <p className="text-xs text-muted-foreground">{t('transfer.pendingHint')}</p>
        <div>
          <Button type="submit" loading={transfer.isPending}>
            {t('transfer.submit')}
          </Button>
        </div>
      </form>
      <section className="flex flex-col gap-3">
        <h2 className="font-semibold">{t('transfer.history')}</h2>
        {history.data?.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('transfer.noHistory')}</p>
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('transfer.when')}</TableHeaderCell>
                <TableHeaderCell>{t('transfer.roles')}</TableHeaderCell>
                <TableHeaderCell>{t('transfer.moved')}</TableHeaderCell>
                <TableHeaderCell>{t('transfer.reason')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {history.data?.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{formatDateTime(item.createdAt, i18n.language)}</TableCell>
                  <TableCell dir="ltr" className="font-mono text-xs">
                    {item.roleKeys}
                  </TableCell>
                  <TableCell>{item.movedCount}</TableCell>
                  <TableCell>{item.reason}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </section>
    </div>
  );
}

// ---- Access review ----------------------------------------------------------------------------------------

/** Settings → Access → Access review: who can see / do what in each system, exportable to Excel (SPEC §0.2). */
export function ReviewPage() {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const systems = useSystems();
  const [system, setSystem] = useState<string>();
  const [unitId, setUnitId] = useState<string>();
  const [text, setText] = useState('');
  const rows = useQuery({
    queryKey: ['access', 'review', system, unitId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/access/review', { params: { query: { system, unitId } }, signal })),
  });
  const filtered = (rows.data ?? []).filter((row) => {
    const term = text.trim().toLowerCase();
    return (
      !term ||
      `${row.userName} ${row.employeeNameAr ?? ''} ${row.employeeNameEn ?? ''}`.toLowerCase().includes(term)
    );
  });
  const exportUrl = (lang: 'ar' | 'en') => {
    const query = new URLSearchParams({ lang });
    if (system) query.set('system', system);
    if (unitId) query.set('unitId', unitId);
    return `/api/v1/access/review/export?${query.toString()}`;
  };
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('review.title')}
        description={t('review.description')}
        actions={
          <>
            <Button variant="outline" asChild>
              <a href={exportUrl('ar')} download>
                <Download aria-hidden />
                {t('review.exportAr')}
              </a>
            </Button>
            <Button variant="outline" asChild>
              <a href={exportUrl('en')} download>
                <Download aria-hidden />
                {t('review.exportEn')}
              </a>
            </Button>
          </>
        }
      />
      <div className="flex flex-wrap items-end gap-4">
        <FormField label={t('roles.system')} className="w-64">
          <Select
            value={system ?? '-'}
            onValueChange={(value) => setSystem(value === '-' ? undefined : value)}
            options={[
              { value: '-', label: t('review.allSystems') },
              ...(systems.data ?? []).map((s) => ({
                value: s.code,
                label: pickText(i18n.language, s.nameAr, s.nameEn),
              })),
            ]}
          />
        </FormField>
        <FormField label={t('principals.unit')} className="w-72">
          <UnitSelect allowEmpty value={unitId} onChange={setUnitId} />
        </FormField>
        <Input
          type="search"
          className="max-w-xs"
          placeholder={t('review.search')}
          aria-label={t('review.search')}
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
      </div>
      {rows.isError && <StatusBanner tone="danger" title={errorMessage(tc, rows.error)} />}
      {!rows.data ? (
        <Skeleton className="h-64" />
      ) : filtered.length === 0 ? (
        <EmptyState title={t('review.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('review.user')}</TableHeaderCell>
              <TableHeaderCell>{t('roles.system')}</TableHeaderCell>
              <TableHeaderCell>{t('assignments.role')}</TableHeaderCell>
              <TableHeaderCell>{t('review.source')}</TableHeaderCell>
              <TableHeaderCell>{t('review.permissions')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {filtered.slice(0, 300).map((row, index) => (
              <TableRow key={`${row.userId}-${row.roleCode}-${row.systemCode}-${index}`}>
                <TableCell>
                  <span className="block font-medium">
                    {pickText(i18n.language, row.employeeNameAr, row.employeeNameEn) || row.userName}
                  </span>
                  <span dir="ltr" className="block text-xs text-muted-foreground">
                    {row.userName}
                  </span>
                  {!row.userActive && <StatusChip tone="neutral">{t('review.inactiveAccount')}</StatusChip>}
                </TableCell>
                <TableCell>
                  <span dir="ltr">{row.systemCode}</span>
                  {!row.systemAccess && (
                    <StatusChip tone="warning" className="ms-2">
                      {t('review.noSystemAccess')}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell>{pickText(i18n.language, row.roleNameAr, row.roleNameEn)}</TableCell>
                <TableCell>{t(`principals.${row.source as PrincipalKind}`)}</TableCell>
                <TableCell className="text-xs">
                  {row.permissions.map((p) => (
                    <span key={p.permission} className="me-2 inline-block" dir="ltr">
                      {p.permission}{' '}
                      <span className="text-muted-foreground">({t(`scopes.${p.scope as 'all'}`)})</span>
                    </span>
                  ))}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
