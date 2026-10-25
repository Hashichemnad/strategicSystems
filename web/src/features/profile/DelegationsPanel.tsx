import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useMe } from '@/features/auth/session';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogContent,
  EmptyState,
  FormField,
  Input,
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

/** My delegations (given and received): leave cover for approvals, consumed by the approval engine (Phase 4). */
export function DelegationsPanel() {
  const { t } = useTranslation('profile');
  const { t: tc, i18n } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [delegateId, setDelegateId] = useState<string>();
  const [from, setFrom] = useState<string>();
  const [to, setTo] = useState<string>();
  const [reason, setReason] = useState('');
  const [cancelId, setCancelId] = useState<string>();
  const delegations = useQuery({
    queryKey: ['org', 'delegations', 'mine'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/org/delegations/mine', { signal })),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['org', 'delegations'] });
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/org/delegations', {
          body: {
            delegatorEmployeeId: null,
            delegateEmployeeId: delegateId ?? null,
            fromDate: from ?? null,
            toDate: to ?? null,
            scope: [],
            reason,
          },
        }),
      ),
    onSuccess: () => {
      setOpen(false);
      toast({ title: t('delegations.created') });
      void refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const cancel = useMutation({
    mutationFn: async (id: string) => {
      const delegation = delegations.data?.find((d) => d.id === id);
      return unwrap(
        await api.POST('/api/v1/org/delegations/{id}/cancel', {
          params: { path: { id } },
          body: { reason: t('delegations.cancelledByUser'), version: delegation?.version ?? 0 },
        }),
      );
    },
    onSuccess: () => {
      setCancelId(undefined);
      void refresh();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  if (!me?.employee) return <StatusBanner title={t('delegations.noEmployee')} />;

  const name = (ref: { nameAr?: string | null; nameEn?: string | null }) =>
    pickText(i18n.language, ref.nameAr, ref.nameEn);
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-5">
      <div>
        <h2 className="text-lg font-semibold">{t('delegations.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('delegations.description')}</p>
      </div>
      {delegations.data?.length === 0 ? (
        <EmptyState title={t('delegations.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('delegations.from')}</TableHeaderCell>
              <TableHeaderCell>{t('delegations.to')}</TableHeaderCell>
              <TableHeaderCell>{t('delegations.period')}</TableHeaderCell>
              <TableHeaderCell>{t('delegations.status')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {delegations.data?.map((delegation) => (
              <TableRow key={delegation.id}>
                <TableCell>{name(delegation.delegator)}</TableCell>
                <TableCell>{name(delegation.delegate)}</TableCell>
                <TableCell>
                  {formatDate(delegation.fromDate, i18n.language)} –{' '}
                  {formatDate(delegation.toDate, i18n.language)}
                </TableCell>
                <TableCell>
                  <StatusChip tone={delegation.status === 'active' ? 'success' : 'neutral'}>
                    {t(`delegations.statuses.${delegation.status as 'active' | 'cancelled'}`)}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  {delegation.status === 'active' && delegation.delegator.id === me.employee?.id && (
                    <Button
                      variant="ghost"
                      size="sm"
                      loading={cancel.isPending && cancelId === delegation.id}
                      onClick={() => {
                        setCancelId(delegation.id);
                        cancel.mutate(delegation.id);
                      }}
                    >
                      {tc('actions.cancel')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Button className="self-start" onClick={() => setOpen(true)}>
        {t('delegations.create')}
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent title={t('delegations.create')}>
          <form
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault();
              create.mutate();
            }}
          >
            <FormField label={t('delegations.to')} required>
              <EmployeePicker value={delegateId} onChange={setDelegateId} excludeId={me.employee.id} />
            </FormField>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label={t('delegations.fromDate')} required>
                <DatePicker value={from} onChange={setFrom} />
              </FormField>
              <FormField label={t('delegations.toDate')} required>
                <DatePicker value={to} onChange={setTo} />
              </FormField>
            </div>
            <FormField label={t('delegations.reason')}>
              <Input value={reason} maxLength={1000} onChange={(e) => setReason(e.target.value)} />
            </FormField>
            <Button type="submit" loading={create.isPending} disabled={!delegateId || !from || !to}>
              {tc('actions.create')}
            </Button>
          </form>
        </DialogContent>
      </Dialog>
    </section>
  );
}
