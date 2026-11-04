import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Pencil } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
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
import { FieldErrors } from '../project/FieldErrors';
import { type VendorDetail, useVendor, useVendorMutation, vendorApi, vendorStatuses } from '../wasel/waselApi';
import { RatingsTable, statusTone } from '../wasel/WaselTab';
import { VendorDialog } from './VendorDialog';

const route = getRouteApi('/app/portfolio/vendors/$vendorId');

/** A vendor of the register: details, Wasel users and their activity, performance history (SPEC §1.18). */
export function VendorPage() {
  const { vendorId } = route.useParams();
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const vendor = useVendor(vendorId);
  const canManage = useCan('portfolio.vendor.manage');
  const [dialog, setDialog] = useState<'edit' | 'status' | { deactivate: string } | null>(null);
  const toast = useToast();
  const reactivate = useVendorMutation(vendorApi.reactivateUser);
  const lang = i18n.language;
  if (vendor.isPending) return <Skeleton className="h-96" />;
  if (vendor.isError) return <StatusBanner tone="danger" title={errorMessage(tc, vendor.error)} />;
  const v = vendor.data;
  const perf = v.performance;
  const avg = (value: number | null) => (value === null ? '—' : formatNumber(value, lang));
  const details: [string, string | null][] = [
    [t('fields.nameAr'), v.nameAr],
    [t('fields.nameEn'), v.nameEn],
    [t('fields.tradeLicenceNo'), v.tradeLicenceNo],
    [t('fields.licenceExpiry'), v.licenceExpiry ? formatDate(v.licenceExpiry, lang) : null],
    [t('fields.countryCode'), v.countryCode],
    [t('fields.contactPerson'), v.contactPerson],
    [t('fields.email'), v.email],
    [t('fields.phone'), v.phone],
    [t('fields.address'), v.address],
    [t('fields.notes'), v.notes],
  ];
  return (
    <div className="flex flex-col gap-6" data-testid="vendor-page">
      <Link to="/portfolio/vendors" className="flex items-center gap-1 text-sm text-primary hover:underline">
        <ArrowLeft aria-hidden className="rtl:rotate-180" />
        {t('register.title')}
      </Link>
      <PageHeader
        title={`${v.number} · ${pickText(lang, v.nameAr, v.nameEn)}`}
        description={<StatusChip tone={statusTone[v.status] ?? 'neutral'}>{t(`status.${v.status}`, v.status)}</StatusChip>}
        actions={
          canManage ? (
            <div className="flex gap-2">
              <Button variant="outline" onClick={() => setDialog('edit')} data-testid="vendor-edit">
                <Pencil aria-hidden />
                {t('detail.edit')}
              </Button>
              <Button variant="outline" onClick={() => setDialog('status')} data-testid="vendor-status">
                {t('detail.changeStatus')}
              </Button>
            </div>
          ) : undefined
        }
      />
      {v.statusReason && <StatusBanner tone="warning" title={t('detail.statusReason')} description={v.statusReason} />}
      <dl className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2">
        {details.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd dir="auto">{value ?? '—'}</dd>
          </div>
        ))}
      </dl>
      <section className="flex flex-col gap-3" aria-labelledby="vendor-users">
        <h2 id="vendor-users" className="font-semibold">
          {t('detail.users')}
        </h2>
        {v.users.length === 0 ? (
          <EmptyState title={t('detail.noUsers')} />
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('panel.email')}</TableHeaderCell>
                  <TableHeaderCell>{t('register.statusCol')}</TableHeaderCell>
                  <TableHeaderCell>{t('detail.invitedAt')}</TableHeaderCell>
                  <TableHeaderCell>{t('detail.lastSignIn')}</TableHeaderCell>
                  <TableHeaderCell>{t('detail.mfa')}</TableHeaderCell>
                  <TableHeaderCell>{t('register.activeProjects')}</TableHeaderCell>
                  <TableHeaderCell>
                    <span className="sr-only">{t('panel.actions')}</span>
                  </TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {v.users.map((u) => (
                  <TableRow key={u.id} data-testid="vendor-user-row">
                    <TableCell>
                      <span dir="ltr">{u.email}</span>
                      {u.name && <span className="block text-xs text-muted-foreground">{u.name}</span>}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={statusTone[u.status] ?? 'neutral'}>{t(`userStatus.${u.status}`, u.status)}</StatusChip>
                      {u.disabledReason && <span className="block text-xs text-muted-foreground">{u.disabledReason}</span>}
                    </TableCell>
                    <TableCell>{formatDateTime(u.invitedAt, lang)}</TableCell>
                    <TableCell>{u.lastSignInAt ? formatDateTime(u.lastSignInAt, lang) : t('detail.never')}</TableCell>
                    <TableCell>
                      <StatusChip tone={u.mfaEnabled ? 'success' : 'warning'}>{u.mfaEnabled ? t('detail.mfaOn') : t('detail.mfaOff')}</StatusChip>
                    </TableCell>
                    <TableCell>{formatNumber(u.activeProjects, lang)}</TableCell>
                    <TableCell>
                      {canManage &&
                        (u.status === 'active' || u.status === 'invited' ? (
                          <Button size="sm" variant="ghost" onClick={() => setDialog({ deactivate: u.id })}>
                            {t('detail.deactivate')}
                          </Button>
                        ) : (
                          <Button
                            size="sm"
                            variant="ghost"
                            loading={reactivate.isPending}
                            onClick={() =>
                              reactivate.mutate(u.id, {
                                onSuccess: () => toast({ title: t('detail.reactivated') }),
                                onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                              })
                            }
                          >
                            {t('detail.reactivate')}
                          </Button>
                        ))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>
      <section className="flex flex-col gap-3" aria-labelledby="vendor-performance">
        <h2 id="vendor-performance" className="font-semibold">
          {t('detail.performance')}
        </h2>
        <dl className="grid grid-cols-2 gap-3 rounded-lg border bg-card p-4 sm:grid-cols-6" data-testid="vendor-performance">
          <div>
            <dt className="text-xs text-muted-foreground">{t('detail.count')}</dt>
            <dd className="text-lg font-semibold">{formatNumber(perf.count, lang)}</dd>
          </div>
          {(['timeliness', 'quality', 'responsiveness', 'reporting', 'overall'] as const).map((c) => (
            <div key={c}>
              <dt className="text-xs text-muted-foreground">{t(`rating.${c}`)}</dt>
              <dd className="text-lg font-semibold">{avg(perf[c])}</dd>
            </div>
          ))}
        </dl>
        {v.ratings.length === 0 ? (
          <EmptyState title={t('detail.noRatings')} />
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-card">
            <RatingsTable ratings={v.ratings} lang={lang} showProject />
          </div>
        )}
      </section>
      {dialog === 'edit' && <VendorDialog vendor={v} onClose={() => setDialog(null)} />}
      {dialog === 'status' && <StatusDialog vendor={v} onClose={() => setDialog(null)} />}
      {dialog !== null && typeof dialog === 'object' && <DeactivateDialog userId={dialog.deactivate} onClose={() => setDialog(null)} />}
    </div>
  );
}

function StatusDialog({ vendor, onClose }: { vendor: VendorDetail; onClose: () => void }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const save = useVendorMutation(vendorApi.status);
  const [status, setStatus] = useState(vendor.status);
  const [reason, setReason] = useState('');
  const needsReason = status !== 'active';
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('detail.changeStatus')}
        description={t('detail.statusHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={needsReason ? 'danger' : 'primary'}
              disabled={needsReason && reason.trim() === ''}
              loading={save.isPending}
              data-testid="vendor-status-save"
              onClick={() => save.mutate({ id: vendor.id, status, reason: reason.trim() || null }, { onSuccess: onClose })}
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FormField label={t('register.statusCol')}>
            <Select value={status} onValueChange={setStatus} options={vendorStatuses.map((s) => ({ value: s, label: t(`status.${s}`) }))} />
          </FormField>
          {needsReason && (
            <FormField label={t('panel.reason')} required>
              <Textarea dir="auto" value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
          )}
          {save.error && <FieldErrors error={save.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function DeactivateDialog({ userId, onClose }: { userId: string; onClose: () => void }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const save = useVendorMutation(vendorApi.deactivateUser);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('detail.deactivate')}
        description={t('detail.deactivateHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={reason.trim() === ''}
              loading={save.isPending}
              onClick={() => save.mutate({ id: userId, reason: reason.trim() }, { onSuccess: onClose })}
            >
              {t('detail.deactivate')}
            </Button>
          </>
        }
      >
        <FormField label={t('panel.reason')} required>
          <Textarea dir="auto" value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {save.error && <FieldErrors error={save.error} />}
      </DialogContent>
    </Dialog>
  );
}
