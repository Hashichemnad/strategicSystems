import { Link } from '@tanstack/react-router';
import { BarChart3, FileText, Inbox, Lightbulb, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { useLookupOptions } from '@/features/settings/lookups';
import { pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  Input,
  ListViewToggle,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SplitView,
  StatusBanner,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useListView,
} from '@/ui';
import { SplitCard, SplitPreview } from '@/ui';
import { ipLookups, ipStatuses, useIpRecords, type IpListQuery } from './api';
import { IpPage, IpStatusChip, useDate, useIpT, useLookupLabel } from './components';

const ALL = '-';
const expiringOptions = [30, 90, 180, 365] as const;

interface Filters {
  propertyType?: string;
  protectionType?: string;
  status?: string;
  unitId?: string;
  year?: number;
  expiringWithin?: number;
  mine?: boolean;
}

/** IP register (docs/design/requests.md §4.8): filters by type, protection, status, unit, year, expiry and "my intellectual property". */
export function IpRegisterPage() {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('appearance');
  const { k, language } = useIpT();
  const date = useDate();
  const canDashboard = useCan('ip.dashboard.read');
  const propertyLabel = useLookupLabel(ipLookups.propertyType);
  const protectionLabel = useLookupLabel(ipLookups.protectionType);
  const propertyOptions = useLookupOptions(ipLookups.propertyType, undefined, t('register.allTypes'));
  const protectionOptions = useLookupOptions(
    ipLookups.protectionType,
    undefined,
    t('register.allProtections'),
  );
  const [filters, setFilters] = useState<Filters>({});
  const [page, setPage] = useState(1);
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(text.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(handle);
  }, [text]);

  const query: IpListQuery = {
    page,
    pageSize: 25,
    ...(search ? { search } : {}),
    ...(filters.propertyType ? { propertyType: filters.propertyType } : {}),
    ...(filters.protectionType ? { protectionType: filters.protectionType } : {}),
    ...(filters.status ? { status: filters.status } : {}),
    ...(filters.unitId ? { unitId: filters.unitId } : {}),
    ...(filters.year ? { year: filters.year } : {}),
    ...(filters.expiringWithin ? { expiringWithin: filters.expiringWithin } : {}),
    ...(filters.mine ? { mine: true } : {}),
  };
  const records = useIpRecords(query);
  const set = (patch: Filters) => {
    setFilters((f) => ({ ...f, ...patch }));
    setPage(1);
  };
  const thisYear = new Date().getFullYear();
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('ip.register');
  const [selectedId, setSelectedId] = useState<string>();
  // The first row is previewed until the user picks another.
  const selected = records.data?.items.find((r) => r.id === selectedId) ?? records.data?.items[0];
  const unitName = (u: { nameAr: string | null; nameEn: string | null } | null) =>
    u ? pickText(language, u.nameAr, u.nameEn) : '—';
  const years = Array.from({ length: 6 }, (_, i) => thisYear - i);

  return (
    <IpPage testId="ip-register">
      <PageHeader
        title={t('register.title')}
        description={t('register.description')}
        actions={
          <>
            {listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
            {canDashboard && (
              <Button variant="outline" asChild>
                <Link to="/ip/dashboard">
                  <BarChart3 aria-hidden />
                  {t('nav.dashboard')}
                </Link>
              </Button>
            )}
            <Button asChild>
              <Link
                to="/requests/new/$typeCode"
                params={{ typeCode: 'ip.registration' }}
                data-testid="ip-new"
              >
                <Plus aria-hidden />
                {t('register.new')}
              </Link>
            </Button>
          </>
        }
      />
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('register.search')}
            aria-label={t('register.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
            data-testid="ip-search"
          />
        </div>
        <Select
          aria-label={t('fields.propertyType')}
          value={filters.propertyType ?? ALL}
          onValueChange={(v) => set({ propertyType: v === ALL ? undefined : v })}
          options={propertyOptions}
        />
        <Select
          aria-label={t('fields.protectionType')}
          value={filters.protectionType ?? ALL}
          onValueChange={(v) => set({ protectionType: v === ALL ? undefined : v })}
          options={protectionOptions}
        />
        <Select
          aria-label={t('fields.status')}
          value={filters.status ?? ALL}
          onValueChange={(v) => set({ status: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('register.allStatuses') },
            ...ipStatuses.map((s) => ({ value: s, label: k(`status.${s}`) })),
          ]}
        />
        <UnitSelect allowEmpty value={filters.unitId} onChange={(unitId) => set({ unitId })} />
        <Select
          aria-label={t('fields.year')}
          value={filters.year ? String(filters.year) : ALL}
          onValueChange={(v) => set({ year: v === ALL ? undefined : Number(v) })}
          options={[
            { value: ALL, label: t('register.allYears') },
            ...years.map((y) => ({ value: String(y), label: String(y) })),
          ]}
        />
        <Select
          aria-label={t('register.expiringWithin')}
          value={filters.expiringWithin ? String(filters.expiringWithin) : ALL}
          onValueChange={(v) => set({ expiringWithin: v === ALL ? undefined : Number(v) })}
          options={[
            { value: ALL, label: t('register.anyExpiry') },
            ...expiringOptions.map((d) => ({
              value: String(d),
              label: t('register.expiringDays', { count: d }),
            })),
          ]}
        />
        <Switch
          className="self-center"
          checked={filters.mine ?? false}
          onCheckedChange={(mine) => set({ mine: mine || undefined })}
          label={t('register.mine')}
          id="ip-mine"
        />
      </div>
      {records.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, records.error)} />
      ) : !records.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : records.data.items.length === 0 ? (
        <EmptyState icon={Lightbulb} title={t('register.empty')} description={t('register.emptyHint')} />
      ) : (
        <>
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="ip-cards">
                  {records.data.items.map((item) => (
                    <SplitCard
                      key={item.id}
                      selected={item.id === selected?.id}
                      onPreview={() => setSelectedId(item.id)}
                      title={
                        <Link
                          to="/ip/records/$recordId"
                          params={{ recordId: item.id }}
                          search={{ tab: 'overview' }}
                          className="min-w-0 font-medium hover:underline"
                        >
                          <bdi className="block text-xs text-muted-foreground">{item.number}</bdi>
                          <span dir="auto" className="block">
                            {pickText(language, item.nameAr, item.nameEn) || '—'}
                          </span>
                        </Link>
                      }
                      chips={
                        <>
                          <IpStatusChip status={item.status} />
                          {item.expiringSoon && (
                            <StatusChip tone="warning">{t('register.expiringSoon')}</StatusChip>
                          )}
                          <span className="text-xs text-muted-foreground">
                            {propertyLabel(item.propertyType)}
                          </span>
                        </>
                      }
                    />
                  ))}
                </ul>
              }
              preview={
                selected && (
                  <SplitPreview
                    title={pickText(language, selected.nameAr, selected.nameEn) || '—'}
                    rows={[
                      [t('fields.number'), <bdi key="n">{selected.number}</bdi>],
                      [t('fields.propertyType'), propertyLabel(selected.propertyType)],
                      [t('fields.protectionType'), protectionLabel(selected.protectionType)],
                      [t('fields.status'), <IpStatusChip key="s" status={selected.status} />],
                      [t('fields.ownerUnit'), unitName(selected.ownerUnit)],
                      [t('fields.registrationDate'), date(selected.registrationDate)],
                      [t('fields.expiryDate'), date(selected.expiryDate)],
                      [t('fields.source'), k(`source.${selected.source}`)],
                    ]}
                    open={
                      <Link
                        to="/ip/records/$recordId"
                        params={{ recordId: selected.id }}
                        search={{ tab: 'overview' }}
                      >
                        {ta('split.open')}
                      </Link>
                    }
                  />
                )
              }
            />
          ) : (
            <div className="overflow-x-auto rounded-lg border bg-card">
              <Table data-testid="ip-table">
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.propertyType')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.protectionType')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.ownerUnit')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.registrationDate')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.expiryDate')}</TableHeaderCell>
                    <TableHeaderCell>
                      <span className="sr-only">{t('fields.source')}</span>
                    </TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {records.data.items.map((item) => (
                    <TableRow key={item.id} data-testid={`ip-row-${item.id}`}>
                      <TableCell>
                        <Link
                          to="/ip/records/$recordId"
                          params={{ recordId: item.id }}
                          search={{ tab: 'overview' }}
                          className="font-medium whitespace-nowrap text-primary hover:underline"
                        >
                          <bdi>{item.number}</bdi>
                        </Link>
                      </TableCell>
                      <TableCell className="min-w-48">
                        {pickText(language, item.nameAr, item.nameEn) || '—'}
                      </TableCell>
                      <TableCell>{propertyLabel(item.propertyType)}</TableCell>
                      <TableCell>{protectionLabel(item.protectionType)}</TableCell>
                      <TableCell>
                        <IpStatusChip status={item.status} />
                      </TableCell>
                      <TableCell>
                        {item.ownerUnit
                          ? pickText(language, item.ownerUnit.nameAr, item.ownerUnit.nameEn)
                          : '—'}
                      </TableCell>
                      <TableCell className="whitespace-nowrap">{date(item.registrationDate)}</TableCell>
                      <TableCell className="whitespace-nowrap">
                        <span className="flex flex-wrap items-center gap-2">
                          {date(item.expiryDate)}
                          {item.expiringSoon && (
                            <StatusChip tone="warning">{t('register.expiringSoon')}</StatusChip>
                          )}
                        </span>
                      </TableCell>
                      <TableCell>
                        <SourceIcon source={item.source} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <Pagination
            page={records.data.page}
            totalPages={records.data.totalPages ?? 1}
            totalCount={records.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </IpPage>
  );
}

function SourceIcon({ source }: { source: string }) {
  const { k } = useIpT();
  const label = k(`source.${source}`);
  const Icon = source === 'document' ? FileText : Inbox;
  return (
    <span title={label} className="inline-flex text-muted-foreground">
      <Icon className="size-4" aria-hidden />
      <span className="sr-only">{label}</span>
    </span>
  );
}
