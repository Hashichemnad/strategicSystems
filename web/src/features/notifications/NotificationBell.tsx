import { Link, useNavigate } from '@tanstack/react-router';
import { Bell } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/ui';
import { useLiveNotifications, useMarkRead, useMyNotifications } from './api';

/** Top-bar bell (CLAUDE.md §10): unread count updated live, the latest items, and the way to the notification centre. */
export function NotificationBell() {
  const { t, i18n } = useTranslation('notifications');
  const navigate = useNavigate();
  const mine = useMyNotifications();
  const markRead = useMarkRead();
  useLiveNotifications(true);
  const unread = mine.data?.unread ?? 0;
  const label =
    unread > 0
      ? t('bellUnread', { count: unread, formatted: formatNumber(unread, i18n.language) })
      : t('bell');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative" data-testid="notification-bell">
          <Bell aria-hidden />
          {/* Named by its content so the visible count is part of the name (WCAG 2.5.3). */}
          <span className="sr-only">{label}</span>
          {unread > 0 && (
            <span className="absolute end-1 top-1 inline-flex min-w-4 items-center justify-center rounded-full bg-danger px-1 text-[10px] font-semibold text-danger-foreground">
              {unread > 99 ? '99+' : formatNumber(unread, i18n.language)}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80">
        <DropdownMenuLabel className="flex items-center justify-between">
          {t('title')}
          {unread > 0 && (
            <button
              type="button"
              className="text-xs font-normal text-primary hover:underline"
              onClick={() => markRead.mutate(null)}
            >
              {t('markAllRead')}
            </button>
          )}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        {(mine.data?.items ?? []).slice(0, 5).map((item) => (
          <DropdownMenuItem
            key={item.id}
            className="flex flex-col items-start gap-0.5"
            onSelect={() => {
              if (!item.readAt) markRead.mutate(item.id);
              if (item.link) void navigate({ to: item.link });
            }}
          >
            <span className={item.readAt ? 'text-muted-foreground' : 'font-medium'}>
              {pickText(i18n.language, item.titleAr, item.titleEn)}
            </span>
            <span className="text-xs text-muted-foreground">
              {formatDateTime(item.createdAt, i18n.language)}
            </span>
          </DropdownMenuItem>
        ))}
        {(mine.data?.items.length ?? 0) === 0 && (
          <p className="px-2 py-3 text-sm text-muted-foreground">{t('empty')}</p>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/notifications" search={{ page: 1 }}>
            {t('viewAll')}
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
