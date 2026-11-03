import { useQuery } from '@tanstack/react-query';
import { Plus, Sparkles, Users, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useAiFeature } from '@/features/ai/api';
import { LinkBadge, type LinkView } from '@/features/links/LinkBadge';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { useLookupOptions } from '@/features/settings/lookups';
import { newIdempotencyKey } from '@/features/workflow/api';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FileDrop,
  FormField,
  Input,
  Select,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import { useCreateTask, useSuggestAssignees, type CreateTaskInput } from './api';
import { FromTextDialog } from './FromTextDialog';

/** Upload one file to a task through the generic attachments API (the server checks the upload permission). */
export async function uploadTaskFile(taskId: string, file: File) {
  const form = new FormData();
  form.append('file', file);
  return unwrap(
    await api.POST('/api/v1/files/owners/{ownerType}/{ownerId}', {
      params: { path: { ownerType: 'tasks.task', ownerId: taskId } },
      body: form as unknown as { file: string },
      bodySerializer: (body: unknown) => body as FormData,
    }),
  );
}

function usePersonNames(ids: string[]) {
  return useQuery({
    queryKey: ['org', 'employees', 'names', ids],
    queryFn: async ({ signal }) =>
      Promise.all(
        ids.map(async (id) =>
          unwrap(await api.GET('/api/v1/org/employees/{id}', { params: { path: { id } }, signal })),
        ),
      ),
    enabled: ids.length > 0,
  });
}

/**
 * New Task (SPEC §11.7): one or several assignees — each gets an independent task tied by an assignment group (§11.4) — type, source,
 * category, priority, dates, description, checklist, files, recurrence, confidentiality, linked record and "on behalf of" for secretaries.
 */
