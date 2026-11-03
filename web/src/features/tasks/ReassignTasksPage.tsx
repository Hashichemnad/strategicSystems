import { getRouteApi } from '@tanstack/react-router';
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
import { useReassignmentPreview, useReassignTasks } from './api';
import { TaskStatusChip } from './TaskChips';

const route = getRouteApi('/app/settings/tasks/reassign');

/**
 * "Reassign open tasks of an employee" (ADR 0040): pick the employee, preview the open tasks, choose who takes them (another employee or the
 * creators), give a reason, confirm. The server checks the permission within its scope, moves the tasks in one transaction, notifies and audits.
 */
export function ReassignTasksPage() {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const { t: to } = useTranslation('org');
  const toast = useToast();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const [employee, setEmployee] = useState<string | undefined>(search.employee);
  const preview = useReassignmentPreview(employee);
  const reassign = useReassignTasks();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [mode, setMode] = useState<'to-employee' | 'to-creators'>('to-employee');
  const [target, setTarget] = useState<string>();
  const [fallback, setFallback] = useState<string>();
  const [reason, setReason] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [key, setKey] = useState(newIdempotencyKey);
  const [error, setError] = useState<string>();

  useEffect(() => setSelected(new Set(preview.data?.tasks.map((task) => task.id) ?? [])), [preview.data]);
  const tasks = preview.data?.tasks ?? [];
  const chosen = tasks.filter((task) => selected.has(task.id));
  const leaverCreated = chosen.filter((task) => task.createdByLeaver).length;
  const valid =
    chosen.length > 0 &&
    reason.trim().length > 0 &&
    (mode === 'to-employee' ? !!target : leaverCreated === 0 || !!fallback);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('reassign.title')} description={t('reassign.description')} />
      <div className="flex max-w-xl flex-col gap-4 rounded-lg border bg-card p-5">
        <FormField label={t('reassign.employee')} required>
          <EmployeePicker
            value={employee}
            onChange={(value) => {
              setEmployee(value);
              setError(undefined);
              void navigate({ search: { employee: value } });
            }}
          />
        </FormField>
        {preview.data && preview.data.employeeStatus === 'left' && (
          <StatusChip tone="warning" className="self-start">
            {to('employeeStatus.left')}
          </StatusChip>
        )}
      </div>

      {employee && preview.isError && <StatusBanner tone="danger" title={errorMessage(tc, preview.error)} />}
      {employee && preview.isPending && <Skeleton className="h-40" />}
      {preview.data && tasks.length === 0 && <EmptyState title={t('reassign.none')} />}
      {preview.data && tasks.length > 0 && (
        <>
          <section className="flex flex-col gap-3">
            <h2 className="font-semibold">
              {t('reassign.preview')} ({tasks.length})
            </h2>
            <Table data-testid="reassign-preview">
              <TableHead>
                <tr>
                  <TableHeaderCell>{t('reassign.select')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.creator')}</TableHeaderCell>
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
                      {task.createdByLeaver && (
                        <StatusChip tone="info" className="ms-2">
                          {t('reassign.createdByLeaver')}
                        </StatusChip>
                      )}
                    </TableCell>
                    <TableCell>
                      <TaskStatusChip template={task.template} status={task.status} />
                    </TableCell>
                    <TableCell>{pickText(i18n.language, task.creator.nameAr, task.creator.nameEn)}</TableCell>
                    <TableCell>{task.dueDate ? formatDate(task.dueDate, i18n.language) : '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <p className="text-sm text-muted-foreground">{t('reassign.statusRule')}</p>
          </section>

          <form
            className="flex max-w-2xl flex-col gap-4 rounded-lg border bg-card p-5"
            onSubmit={(event) => {
              event.preventDefault();
              setConfirming(true);
            }}
          >
            {error && <StatusBanner tone="danger" title={error} />}
            <FormField label={t('reassign.mode')}>
              <Select
                value={mode}
                onValueChange={(value) => setMode(value as 'to-employee')}
                options={(['to-employee', 'to-creators'] as const).map((value) => ({
                  value,
                  label: t(`reassign.modes.${value}`),
                }))}
              />
            </FormField>
            {mode === 'to-employee' ? (
              <FormField label={t('reassign.newAssignee')} required>
                <EmployeePicker value={target} onChange={setTarget} excludeId={employee} />
              </FormField>
            ) : (
              leaverCreated > 0 && (
                <FormField
                  label={t('reassign.fallback')}
                  required
                  hint={t('reassign.fallbackHint', { count: leaverCreated })}
                >
                  <EmployeePicker value={fallback} onChange={setFallback} excludeId={employee} />
                </FormField>
              )
            )}
            <FormField label={t('reassign.reason')} required>
              <Textarea
                rows={3}
                maxLength={2000}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            </FormField>
            <Button type="submit" className="self-end" disabled={!valid} data-testid="reassign-submit">
              {t('reassign.submit', { count: chosen.length })}
            </Button>
          </form>
        </>
      )}

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('reassign.confirmTitle')}
        description={t('reassign.confirmDescription', { count: chosen.length })}
        confirmLabel={t('reassign.submit', { count: chosen.length })}
        loading={reassign.isPending}
        onConfirm={() =>
          reassign.mutate(
            {
              idempotencyKey: key,
              body: {
                fromEmployeeId: employee ?? '',
                mode,
                toEmployeeId: mode === 'to-employee' ? (target ?? null) : null,
                fallbackEmployeeId: mode === 'to-creators' ? (fallback ?? null) : null,
                reason: reason.trim(),
                taskIds: chosen.map((task) => task.id),
              },
            },
            {
              onSuccess: (result) => {
                toast({
                  title: t('reassign.done', {
                    reassigned: result.reassigned,
                    returned: result.returnedToCreators,
                  }),
                });
                setConfirming(false);
                setReason('');
                setKey(newIdempotencyKey());
                void preview.refetch();
              },
              onError: (failure) => {
                setConfirming(false);
                setError(errorMessage(tc, failure));
              },
            },
          )
        }
      />
    </div>
  );
}
