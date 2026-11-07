import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, getRouteApi } from '@tanstack/react-router';
import { Settings2, ShieldHalf, UserPlus, UsersRound } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useServerErrors } from '@/features/settings/common';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
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
import {
  useMemberMutations,
  useMemberRoles,
  useMemberSystems,
  useSystemMembers,
  type Member,
  type MemberList,
  type MemberPrincipalKind,
} from './members';
import { UnitSelect, UserPicker, usePrincipalName, type Principal } from './pickers';
import { useCan } from './session';

const systemMembersApi = getRouteApi('/app/settings/members/$system');

/** Settings → Members: the systems whose members the user manages (platform administrator: every system; ADR 0167). */
export function MembersHubPage() {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const systems = useMemberSystems();
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('members.hubTitle')} description={t('members.hubDescription')} />
      {systems.isError && <StatusBanner tone="danger" title={errorMessage(tc, systems.error)} />}
      {!systems.data ? (
        <Skeleton className="h-48" />
      ) : systems.data.length === 0 ? (
        <EmptyState title={t('members.hubEmpty')} icon={UsersRound} />
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {systems.data.map((system) => (
            <li key={system.code}>
              <Link
                to="/settings/members/$system"
                params={{ system: system.code }}
                className="flex items-center gap-3 rounded-lg border bg-card p-4 transition-colors hover:border-primary"
              >
                <UsersRound className="size-5 shrink-0 text-primary" aria-hidden />
                <span className="font-medium">{pickText(i18n.language, system.nameAr, system.nameEn)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** System → Settings → Members (SPEC-ADDENDUM §D1, docs/design/members-responsibilities.md §1). */
export function SystemMembersPage() {
  const { system } = systemMembersApi.useParams();
  const { t } = useTranslation('access');
  const { i18n } = useTranslation();
  const members = useSystemMembers(system);
  const canMatrix = useCan('access.role.read');
  const canConfigure = useCan('access.role.manage');
  const [configuring, setConfiguring] = useState(false);
  const name = members.data
    ? pickText(i18n.language, members.data.system.nameAr, members.data.system.nameEn)
    : undefined;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={name ? t('members.titleOf', { system: name }) : t('members.title')}
        description={t('members.description')}
        actions={
          <>
            {canConfigure && members.data && (
              <Button variant="outline" onClick={() => setConfiguring(true)}>
                <Settings2 aria-hidden />
                {t('members.configure')}
              </Button>
            )}
            {canMatrix && (
              <Link
                to="/settings/access/roles"
                search={{ system }}
                className="inline-flex items-center gap-2 rounded-md border px-3 py-2 text-sm hover:bg-muted"
              >
                <ShieldHalf className="size-4" aria-hidden />
                {t('members.advanced')}
              </Link>
            )}
          </>
        }
      />
      <SystemMembersView system={system} />
      {configuring && <MemberRolesDialog system={system} onClose={() => setConfiguring(false)} />}
    </div>
  );
}

/** The lists of one system: Administrators, Users and one per extra role, each with add / remove. */
export function SystemMembersView({ system }: { system: string }) {
  const { t } = useTranslation('access');
  const { t: tc } = useTranslation();
  const members = useSystemMembers(system);
  const [adding, setAdding] = useState<MemberList>();
  const [removing, setRemoving] = useState<{ list: MemberList; member: Member }>();
  if (members.isError) {
    const notFound = members.error instanceof ApiError && members.error.status === 404;
    return (
      <StatusBanner
        tone="danger"
        title={notFound ? t('members.notFound') : errorMessage(tc, members.error)}
      />
    );
  }
  if (!members.data) return <Skeleton className="h-96" />;
  return (
    <div className="flex flex-col gap-8" data-testid="system-members">
      {members.data.lists.map((list) => (
        <MemberListSection
          key={list.key}
          list={list}
          onAdd={() => setAdding(list)}
          onRemove={(member) => setRemoving({ list, member })}
        />
      ))}
      {adding && <AddMemberDialog system={system} list={adding} onClose={() => setAdding(undefined)} />}
      {removing && (
        <RemoveMemberDialog
          system={system}
          list={removing.list}
          member={removing.member}
          onClose={() => setRemoving(undefined)}
        />
      )}
    </div>
  );
}

function useListTitle() {
  const { t } = useTranslation('access');
  const { i18n } = useTranslation();
  return (list: MemberList) => {
    if (list.kind === 'administrators') return t('members.lists.administrators');
    if (list.kind === 'users') return t('members.lists.users');
    const role = list.roles[0];
    return role ? pickText(i18n.language, role.nameAr, role.nameEn) || role.code : list.key;
  };
}

function MemberListSection({
  list,
  onAdd,
  onRemove,
}: {
  list: MemberList;
  onAdd: () => void;
  onRemove: (member: Member) => void;
}) {
  const { t } = useTranslation('access');
  const { i18n } = useTranslation();
  const title = useListTitle()(list);
  const principalName = usePrincipalName();
  const roles = new Intl.ListFormat(i18n.language).format(
    list.roles.map((r) => pickText(i18n.language, r.nameAr, r.nameEn) || r.code),
  );
  const headingId = `members-${list.key.replace(/[^a-z0-9-]/gi, '-')}`;
  const hint =
    list.kind === 'extra'
      ? t('members.hints.extra')
      : t(`members.hints.${list.kind as 'administrators' | 'users'}`, {
          roles: roles || t('members.noRole'),
        });
  return (
    <section
      aria-labelledby={headingId}
      className="flex flex-col gap-3"
      data-testid={`members-list-${list.key}`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 id={headingId} className="text-lg font-semibold">
            {title}
          </h2>
          <p className="text-sm text-muted-foreground">{hint}</p>
        </div>
        {list.canManage ? (
          <Button size="sm" onClick={onAdd} aria-label={t('members.addTo', { list: title })}>
            <UserPlus aria-hidden />
            {t('members.add')}
          </Button>
        ) : (
          <StatusChip tone="neutral">{t('members.platformOnly')}</StatusChip>
        )}
      </div>
      {list.members.length === 0 ? (
        <EmptyState title={t('members.empty')} icon={UsersRound} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('members.columns.member')}</TableHeaderCell>
              <TableHeaderCell>{t('members.columns.period')}</TableHeaderCell>
              <TableHeaderCell>{t('members.columns.state')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('members.remove')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.members.map((member) => {
              const name =
                member.principal.kind === 'everyone'
                  ? t('members.kinds.everyone')
                  : principalName(member.principal);
              return (
                <TableRow key={member.id}>
                  <TableCell className="font-medium">
                    <StatusChip tone="neutral" className="me-2">
                      {t(`members.kinds.${member.principal.kind as MemberPrincipalKind}`)}
                    </StatusChip>
                    {member.principal.kind !== 'everyone' && name}
                  </TableCell>
                  <TableCell>
                    {member.validFrom
                      ? `${formatDate(member.validFrom, i18n.language)} — ${
                          member.validTo ? formatDate(member.validTo, i18n.language) : t('members.open')
                        }`
                      : t('members.accessOnly')}
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={member.active ? 'success' : 'neutral'}>
                      {member.active ? t('members.active') : t('members.notActive')}
                    </StatusChip>
                  </TableCell>
                  <TableCell className="text-end">
                    {list.canManage && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => onRemove(member)}
                        aria-label={t('members.removeNamed', { name })}
                      >
                        {t('members.remove')}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
    </section>
  );
}

/** Add a person (search), a unit with its sub-units (unit picker) or all employees, with optional dates and note. */
export function AddMemberDialog({
  system,
  list,
  onClose,
}: {
  system: string;
  list: MemberList;
  onClose: () => void;
}) {
  const { t } = useTranslation('access');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const title = useListTitle()(list);
  const errors = useServerErrors();
  const { add } = useMemberMutations(system);
  const peopleOnly = list.kind === 'administrators';
  const [kind, setKind] = useState<MemberPrincipalKind>('user');
  const [user, setUser] = useState<Principal>();
  const [unit, setUnit] = useState<string>();
  const [validFrom, setValidFrom] = useState<string>();
  const [validTo, setValidTo] = useState<string>();
  const [note, setNote] = useState('');
  const submit = () =>
    add.mutate(
      {
        list: list.key,
        principalKind: kind,
        principalId: kind === 'user' ? (user?.id ?? null) : kind === 'unit' ? (unit ?? null) : null,
        validFrom: validFrom ?? null,
        validTo: validTo ?? null,
        note: note.trim() || null,
      },
      {
        onSuccess: () => {
          toast({ title: t('members.added') });
          onClose();
        },
        onError: (failure) => {
          if (failure instanceof ApiError && !failure.problem.errors?.length)
            toast({ tone: 'error', title: errorMessage(tc, failure) });
          else errors.fail(failure);
        },
      },
    );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('members.addTitle', { list: title })}
        description={t('members.addDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={add.isPending} onClick={submit}>
              {t('members.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {!peopleOnly && (
            <FormField label={t('members.kind')} error={errors.errorFor('principalKind')}>
              <Select
                value={kind}
                onValueChange={(value) => setKind(value as MemberPrincipalKind)}
                options={(['user', 'unit', 'everyone'] as const).map((value) => ({
                  value,
                  label: t(`members.kinds.${value}`),
                }))}
              />
            </FormField>
          )}
          {kind === 'user' && (
            <FormField label={t('members.person')} error={errors.errorFor('principalId')} required>
              <UserPicker value={user} onChange={setUser} membersOf={system} />
            </FormField>
          )}
          {kind === 'unit' && (
            <FormField
              label={t('members.unit')}
              error={errors.errorFor('principalId')}
              hint={t('principals.unitHint')}
              required
            >
              <UnitSelect value={unit} onChange={setUnit} />
            </FormField>
          )}
          {kind === 'everyone' && <StatusBanner tone="info" title={t('members.everyoneHint')} />}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('members.from')} error={errors.errorFor('validFrom')}>
              <DatePicker value={validFrom} onChange={setValidFrom} />
            </FormField>
            <FormField label={t('members.to')} error={errors.errorFor('validTo')}>
              <DatePicker value={validTo} onChange={setValidTo} />
            </FormField>
          </div>
          <FormField label={t('members.note')} error={errors.errorFor('note')}>
            <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Confirm removal (access through the list ends now, nothing is deleted) with an optional reason. */
function RemoveMemberDialog({
  system,
  list,
  member,
  onClose,
}: {
  system: string;
  list: MemberList;
  member: Member;
  onClose: () => void;
}) {
  const { t } = useTranslation('access');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const listTitle = useListTitle()(list);
  const principalName = usePrincipalName();
  const { remove } = useMemberMutations(system);
  const [reason, setReason] = useState('');
  const name =
    member.principal.kind === 'everyone' ? t('members.kinds.everyone') : principalName(member.principal);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('members.removeTitle', { name, list: listTitle })}
        description={t('members.removeDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              loading={remove.isPending}
              onClick={() =>
                remove.mutate(
                  { id: member.id, reason: reason.trim() || null },
                  {
                    onSuccess: () => {
                      toast({ title: t('members.removed') });
                      onClose();
                    },
                    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                  },
                )
              }
            >
              {t('members.remove')}
            </Button>
          </>
        }
      >
        <FormField label={t('members.reason')}>
          <Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

type Slot = 'none' | 'administrators' | 'users' | 'extra';

/** Platform administrator: which of the system's roles each list means (defaults from code, ADR 0166). */
function MemberRolesDialog({ system, onClose }: { system: string; onClose: () => void }) {
  const { t } = useTranslation('access');
  const { t: tc, i18n } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const errors = useServerErrors();
  const config = useMemberRoles(system, true);
  const roles = useQuery({
    queryKey: ['access', 'roles', system],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/access/roles', { params: { query: { system } }, signal })),
  });
  const [slots, setSlots] = useState<Record<string, Slot>>();
  const current: Record<string, Slot> =
    slots ??
    Object.fromEntries([
      ...(config.data?.administratorRoles ?? []).map((code) => [code, 'administrators'] as const),
      ...(config.data?.userRoles ?? []).map((code) => [code, 'users'] as const),
      ...(config.data?.extraRoles ?? []).map((code) => [code, 'extra'] as const),
    ]);
  const pick = (slot: Slot) =>
    Object.entries(current)
      .filter(([, value]) => value === slot)
      .map(([code]) => code);
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/v1/access/systems/{code}/member-roles', {
          params: { path: { code: system } },
          body: {
            administratorRoles: pick('administrators'),
            userRoles: pick('users'),
            extraRoles: pick('extra'),
            version: config.data?.isDefault ? null : (config.data?.version ?? null),
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['access'] });
      toast({ title: t('members.config.saved') });
      onClose();
    },
    onError: errors.fail,
  });
  const defaults = () =>
    config.data &&
    setSlots(
      Object.fromEntries([
        ...config.data.defaults.administratorRoles.map((code) => [code, 'administrators'] as const),
        ...config.data.defaults.userRoles.map((code) => [code, 'users'] as const),
        ...config.data.defaults.extraRoles.map((code) => [code, 'extra'] as const),
      ]),
    );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('members.config.title')}
        description={t('members.config.description')}
        footer={
          <>
            <Button variant="ghost" onClick={defaults} disabled={!config.data}>
              {t('members.config.reset')}
            </Button>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={() => save.mutate()} disabled={!config.data}>
              {t('members.config.save')}
            </Button>
          </>
        }
      >
        {!config.data || !roles.data ? (
          <Skeleton className="h-48" />
        ) : (
          <div className="flex flex-col gap-3">
            {(errors.errorFor('administratorRoles') ??
              errors.errorFor('userRoles') ??
              errors.errorFor('extraRoles')) && (
              <StatusBanner
                tone="danger"
                title={
                  errors.errorFor('administratorRoles') ??
                  errors.errorFor('userRoles') ??
                  errors.errorFor('extraRoles')
                }
              />
            )}
            {roles.data
              .filter((role) => role.active)
              .map((role) => {
                const label = pickText(i18n.language, role.nameAr, role.nameEn) || role.code;
                return (
                  <div key={role.id} className="flex flex-wrap items-center justify-between gap-2">
                    <span className="text-sm">{label}</span>
                    <Select
                      className="w-56"
                      aria-label={t('members.config.listOf', { role: label })}
                      value={current[role.code] ?? 'none'}
                      onValueChange={(value) => setSlots({ ...current, [role.code]: value as Slot })}
                      options={(['none', 'administrators', 'users', 'extra'] as const).map((value) => ({
                        value,
                        label: t(`members.config.slots.${value}`),
                      }))}
                    />
                  </div>
                );
              })}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
