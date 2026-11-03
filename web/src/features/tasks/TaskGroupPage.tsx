import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Ban, CalendarClock, MessageSquare } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import { useGroupActions, useTaskGroup } from './api';
import { defaultTasksSearch } from './api';
import { TaskTable } from './TaskViews';

const route = getRouteApi('/app/tasks/groups/$groupId');

type GroupDialog = 'message' | 'extend' | 'cancel';

/** Assignment group (SPEC §11.4): every assignee's task and status, combined progress, and the creator's group actions. */
export function TaskGroupPage() {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const { groupId } = route.useParams();
  const group = useTaskGroup(groupId);
  const [dialog, setDialog] = useState<GroupDialog>();
  if (group.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (group.isError) return <StatusBanner tone="danger" title={errorMessage(tc, group.error)} />;
  const data = group.data;
  const percent = data.total === 0 ? 0 : Math.round((data.finished / data.total) * 100);
  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/tasks"
        search={{ ...defaultTasksSearch, tab: 'created' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader
        title={pickText(i18n.language, data.titleAr, data.titleEn)}
        description={`${t('group.title')} · ${pickText(i18n.language, data.creator.nameAr, data.creator.nameEn)} · ${formatDateTime(data.createdAt, i18n.language)}`}
        meta={
          <>
            <StatusChip tone={data.complete ? 'success' : 'info'}>
              {data.complete ? t('group.complete') : t('group.inProgress')}
            </StatusChip>
            <StatusChip tone="neutral">
              {t('fields.completionRule')}:{' '}
              {t(`completionRule.${data.completionRule}` as 'completionRule.all')}
            </StatusChip>
            {data.cancelled > 0 && (
              <StatusChip tone="neutral">{t('group.cancelledCount', { count: data.cancelled })}</StatusChip>
            )}
          </>
        }
        actions={
          data.canManage && (
            <>
              <Button variant="outline" onClick={() => setDialog('message')}>
                <MessageSquare aria-hidden />
                {t('group.messageAll')}
              </Button>
              <Button variant="outline" onClick={() => setDialog('extend')}>
                <CalendarClock aria-hidden />
                {t('group.extendDue')}
              </Button>
              <Button variant="danger" onClick={() => setDialog('cancel')}>
                <Ban aria-hidden />
                {t('group.cancelAll')}
              </Button>
            </>
          )
        }
      />
      <section
        className="flex flex-col gap-2 rounded-lg border bg-card p-5"
        aria-label={t('group.progress', { finished: data.finished, total: data.total })}
      >
        <div className="flex items-center justify-between text-sm">
          <span className="font-semibold" data-testid="group-progress">
            {t('group.progress', { finished: data.finished, total: data.total })}
          </span>
          <span className="tabular-nums text-muted-foreground">{percent}%</span>
        </div>
        <div
          className="h-3 overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div
            className="h-full rounded-full bg-emerald-500 transition-all"
            style={{ inlineSize: `${percent}%` }}
          />
        </div>
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="font-semibold">{t('group.members')}</h2>
        <TaskTable tasks={data.members.map((m) => m.task)} showCreator={false} />
        {data.hidden > 0 && (
          <p className="text-sm text-muted-foreground">{t('group.hidden', { count: data.hidden })}</p>
        )}
      </section>
      {dialog && <GroupActionDialog groupId={data.id} kind={dialog} onClose={() => setDialog(undefined)} />}
    </div>
  );
}

function GroupActionDialog({
  groupId,
  kind,
  onClose,
}: {
  groupId: string;
  kind: GroupDialog;
  onClose: () => void;
}) {
  const { t } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const actions = useGroupActions(groupId);
  const [text, setText] = useState('');
  const [due, setDue] = useState<string>();
  const [error, setError] = useState<string>();
  const mutation = kind === 'message' ? actions.message : kind === 'extend' ? actions.extend : actions.cancel;
  const valid = kind === 'extend' ? !!due : text.trim().length > 0;
  const title =
    kind === 'message'
      ? t('group.messageAll')
      : kind === 'extend'
        ? t('group.extendDue')
        : t('group.cancelAll');
  const done =
    kind === 'message' ? t('group.sent') : kind === 'extend' ? t('group.extended') : t('group.cancelled');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={title}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={kind === 'cancel' ? 'danger' : 'primary'}
              disabled={!valid}
              loading={mutation.isPending}
              onClick={() =>
                (kind === 'extend'
                  ? actions.extend.mutateAsync(due ?? '')
                  : kind === 'message'
                    ? actions.message.mutateAsync(text.trim())
                    : actions.cancel.mutateAsync(text.trim())
                )
                  .then(() => {
                    toast({ title: done });
                    onClose();
                  })
                  .catch((failure: unknown) => setError(errorMessage(tc, failure)))
              }
            >
              {tc('actions.confirm')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {error && <StatusBanner tone="danger" title={error} />}
          {kind === 'extend' ? (
            <FormField label={t('group.newDue')} required>
              <DatePicker value={due} onChange={setDue} />
            </FormField>
          ) : (
            <FormField label={kind === 'message' ? t('group.message') : t('group.reason')} required>
              <Textarea
                rows={4}
                maxLength={kind === 'message' ? 4000 : 2000}
                value={text}
                onChange={(e) => setText(e.target.value)}
              />
            </FormField>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
