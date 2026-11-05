import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import { CountChart, PageHeader, Skeleton, StatusBanner, StatusChip, Tile, type TileTone } from '@/ui';
import { ipLookups, useIpDashboard, type IpDashboard } from './api';
import { IpPage, IpStatusChip, useDate, useIpT, useLookupLabel } from './components';

/** IP dashboard (docs/design/requests.md §4.8): totals, by type / protection / year / unit, upcoming expiries. */
export function IpDashboardPage() {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { language } = useIpT();
  const dashboard = useIpDashboard();
  const propertyLabel = useLookupLabel(ipLookups.propertyType);
  const protectionLabel = useLookupLabel(ipLookups.protectionType);

  return (
    <IpPage testId="ip-dashboard">
      <PageHeader title={t('dashboard.title')} description={t('dashboard.description')} />
      {dashboard.isError && <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />}
      {dashboard.isPending ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : (
        dashboard.data && (
          <>
            <Tiles data={dashboard.data} />
            <div className="grid gap-4 lg:grid-cols-2">
              <CountChart
                title={t('dashboard.byType')}
                testId="ip-by-type"
                rows={dashboard.data.byType.map((r) => ({
                  key: r.code,
                  label: propertyLabel(r.code),
                  value: r.count,
                }))}
              />
              <CountChart
                title={t('dashboard.byProtection')}
                rows={dashboard.data.byProtection.map((r) => ({
                  key: r.code,
                  label: protectionLabel(r.code),
                  value: r.count,
                }))}
              />
              <CountChart
                title={t('dashboard.byYear')}
                rows={dashboard.data.byYear.map((r) => ({ key: r.code, label: r.code, value: r.count }))}
              />
              <CountChart
                title={t('dashboard.byUnit')}
                rows={dashboard.data.byUnit.map((r) => ({
                  key: r.id,
                  label: pickText(language, r.nameAr, r.nameEn) || '—',
                  value: r.count,
                }))}
              />
            </div>
            <Upcoming rows={dashboard.data.upcomingExpiries} />
          </>
        )
      )}
    </IpPage>
  );
}

function Tiles({ data }: { data: IpDashboard }) {
  const { t } = useTranslation('ip');
  const tiles: [
    key: 'total' | 'registered' | 'pending' | 'expiringSoon' | 'expired',
    value: number,
    tone?: TileTone,
  ][] = [
    ['total', data.total],
    ['registered', data.registered],
    ['pending', data.pending],
    ['expiringSoon', data.expiringSoon, data.expiringSoon > 0 ? 'warning' : undefined],
    ['expired', data.expired, data.expired > 0 ? 'danger' : undefined],
  ];
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5" data-testid="ip-tiles">
      {tiles.map(([key, value, tone]) => (
        <Tile
          key={key}
          label={t(`dashboard.tiles.${key}`)}
          value={value}
          tone={tone}
          testId={`ip-tile-${key}`}
        />
      ))}
    </div>
  );
}

function Upcoming({ rows }: { rows: IpDashboard['upcomingExpiries'] }) {
  const { t } = useTranslation('ip');
  const { language } = useIpT();
  const date = useDate();
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('dashboard.upcoming')}
    >
      <h2 className="font-semibold">{t('dashboard.upcoming')}</h2>
      {rows.length === 0 && <p className="text-sm text-muted-foreground">{t('dashboard.noUpcoming')}</p>}
      <ul className="flex flex-col divide-y" data-testid="ip-upcoming">
        {rows.map((row) => (
          <li key={row.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
            <Link
              to="/ip/records/$recordId"
              params={{ recordId: row.id }}
              search={{ tab: 'overview' }}
              className="min-w-0 truncate text-primary hover:underline"
            >
              <bdi>{row.number}</bdi> · {pickText(language, row.nameAr, row.nameEn)}
            </Link>
            <span className="flex flex-wrap items-center gap-2">
              <IpStatusChip status={row.status} />
              <StatusChip tone={row.expiringSoon ? 'warning' : 'neutral'}>
                {t('dashboard.expires', { date: date(row.expiryDate) })}
              </StatusChip>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
