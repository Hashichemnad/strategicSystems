import { getRouteApi, useNavigate } from '@tanstack/react-router';
import { BellOff, CheckCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import { Button, EmptyState, PageHeader, Pagination, Skeleton, StatusBanner, StatusChip } from '@/ui';
import { useMarkRead, useMyNotifications } from './api';

const route = getRouteApi('/app/notifications');

/** Notification centre: every in-app notification of the user, newest first; links open the record. */
export function NotificationsPage() {
  const { t, i18n } = useTranslation('notifications');
  const { t: tc } = useTranslation();
  const search = route.useSearch();
  const routeNavigate = route.useNavigate();
  const navigate = useNavigate();
  const mine = useMyNotifications(search.page);
  const markRead = useMarkRead();

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        actions={
          (mine.data?.unread ?? 0) > 0 && (
            <Button variant="outline" onClick={() => markRead.mutate(null)} loading={markRead.isPending}>
              <CheckCheck aria-hidden />
              {t('markAllRead')}
            </Button>
          )
        }
      />
      {mine.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : mine.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, mine.error)} />
      ) : mine.data.items.length === 0 ? (
        <EmptyState icon={BellOff} title={t('empty')} />
      ) : (
        <>
          <ul className="flex flex-col divide-y rounded-lg border bg-card">
            {mine.data.items.map((item) => (
              <li key={item.id} className="flex flex-wrap items-center gap-3 p-4">
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className={item.readAt ? '' : 'font-semibold'}>
                    {pickText(i18n.language, item.titleAr, item.titleEn)}
                  </span>
                  <span className="text-sm text-muted-foreground">
                    {formatDateTime(item.createdAt, i18n.language)}
                  </span>
                </div>
                {!item.readAt && <StatusChip tone="info">{t('new')}</StatusChip>}
                {item.link && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      if (!item.readAt) markRead.mutate(item.id);
                      void navigate({ to: item.link ?? '/' });
                    }}
                  >
                    {t('open')}
                  </Button>
                )}
                {!item.readAt && (
                  <Button variant="ghost" size="sm" onClick={() => markRead.mutate(item.id)}>
                    {t('markRead')}
                  </Button>
                )}
              </li>
            ))}
          </ul>
          <Pagination
            page={search.page}
            totalPages={Math.max(1, Math.ceil(mine.data.totalCount / mine.data.pageSize))}
            totalCount={mine.data.totalCount}
            onPageChange={(page) => void routeNavigate({ search: { page } })}
          />
        </>
      )}
    </div>
  );
}
