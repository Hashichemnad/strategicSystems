import { Link, useNavigate } from '@tanstack/react-router';
import {
  AlarmClock,
  Bell,
  ClipboardCheck,
  FileClock,
  Inbox,
  ListTodo,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { useMe } from '@/features/auth/session';
import { useMyNotifications } from '@/features/notifications/api';
import { useMyWork, type MyWork } from '@/features/workflow/api';
import { usePersonName } from '@/features/workflow/ApprovalPanel';
import { cn } from '@/lib/cn';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import { Button, EmptyState, PageHeader, Skeleton, StatusBanner, StatusChip } from '@/ui';

function Tile({
  icon: Icon,
  label,
  value,
  tone,
}: {
  icon: LucideIcon;
  label: string;
  value: number;
  tone?: 'danger';
}) {
  const { i18n } = useTranslation();
  return (
    <div className="flex items-center gap-3 rounded-lg border bg-card p-4">
      <span
        className={cn(
          'flex size-10 items-center justify-center rounded-full',
          tone === 'danger' && value > 0 ? 'bg-danger-soft text-danger' : 'bg-primary/10 text-primary',
        )}
      >
        <Icon className="size-5" aria-hidden />
      </span>
      <div className="flex flex-col">
        <span className="text-2xl font-semibold tabular-nums">{formatNumber(value, i18n.language)}</span>
        <span className="text-sm text-muted-foreground">{label}</span>
      </div>
    </div>
  );
}

function Card({
  title,
  icon: Icon,
  children,
  action,
}: {
  title: string;
  icon: LucideIcon;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title}>
      <header className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold">
          <Icon className="size-4 text-muted-foreground" aria-hidden />
          {title}
        </h2>
        {action}
      </header>
      {children}
    </section>
  );
}

function Approvals({ data }: { data: MyWork }) {
  const { t, i18n } = useTranslation('workflow');
  const navigate = useNavigate();
  const name = usePersonName();
  if (data.approvals.length === 0)
    return <p className="text-sm text-muted-foreground">{t('myWork.approvals.empty')}</p>;
  return (
    <ul className="flex flex-col divide-y">
      {data.approvals.map((task) => (
        <li
          key={task.taskId}
          className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <div className="flex min-w-0 flex-col gap-1">
            <span className="text-xs text-muted-foreground">
              {pickText(i18n.language, task.processNameAr, task.processNameEn)}
              {task.number && ` · ${task.number}`}
            </span>
            <Link to={task.link as '/'} className="truncate font-medium text-primary hover:underline">
              {pickText(i18n.language, task.titleAr, task.titleEn) || task.number}
            </Link>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <StatusChip tone="info">{pickText(i18n.language, task.stepNameAr, task.stepNameEn)}</StatusChip>
              {task.onBehalfOf && (
                <StatusChip tone="warning">
                  {t('myWork.approvals.onBehalfOf', { name: name(task.onBehalfOf) })}
                </StatusChip>
              )}
              {task.attempt > 1 && (
                <StatusChip>{t('myWork.approvals.attempt', { number: task.attempt })}</StatusChip>
              )}
              {task.overdue ? (
                <StatusChip tone="danger">
                  <TriangleAlert className="size-3" aria-hidden /> {t('panel.overdue')}
                </StatusChip>
              ) : (
                <span className="text-muted-foreground">
                  {task.dueAt
                    ? t('panel.due', { date: formatDateTime(task.dueAt, i18n.language) })
                    : t('myWork.approvals.noDue')}
                </span>
              )}
            </div>
            <span className="text-xs text-muted-foreground">
              {t('myWork.approvals.submittedBy')}: {task.submitterName ?? '—'} ·{' '}
              {t('myWork.approvals.waiting')} {formatDateTime(task.openedAt, i18n.language)}
            </span>
          </div>
          <Button size="sm" variant="outline" onClick={() => void navigate({ to: task.link as '/' })}>
            {t('myWork.approvals.open')}
          </Button>
        </li>
      ))}
    </ul>
  );
}

