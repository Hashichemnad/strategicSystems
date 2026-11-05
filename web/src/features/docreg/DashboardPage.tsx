import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import { CountChart, PageHeader, Skeleton, StatusBanner, Tile } from '@/ui';
import { useDocDashboard, type DocDashboard, type DocumentListItem } from './api';
import {
  DocRegPage,
  documentName,
  SensitivityChip,
  useFormatDate,
  useLookupLabel,
  ValidityChips,
} from './components';

/** Document dashboard (docs/design/requests.md §3.7): tiles, by level / type / unit, expiring and recently published lists. */
export function DocumentDashboardPage() {
  const { t } = useTranslation('docreg');
  const { t: tc, i18n } = useTranslation();
  const dashboard = useDocDashboard();
  const levelLabel = useLookupLabel('docreg.level');
  const typeLabel = useLookupLabel('docreg.type');

  return (
    <DocRegPage testId="docreg-dashboard">
      <PageHeader title={t('dashboard.title')} description={t('dashboard.description')} />
      {dashboard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />
      ) : !dashboard.data ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : (
        <>
          <Tiles data={dashboard.data} />
          <div className="grid gap-4 lg:grid-cols-3">
            <CountChart
              title={t('dashboard.byLevel')}
              testId="docreg-by-level"
              rows={dashboard.data.byLevel.map((r) => ({
                key: r.code,
                label: levelLabel(r.code),
                value: r.count,
              }))}
            />
            <CountChart
              title={t('dashboard.byType')}
              testId="docreg-by-type"
              rows={dashboard.data.byType.map((r) => ({
                key: r.code,
                label: typeLabel(r.code),
                value: r.count,
              }))}
            />
            <CountChart
              title={t('dashboard.byUnit')}
              testId="docreg-by-unit"
              rows={dashboard.data.byUnit.map((r) => ({
                key: r.id,
                label: pickText(i18n.language, r.nameAr, r.nameEn),
                value: r.count,
              }))}
            />
          </div>
          <div className="grid gap-4 lg:grid-cols-2">
            <DocList
              title={t('dashboard.expiring')}
              items={dashboard.data.expiring}
              show="expiry"
              testId="docreg-expiring"
            />
            <DocList
              title={t('dashboard.recent')}
              items={dashboard.data.recentlyPublished}
              show="published"
              testId="docreg-recent"
            />
          </div>
        </>
      )}
    </DocRegPage>
  );
}

function Tiles({ data }: { data: DocDashboard }) {
  const { t } = useTranslation('docreg');
  const canQueue = useCan('docreg.request.process');
  const tiles: [string, number, ReactNode?][] = [
    [t('dashboard.published'), data.published],
    [t('dashboard.expired'), data.expired],
    [t('dashboard.expiringSoon'), data.expiringSoon],
    [
      t('dashboard.pendingReview'),
      data.pendingReview,
      canQueue && (
        <Link to="/requests/queue" className="text-xs text-primary hover:underline">
          {t('dashboard.queue')}
        </Link>
      ),
    ],
    [t('dashboard.awaitingPublication'), data.awaitingPublication],
  ];
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5" data-testid="docreg-tiles">
      {tiles.map(([title, value, extra]) => (
        <Tile key={title} label={title} value={value} footer={extra} />
      ))}
    </div>
  );
}

function DocList({
  title,
  items,
  show,
  testId,
}: {
  title: string;
  items: DocumentListItem[];
  show: 'expiry' | 'published';
  testId: string;
}) {
  const { t, i18n } = useTranslation('docreg');
  const date = useFormatDate();
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={title}
      data-testid={testId}
    >
      <h2 className="font-semibold">{title}</h2>
      {items.length === 0 && <p className="text-sm text-muted-foreground">{t('common.noData')}</p>}
      <ul className="flex flex-col divide-y">
        {items.map((doc) => (
          <li key={doc.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
            <span className="flex min-w-0 flex-col gap-1">
              <Link
                to="/documents/$documentId"
                params={{ documentId: doc.id }}
                search={{ tab: 'overview' }}
                className="truncate text-primary hover:underline"
              >
                {doc.documentNo && <bdi>{doc.documentNo}</bdi>}
                {doc.documentNo && ' · '}
                {documentName(i18n.language, doc, t('document.untitled'))}
              </Link>
              <span className="flex flex-wrap gap-1">
                <SensitivityChip code={doc.sensitivity} />
                <ValidityChips
                  status={doc.status}
                  expiryDate={doc.expiryDate}
                  underRevision={doc.underRevision}
                />
              </span>
            </span>
            <span className="whitespace-nowrap text-muted-foreground">
              {show === 'expiry' ? date(doc.expiryDate) : date(doc.publishedAt)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
