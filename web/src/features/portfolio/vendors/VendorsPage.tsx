import { Link } from '@tanstack/react-router';
import { Handshake, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
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
} from '@/ui';
import { useVendors, vendorStatuses } from '../wasel/waselApi';
import { statusTone } from '../wasel/WaselTab';
import { VendorDialog } from './VendorDialog';

/** Portfolio → Vendors (Wasel): the shared supplier register (ADR 0112). */
export function VendorsPage() {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const vendors = useVendors(search.trim(), status);
  const canManage = useCan('portfolio.vendor.manage');
  const [creating, setCreating] = useState(false);
  const lang = i18n.language;
  return (
    <div className="flex flex-col gap-6" data-testid="vendors-page">
      <PageHeader
        title={t('register.title')}
        description={t('register.description')}
        actions={
          canManage ? (
            <Button onClick={() => setCreating(true)} data-testid="vendor-new">
              <Plus aria-hidden />
              {t('register.new')}
            </Button>
          ) : undefined
        }
      />
      <div className="flex flex-wrap items-end gap-3">
        <Input
          className="max-w-xs"
          type="search"
          aria-label={t('register.search')}
          placeholder={t('register.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          data-testid="vendor-search"
        />
        <div className="w-48">
          <Select
            value={status || '*'}
            onValueChange={(v) => setStatus(v === '*' ? '' : v)}
            options={[
              { value: '*', label: t('register.allStatuses') },
              ...vendorStatuses.map((s) => ({ value: s, label: t(`status.${s}`) })),
            ]}
          />
        </div>
      </div>
      {vendors.isPending ? (
        <Skeleton className="h-64" />
      ) : vendors.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, vendors.error)} />
      ) : vendors.data.length === 0 ? (
        <EmptyState title={t('register.empty')} icon={Handshake} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('register.number')}</TableHeaderCell>
                <TableHeaderCell>{t('register.name')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.tradeLicenceNo')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.licenceExpiry')}</TableHeaderCell>
                <TableHeaderCell>{t('register.statusCol')}</TableHeaderCell>
                <TableHeaderCell>{t('register.users')}</TableHeaderCell>
                <TableHeaderCell>{t('register.activeProjects')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.overall')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {vendors.data.map((v) => (
                <TableRow key={v.id} data-testid="vendor-row">
                  <TableCell>
                    <Link
                      to="/portfolio/vendors/$vendorId"
                      params={{ vendorId: v.id }}
                      className="text-primary hover:underline"
                    >
                      {v.number}
                    </Link>
                  </TableCell>
                  <TableCell dir="auto">{pickText(lang, v.nameAr, v.nameEn)}</TableCell>
                  <TableCell>{v.tradeLicenceNo ?? '—'}</TableCell>
                  <TableCell>{v.licenceExpiry ? formatDate(v.licenceExpiry, lang) : '—'}</TableCell>
                  <TableCell>
                    <StatusChip tone={statusTone[v.status] ?? 'neutral'}>
                      {t(`status.${v.status}`, v.status)}
                    </StatusChip>
                  </TableCell>
                  <TableCell>{formatNumber(v.users, lang)}</TableCell>
                  <TableCell>{formatNumber(v.activeProjects, lang)}</TableCell>
                  <TableCell>
                    {v.averageRating === null ? '—' : formatNumber(v.averageRating, lang)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {creating && <VendorDialog onClose={() => setCreating(false)} />}
    </div>
  );
}