/** My Work home page (SPEC §0.3, §0.4 level 1): pending approvals across systems, items needing action, requests, notifications. */
export function HomePage() {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const me = useMe();
  const work = useMyWork();
  const notifications = useMyNotifications(1, false);
  const displayName =
    (pickText(i18n.language, me?.employee?.nameAr, me?.employee?.nameEn) || me?.userName) ?? '';

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('myWork.greeting', { name: displayName })} description={t('myWork.description')} />
      {work.isError && <StatusBanner tone="danger" title={errorMessage(tc, work.error)} />}
      {work.isPending ? (
        <Skeleton className="h-24 rounded-lg" />
      ) : (
        work.data && (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Tile
              icon={ClipboardCheck}
              label={t('myWork.counts.pending')}
              value={work.data.counts.pendingApprovals}
            />
            <Tile
              icon={AlarmClock}
              label={t('myWork.counts.overdue')}
              value={work.data.counts.overdue}
              tone="danger"
            />
            <Tile
              icon={ListTodo}
              label={t('myWork.counts.actionItems')}
              value={work.data.counts.actionItems}
            />
            <Tile icon={Bell} label={t('myWork.counts.unread')} value={notifications.data?.unread ?? 0} />
          </div>
        )
      )}
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="flex flex-col gap-6 xl:col-span-2">
          <Card title={t('myWork.approvals.title')} icon={ClipboardCheck}>
            {work.data ? <Approvals data={work.data} /> : <Skeleton className="h-32" />}
          </Card>
          <Card title={t('myWork.actionItems.title')} icon={ListTodo}>
            {work.data && work.data.actionItems.length === 0 && (
              <p className="text-sm text-muted-foreground">{t('myWork.actionItems.empty')}</p>
            )}
            <ul className="flex flex-col divide-y">
              {work.data?.actionItems.map((item) => (
                <li key={`${item.resourceType}:${item.resourceId}`} className="flex flex-col gap-0.5 py-3">
                  <Link to={item.link as '/'} className="font-medium text-primary hover:underline">
                    {[item.number, pickText(i18n.language, item.titleAr, item.titleEn)]
                      .filter(Boolean)
                      .join(' · ')}
                  </Link>
                  <span className="text-sm text-muted-foreground">
                    {translateMessageKey(tc, item.messageKey)}
                  </span>
                </li>
              ))}
            </ul>
          </Card>
        </div>
        <div className="flex flex-col gap-6">
          <Card title={t('myWork.submissions.title')} icon={FileClock}>
            {work.data && work.data.submissions.length === 0 && (
              <p className="text-sm text-muted-foreground">{t('myWork.submissions.empty')}</p>
            )}
            <ul className="flex flex-col divide-y">
              {work.data?.submissions.map((submission) => (
                <li key={submission.instanceId} className="flex flex-col gap-1 py-3">
                  <Link to={submission.link as '/'} className="font-medium text-primary hover:underline">
                    {[submission.number, pickText(i18n.language, submission.titleAr, submission.titleEn)]
                      .filter(Boolean)
                      .join(' · ')}
                  </Link>
                  <span className="flex flex-wrap items-center gap-2 text-sm">
                    <StatusChip
                      tone={
                        submission.status === 'approved'
                          ? 'success'
                          : submission.status === 'rejected'
                            ? 'danger'
                            : submission.status === 'returned'
                              ? 'warning'
                              : 'info'
                      }
                    >
                      {t(`status.${submission.status as 'approved'}`)}
                    </StatusChip>
                    {submission.stepNameEn && (
                      <span className="text-muted-foreground">
                        {t('myWork.submissions.at', {
                          step: pickText(i18n.language, submission.stepNameAr, submission.stepNameEn),
                        })}
                      </span>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </Card>
          <Card
            title={t('myWork.notifications.title')}
            icon={Inbox}
            action={
              <Link to="/notifications" search={{ page: 1 }} className="text-sm text-primary hover:underline">
                {t('myWork.notifications.all')}
              </Link>
            }
          >
            {notifications.data && notifications.data.items.length === 0 && (
              <EmptyState icon={Bell} title={t('myWork.notifications.empty')} />
            )}
            <ul className="flex flex-col divide-y">
              {notifications.data?.items.slice(0, 5).map((item) => (
                <li key={item.id} className="flex flex-col gap-0.5 py-2">
                  {item.link ? (
                    <Link
                      to={item.link as '/'}
                      className={cn('text-sm hover:underline', !item.readAt && 'font-semibold')}
                    >
                      {pickText(i18n.language, item.titleAr, item.titleEn)}
                    </Link>
                  ) : (
                    <span className={cn('text-sm', !item.readAt && 'font-semibold')}>
                      {pickText(i18n.language, item.titleAr, item.titleEn)}
                    </span>
                  )}
                  <span className="text-xs text-muted-foreground">
                    {formatDateTime(item.createdAt, i18n.language)}
                  </span>
                </li>
              ))}
            </ul>
          </Card>
        </div>
      </div>
    </div>
  );
}
