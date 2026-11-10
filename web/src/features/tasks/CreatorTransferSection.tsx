import { Lock } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { newIdempotencyKey } from '@/features/workflow/api';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  EmptyState,
  FormField,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { useTransferCreatorRole, type ReassignmentPreview } from './api';
import { TaskStatusChip } from './TaskChips';

/**
 * "Take over as creator" (ADR 0169): the open tasks an employee created for other people; select them, choose the new creator and give a reason.
 * The server moves the creator role (and the approval of the work) in one transaction, notifies both creators and audits it.
 */
export function CreatorTransferSection({
  employeeId,
  preview,
  onDone,
}: {
  employeeId: string;
  preview: ReassignmentPreview;
  onDone: () => void;
}) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const transfer = useTransferCreatorRole();
  const tasks = preview.createdTasks;
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [target, setTarget] = useState<string>();
  const [reason, setReason] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [key, setKey] = useState(newIdempotencyKey);
  const [error, setError] = useState<string>();
  useEffect(() => setSelected(new Set(tasks.map((task) => task.id))), [tasks]);
  const chosen = tasks.filter((task) => selected.has(task.id));
  const valid = chosen.length > 0 && !!target && reason.trim().length > 0;
  const name = (p: { nameAr?: string | null; nameEn?: string | null }) =>
    pickText(i18n.language, p.nameAr, p.nameEn);

  return (
    <section className="flex flex-col gap-3" data-testid="creator-transfer">
      <h2 className="font-semibold">
        {t('reassign.createdTitle')} ({tasks.length})
      </h2>
      <p className="text-sm text-muted-foreground">{t('reassign.createdHint')}</p>
      {tasks.length === 0 ? (
        <EmptyState title={t('reassign.createdNone')} />
      ) : (
        <>
          <Table data-testid="creator-transfer-preview">
            <TableHead>
              <tr>
                <TableHeaderCell>{t('reassign.select')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('reassign.assignee')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.dueDate')}</TableHeaderCell>
              </tr>
            </TableHead>
            <TableBody>
              {tasks.map((task) => (
                <TableRow key={task.id}>
                  <TableCell>
                    <Checkbox
                      label={<span className="sr-only">{t('reassign.select')}</span>}
                      checked={selected.has(task.id)}
                      onChange={(e) =>
                        setSelected((current) => {
                          const next = new Set(current);
                          if (e.target.checked) next.add(task.id);
                          else next.delete(task.id);
                          return next;
                        })
                      }
                    />
                  </TableCell>
                  <TableCell dir="ltr">{task.number ?? '—'}</TableCell>
                  <TableCell>
                    {task.readable ? (
                      pickText(i18n.language, task.titleAr, task.titleEn)
                    ) : (
                      <span className="flex items-center gap-1 text-muted-foreground">
                        <Lock className="size-3" aria-hidden />
                        {t('reassign.restricted')}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    <TaskStatusChip template={task.template} status={task.status} />
                  </TableCell>
                  <TableCell>{name(task.assignee)}</TableCell>
                  <TableCell>{task.dueDate ? formatDate(task.dueDate, i18n.language) : '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <form
            className="flex max-w-2xl flex-col gap-4 rounded-lg border bg-card p-5"
            onSubmit={(event) => {
              event.preventDefault();
              setConfirming(true);
            }}
          >
            {error && <StatusBanner tone="danger" title={error} />}
            <FormField label={t('reassign.newCreator')} required>
              <EmployeePicker value={target} onChange={setTarget} excludeId={employeeId} />
            </FormField>
            <FormField label={t('reassign.creatorReason')} required>
              <Textarea
                rows={3}
                maxLength={2000}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                data-testid="creator-transfer-reason"
              />
            </FormField>
            <Button
              type="submit"
              className="self-end"
              disabled={!valid}
              data-testid="creator-transfer-submit"
            >
              {t('reassign.creatorSubmit', { count: chosen.length })}
            </Button>
          </form>
        </>
      )}
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('reassign.creatorConfirmTitle')}
        description={t('reassign.creatorConfirmDescription', { count: chosen.length })}
        confirmLabel={t('reassign.creatorSubmit', { count: chosen.length })}
        loading={transfer.isPending}
        onConfirm={() =>
          transfer.mutate(
            {
              idempotencyKey: key,
              body: {
                fromEmployeeId: employeeId,
                toEmployeeId: target ?? null,
                reason: reason.trim(),
                taskIds: chosen.map((task) => task.id),
              },
            },
            {
              onSuccess: (result) => {
                toast({
                  title: t('reassign.creatorDone', {
                    transferred: result.transferred,
                    skipped: result.skipped,
                  }),
                });
                setConfirming(false);
                setReason('');
                setError(undefined);
                setKey(newIdempotencyKey());
                onDone();
              },
              onError: (failure) => {
                setConfirming(false);
                setError(errorMessage(tc, failure));
              },
            },
          )
        }
      />
    </section>
  );
}
