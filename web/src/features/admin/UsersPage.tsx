import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, useNavigate } from '@tanstack/react-router';
import { MoreHorizontal, Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { useCan } from '@/features/access/session';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';

type User = components['schemas']['UserDto'];
type UserAction =
  'activate' | 'deactivate' | 'unlock' | 'reset-mfa' | 'revoke-sessions' | 'send-password-reset';

const routeApi = getRouteApi('/app/admin/users');

export function UsersPage() {
  const { t } = useTranslation('admin');
  const { t: tc, i18n } = useTranslation();
  const search = routeApi.useSearch();
  const navigate = routeApi.useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [text, setText] = useState(search.search ?? '');
  const [creating, setCreating] = useState(false);
  const canSeeRoles = useCan('access.assignment.read');
  const navigateTo = useNavigate();
  const users = useQuery({
    queryKey: ['identity', 'users', search],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/identity/admin/users', {
          params: { query: { page: search.page, pageSize: 25, search: search.search } },
          signal,
        }),
      ),
    placeholderData: keepPreviousData,
  });
  useEffect(() => {
    const timer = window.setTimeout(() => {
      if ((search.search ?? '') !== text) void navigate({ search: { page: 1, search: text || undefined } });
    }, 300);
    return () => window.clearTimeout(timer);
  }, [text, search.search, navigate]);

  const act = useMutation({
    mutationFn: async ({ user, action }: { user: User; action: UserAction }) => {
      const path = { params: { path: { id: user.id } } };
      switch (action) {
        case 'activate':
          return unwrap(await api.POST('/api/v1/identity/admin/users/{id}/activate', path));
        case 'deactivate':
          return unwrap(await api.POST('/api/v1/identity/admin/users/{id}/deactivate', path));
        case 'unlock':
          return unwrap(await api.POST('/api/v1/identity/admin/users/{id}/unlock', path));
        case 'reset-mfa':
          return unwrap(await api.POST('/api/v1/identity/admin/users/{id}/reset-mfa', path));
        case 'revoke-sessions':
          return unwrap(await api.POST('/api/v1/identity/admin/users/{id}/revoke-sessions', path));
        case 'send-password-reset':
          unwrap(await api.POST('/api/v1/identity/admin/users/{id}/send-password-reset', path));
          return;
      }
    },
    onSuccess: (_, { action }) => {
      toast({ title: t(`users.done.${action}`) });
      void queryClient.invalidateQueries({ queryKey: ['identity', 'users'] });
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  const data = users.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('users.title')}
        description={t('users.description')}
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus aria-hidden />
            {t('users.create')}
          </Button>
        }
      />
      <Input
        type="search"
        className="max-w-xs"
        placeholder={t('users.searchPlaceholder')}
        aria-label={tc('actions.search')}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      {users.isError && <StatusBanner tone="danger" title={errorMessage(tc, users.error)} />}
      {data?.items.length === 0 ? (
        <EmptyState title={t('users.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('users.user')}</TableHeaderCell>
              <TableHeaderCell>{t('users.employee')}</TableHeaderCell>
              <TableHeaderCell>{t('users.state')}</TableHeaderCell>
              <TableHeaderCell>{t('users.signIn')}</TableHeaderCell>
              <TableHeaderCell>{t('users.lastSignIn')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data?.items.map((user) => (
              <TableRow key={user.id}>
                <TableCell>
                  <span className="block font-medium" dir="ltr">
                    {user.userName}
                  </span>
                </TableCell>
                <TableCell>
                  {user.employee ? pickText(i18n.language, user.employee.nameAr, user.employee.nameEn) : '—'}
                </TableCell>
                <TableCell className="flex flex-wrap gap-1">
                  <StatusChip tone={user.active ? 'success' : 'neutral'}>
                    {user.active ? t('users.active') : t('users.inactive')}
                  </StatusChip>
                  {user.lockedOut && <StatusChip tone="danger">{t('users.locked')}</StatusChip>}
                  {user.mfaEnabled && <StatusChip tone="info">{t('users.mfa')}</StatusChip>}
                </TableCell>
                <TableCell className="text-xs">
                  {[user.hasPassword && t('users.password'), ...user.externalLogins]
                    .filter(Boolean)
                    .join(', ') || '—'}
                </TableCell>
                <TableCell>
                  {user.lastSignInAt ? formatDateTime(user.lastSignInAt, i18n.language) : '—'}
                </TableCell>
                <TableCell className="text-end">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={t('users.actionsFor', { name: user.userName })}
                      >
                        <MoreHorizontal aria-hidden />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      {(
                        [
                          user.active ? 'deactivate' : 'activate',
                          user.lockedOut && 'unlock',
                          'send-password-reset',
                          user.mfaEnabled && 'reset-mfa',
                          'revoke-sessions',
                        ] as (UserAction | false)[]
                      )
                        .filter((a): a is UserAction => !!a)
                        .map((action) => (
                          <DropdownMenuItem key={action} onSelect={() => act.mutate({ user, action })}>
                            {t(`users.actions.${action}`)}
                          </DropdownMenuItem>
                        ))}
                      {canSeeRoles && (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            onSelect={() =>
                              void navigateTo({
                                to: '/settings/access/assignments',
                                search: { userId: user.id },
                              })
                            }
                          >
                            {t('users.actions.roles')}
                          </DropdownMenuItem>
                        </>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {data && data.totalCount > data.pageSize && (
        <Pagination
          page={data.page}
          totalPages={Math.ceil(data.totalCount / data.pageSize)}
          totalCount={data.totalCount}
          onPageChange={(page) => void navigate({ search: (s) => ({ ...s, page }) })}
        />
      )}
      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('admin');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [employeeId, setEmployeeId] = useState<string>();
  const [userName, setUserName] = useState('');
  const [invite, setInvite] = useState(true);
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/identity/admin/users', {
          body: {
            employeeId: employeeId ?? null,
            userName: userName.trim() || null,
            sendInvitation: invite,
          },
        }),
      ),
    onSuccess: (result) => {
      toast({
        tone: invite && !result.invitationSent ? 'error' : 'success',
        title: t('users.created'),
        description: invite
          ? result.invitationSent
            ? t('users.invitationSent')
            : translateMessageKey(tc, result.invitationMessageKey ?? 'identity.errors.smtpFailed')
          : undefined,
      });
      void queryClient.invalidateQueries({ queryKey: ['identity', 'users'] });
      onClose();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('users.create')}
        description={t('users.createDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending} disabled={!employeeId} onClick={() => create.mutate()}>
              {tc('actions.create')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField label={t('users.employee')} required>
            <EmployeePicker value={employeeId} onChange={setEmployeeId} />
          </FormField>
          <FormField label={t('users.userName')} hint={t('users.userNameHint')}>
            <Input dir="ltr" value={userName} onChange={(e) => setUserName(e.target.value)} />
          </FormField>
          <Checkbox
            label={t('users.sendInvitation')}
            checked={invite}
            onChange={(e) => setInvite(e.target.checked)}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}