export function NewTaskDialog({
  open,
  onOpenChange,
  link,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  link?: { type: string; id: string; view?: LinkView | undefined } | undefined;
  onCreated?: ((ids: string[], groupId: string | null | undefined) => void) | undefined;
}) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const create = useCreateTask();
  const suggest = useSuggestAssignees();
  const canOnBehalf = useCan('tasks.task.on-behalf');
  const aiFromText = useAiFeature('tasks-from-text');
  const aiAssignee = useAiFeature('task-assignee');
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey);
  const [titleAr, setTitleAr] = useState('');
  const [titleEn, setTitleEn] = useState('');
  const [description, setDescription] = useState('');
  const [typeCode, setTypeCode] = useState('manager-assignment');
  const [sourceCode, setSourceCode] = useState('manager-assignment');
  const [categoryCode, setCategoryCode] = useState('-');
  const [priority, setPriority] = useState('medium');
  const [assignees, setAssignees] = useState<string[]>([]);
  const [completionRule, setCompletionRule] = useState('all');
  const [plannedStart, setPlannedStart] = useState<string>();
  const [dueDate, setDueDate] = useState<string>();
  const [confidential, setConfidential] = useState(false);
  const [checklist, setChecklist] = useState<string[]>([]);
  const [checkText, setCheckText] = useState('');
  const [frequency, setFrequency] = useState('none');
  const [count, setCount] = useState('4');
  const [onBehalf, setOnBehalf] = useState<string>();
  const [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState<string>();
  const [fromText, setFromText] = useState(false);
  const types = useLookupOptions('tasks.type', typeCode, '—').filter((o) => o.value !== '-');
  const sources = useLookupOptions('tasks.source', sourceCode, '—').filter((o) => o.value !== '-');
  const categories = useLookupOptions('tasks.category', categoryCode === '-' ? null : categoryCode, '—');
  const names = usePersonNames(assignees);

  function reset() {
    setTitleAr('');
    setTitleEn('');
    setDescription('');
    setAssignees([]);
    setPlannedStart(undefined);
    setDueDate(undefined);
    setConfidential(false);
    setChecklist([]);
    setFrequency('none');
    setOnBehalf(undefined);
    setFiles([]);
    setError(undefined);
    setIdempotencyKey(newIdempotencyKey());
  }

  async function submit(draft: boolean) {
    setError(undefined);
    const body: CreateTaskInput = {
      titleAr: titleAr.trim() || null,
      titleEn: titleEn.trim() || null,
      description: description.trim() || null,
      typeCode,
      sourceCode,
      categoryCode: categoryCode === '-' ? null : categoryCode,
      priority,
      confidential,
      plannedStart: plannedStart ?? null,
      dueDate: dueDate ?? null,
      estimatedHours: null,
      assigneeEmployeeIds: assignees,
      completionRule,
      draft,
      onBehalfOfEmployeeId: onBehalf ?? null,
      linkedType: link?.type ?? null,
      linkedId: link?.id ?? null,
      checklist,
      recurrence:
        frequency === 'none' ? null : { frequency, interval: 1, until: null, count: Number(count) || null },
      dependsOn: null,
    };
    try {
      const created = await create.mutateAsync({ body, idempotencyKey });
      for (const task of created.tasks) {
        for (const file of files) await uploadTaskFile(task.id, file);
      }
      toast({
        title:
          created.tasks.length > 1
            ? t('toast.createdMany', { count: created.tasks.length })
            : t('toast.created'),
      });
      onCreated?.(
        created.tasks.map((task) => task.id),
        created.groupId,
      );
      reset();
      onOpenChange(false);
    } catch (failure) {
      setError(errorMessage(tc, failure));
    }
  }

  const valid = (titleAr.trim() || titleEn.trim()) && assignees.length > 0;
  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          title={t('newTask.title')}
          description={t('newTask.description')}
          className="max-w-3xl"
        >
          <div className="flex flex-col gap-4" data-testid="new-task-form">
            {error && <StatusBanner tone="danger" title={error} />}
            {aiFromText && (
              <Button variant="outline" size="sm" className="self-start" onClick={() => setFromText(true)}>
                <Sparkles aria-hidden />
                {t('newTask.fromText')}
              </Button>
            )}
            {link && (
              <div className="flex flex-col gap-1 rounded-md border bg-muted/40 p-3">
                <span className="text-xs text-muted-foreground">{t('newTask.linkedTo')}</span>
                {link.view ? <LinkBadge view={link.view} /> : <span className="text-sm">{link.type}</span>}
              </div>
            )}
            <div className="grid gap-4 md:grid-cols-2">
              <FormField label={t('fields.titleAr')}>
                <Input
                  dir="rtl"
                  lang="ar"
                  maxLength={300}
                  value={titleAr}
                  onChange={(e) => setTitleAr(e.target.value)}
                />
              </FormField>
              <FormField label={t('fields.titleEn')}>
                <Input
                  dir="ltr"
                  lang="en"
                  maxLength={300}
                  value={titleEn}
                  onChange={(e) => setTitleEn(e.target.value)}
                />
              </FormField>
            </div>
            <FormField label={t('fields.description')}>
              <Textarea
                rows={3}
                maxLength={8000}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </FormField>
            <div className="grid gap-4 md:grid-cols-4">
              <FormField label={t('fields.type')} required>
                <Select value={typeCode} onValueChange={setTypeCode} options={types} />
              </FormField>
              <FormField label={t('fields.source')} required>
                <Select value={sourceCode} onValueChange={setSourceCode} options={sources} />
              </FormField>
              <FormField label={t('fields.category')}>
                <Select value={categoryCode} onValueChange={setCategoryCode} options={categories} />
              </FormField>
              <FormField label={t('fields.priority')}>
                <Select
                  value={priority}
                  onValueChange={setPriority}
                  options={(['high', 'medium', 'low'] as const).map((p) => ({
                    value: p,
                    label: t(`priority.${p}`),
                  }))}
                />
              </FormField>
            </div>

            <fieldset className="flex flex-col gap-3 rounded-md border p-3" data-testid="assignees">
              <legend className="px-1 text-sm font-medium">
                <Users className="me-1 inline size-4" aria-hidden />
                {t('fields.assignees')}
              </legend>
              {assignees.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t('newTask.noAssignees')}</p>
              ) : (
                <ul className="flex flex-wrap gap-2">
                  {assignees.map((id) => {
                    const person = names.data?.find((p) => p.id === id);
                    return (
                      <li
                        key={id}
                        className="flex items-center gap-1 rounded-full border bg-muted px-3 py-1 text-sm"
                      >
                        {person ? pickText(i18n.language, person.nameAr, person.nameEn) : '…'}
                        <button
                          type="button"
                          className="rounded-full p-0.5 hover:bg-background"
                          aria-label={tc('actions.clear')}
                          onClick={() => setAssignees((list) => list.filter((x) => x !== id))}
                        >
                          <X className="size-3" aria-hidden />
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
              <FormField label={t('newTask.addAssignee')}>
                <EmployeePicker
                  value={undefined}
                  onChange={(id) => id && setAssignees((list) => (list.includes(id) ? list : [...list, id]))}
                />
              </FormField>
              {aiAssignee && (titleEn || titleAr) && (
                <div className="flex flex-wrap items-center gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    loading={suggest.isPending}
                    onClick={() => suggest.mutate(titleEn || titleAr)}
                  >
                    <Sparkles aria-hidden />
                    {t('newTask.suggest')}
                  </Button>
                  {suggest.data?.map((s) => (
                    <button
                      key={s.employee.employeeId}
                      type="button"
                      className="rounded-full border px-3 py-1 text-xs hover:bg-muted"
                      onClick={() =>
                        setAssignees((list) =>
                          list.includes(s.employee.employeeId) ? list : [...list, s.employee.employeeId],
                        )
                      }
                      title={s.reasons
                        .map((r) => t(r.replace('tasks.', '') as 'ai.reasons.unit'))
                        .join(' · ')}
                    >
                      {pickText(i18n.language, s.employee.nameAr, s.employee.nameEn)} ·{' '}
                      {t('ai.openTasks', { count: s.openTasks })}
                    </button>
                  ))}
                </div>
              )}
              {assignees.length > 1 && (
                <>
                  <StatusBanner tone="info" title={t('newTask.multiHint', { count: assignees.length })} />
                  <FormField label={t('fields.completionRule')}>
                    <Select
                      value={completionRule}
                      onValueChange={setCompletionRule}
                      options={(['all', 'any'] as const).map((r) => ({
                        value: r,
                        label: t(`completionRule.${r}`),
                      }))}
                    />
                  </FormField>
                </>
              )}
            </fieldset>

            <div className="grid gap-4 md:grid-cols-2">
              <FormField label={t('fields.plannedStart')}>
                <DatePicker value={plannedStart} onChange={setPlannedStart} />
              </FormField>
              <FormField label={t('fields.dueDate')}>
                <DatePicker value={dueDate} onChange={setDueDate} />
              </FormField>
            </div>

            <div className="flex flex-col gap-2">
              <span className="text-sm font-medium">{t('fields.checklist')}</span>
              {checklist.length > 0 && (
                <ul className="flex flex-col gap-1 text-sm">
                  {checklist.map((item, index) => (
                    <li
                      key={`${item}-${index}`}
                      className="flex items-center justify-between rounded border px-2 py-1"
                    >
                      {item}
                      <button
                        type="button"
                        aria-label={tc('actions.clear')}
                        onClick={() => setChecklist((list) => list.filter((_, i) => i !== index))}
                      >
                        <X className="size-3" aria-hidden />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <div className="flex gap-2">
                <Input
                  value={checkText}
                  maxLength={500}
                  placeholder={t('newTask.checklistPlaceholder')}
                  aria-label={t('newTask.checklistPlaceholder')}
                  onChange={(e) => setCheckText(e.target.value)}
                />
                <Button
                  variant="outline"
                  disabled={!checkText.trim()}
                  onClick={() => {
                    setChecklist((list) => [...list, checkText.trim()]);
                    setCheckText('');
                  }}
                >
                  <Plus aria-hidden />
                  {t('newTask.addChecklist')}
                </Button>
              </div>
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <FormField label={t('fields.recurrence')}>
                <Select
                  value={frequency}
                  onValueChange={setFrequency}
                  options={(['none', 'daily', 'weekly', 'monthly'] as const).map((f) => ({
                    value: f,
                    label: t(`recurrence.${f}`),
                  }))}
                />
              </FormField>
              {frequency !== 'none' && (
                <FormField label={t('recurrence.count')}>
                  <Input
                    type="number"
                    min={2}
                    max={366}
                    value={count}
                    onChange={(e) => setCount(e.target.value)}
                  />
                </FormField>
              )}
            </div>

            {canOnBehalf && (
              <FormField label={t('fields.onBehalfOf')}>
                <EmployeePicker value={onBehalf} onChange={setOnBehalf} />
              </FormField>
            )}

            <FileDrop
              label={t('newTask.files')}
              accept=".pdf,.png,.jpg,.jpeg,.docx,.xlsx,.pptx,.txt,.csv"
              file={null}
              onFile={(file) => file && setFiles((list) => [...list, file])}
            />
            {files.length > 0 && (
              <ul className="flex flex-wrap gap-2">
                {files.map((file, index) => (
                  <li key={`${file.name}-${index}`}>
                    <StatusChip tone="neutral">{file.name}</StatusChip>
                  </li>
                ))}
              </ul>
            )}

            <Checkbox
              label={t('fields.confidential')}
              checked={confidential}
              onChange={(e) => setConfidential(e.target.checked)}
            />
          </div>
          <div className="flex flex-wrap justify-end gap-2 border-t pt-4">
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="outline"
              disabled={!valid}
              loading={create.isPending}
              onClick={() => void submit(true)}
            >
              {t('newTask.saveDraft')}
            </Button>
            <Button
              data-testid="create-task"
              disabled={!valid}
              loading={create.isPending}
              onClick={() => void submit(false)}
            >
              {assignees.length > 1
                ? t('newTask.createMany', { count: assignees.length })
                : t('newTask.create')}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
      {fromText && (
        <FromTextDialog
          link={link}
          onClose={() => setFromText(false)}
          onCreated={() => {
            setFromText(false);
            onOpenChange(false);
          }}
        />
      )}
    </>
  );
}
