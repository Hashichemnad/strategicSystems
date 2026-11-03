import { Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { newIdempotencyKey } from '@/features/workflow/api';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Select,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import { useCreateTask, useDraftTasksFromText } from './api';

interface DraftRow {
  include: boolean;
  title: string;
  description: string;
  assignee: string | undefined;
  assigneeName: string | null | undefined;
  dueDate: string | undefined;
  priority: string;
  source: string | null | undefined;
}

/**
 * "Tasks from text" (SPEC §11.10): AI proposes tasks from minutes / an e-mail / a memo; every proposal is an "AI draft" the creator reviews
 * and completes (assignee, date). Nothing is created until the creator confirms (CLAUDE.md rule 6).
 */
export function FromTextDialog({
  link,
  onClose,
  onCreated,
}: {
  link?: { type: string; id: string } | undefined;
  onClose: () => void;
  onCreated: () => void;
}) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const draft = useDraftTasksFromText();
  const create = useCreateTask();
  const [text, setText] = useState('');
  const [rows, setRows] = useState<DraftRow[]>();
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const arabic = i18n.language === 'ar';
  const selected = rows?.filter((r) => r.include && r.title.trim() && r.assignee) ?? [];

  function update(index: number, change: Partial<DraftRow>) {
    setRows((list) => list?.map((row, i) => (i === index ? { ...row, ...change } : row)));
  }

  async function createSelected() {
    setSaving(true);
    setError(undefined);
    try {
      for (const row of selected) {
        await create.mutateAsync({
          idempotencyKey: newIdempotencyKey(),
          body: {
            titleAr: arabic ? row.title.trim() : null,
            titleEn: arabic ? null : row.title.trim(),
            description: row.description.trim() || null,
            typeCode: 'meeting-decision',
            sourceCode: 'committee-decision',
            categoryCode: null,
            priority: row.priority,
            confidential: null,
            plannedStart: null,
            dueDate: row.dueDate ?? null,
            estimatedHours: null,
            assigneeEmployeeIds: row.assignee ? [row.assignee] : [],
            linkedType: link?.type ?? null,
            linkedId: link?.id ?? null,
            draft: false,
          },
        });
      }
      toast({ title: t('toast.createdMany', { count: selected.length }) });
      onCreated();
    } catch (failure) {
      setError(errorMessage(tc, failure));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('fromText.title')}
        description={t('fromText.description')}
        className="max-w-4xl"
      >
        <div className="flex flex-col gap-4" data-testid="from-text">
          {error && <StatusBanner tone="danger" title={error} />}
          <FormField label={t('fromText.text')}>
            <Textarea rows={6} maxLength={30000} value={text} onChange={(e) => setText(e.target.value)} />
          </FormField>
          <Button
            className="self-start"
            variant="outline"
            disabled={!text.trim()}
            loading={draft.isPending}
            onClick={() =>
              draft.mutate(text, {
                onSuccess: (result) =>
                  setRows(
                    result.drafts.map((d) => ({
                      include: true,
                      title: (arabic ? d.titleAr : d.titleEn) ?? d.titleEn ?? d.titleAr ?? '',
                      description: d.description ?? '',
                      assignee: d.assignee?.employeeId,
                      assigneeName: d.assigneeName,
                      dueDate: d.dueDate ?? undefined,
                      priority: d.priority,
                      source: d.source,
                    })),
                  ),
                onError: (failure) => setError(errorMessage(tc, failure)),
              })
            }
          >
            <Sparkles aria-hidden />
            {t('fromText.generate')}
          </Button>
          {rows && rows.length === 0 && (
            <p className="text-sm text-muted-foreground">{t('fromText.empty')}</p>
          )}
          {rows && rows.length > 0 && (
            <ol className="flex flex-col gap-3" aria-label={t('fromText.drafts')}>
              {rows.map((row, index) => (
                <li
                  key={index}
                  className="flex flex-col gap-3 rounded-lg border-2 border-dashed border-amber-400/70 bg-amber-50/40 p-3 dark:bg-amber-950/20"
                  data-testid="task-draft"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <Checkbox
                      label={t('fromText.include')}
                      checked={row.include}
                      onChange={(e) => update(index, { include: e.target.checked })}
                    />
                    <StatusChip tone="warning">
                      <Sparkles className="size-3" aria-hidden />
                      {t('fromText.aiDraft')}
                    </StatusChip>
                  </div>
                  <FormField label={t('fields.title')} required>
                    <Input
                      value={row.title}
                      maxLength={300}
                      onChange={(e) => update(index, { title: e.target.value })}
                    />
                  </FormField>
                  <div className="grid gap-3 md:grid-cols-3">
                    <FormField
                      label={t('fields.assignee')}
                      required
                      hint={row.assigneeName ? t('fromText.named', { name: row.assigneeName }) : undefined}
                    >
                      <EmployeePicker
                        value={row.assignee}
                        onChange={(assignee) => update(index, { assignee })}
                      />
                    </FormField>
                    <FormField label={t('fields.dueDate')}>
                      <DatePicker value={row.dueDate} onChange={(dueDate) => update(index, { dueDate })} />
                    </FormField>
                    <FormField label={t('fields.priority')}>
                      <Select
                        value={row.priority}
                        onValueChange={(priority) => update(index, { priority })}
                        options={(['high', 'medium', 'low'] as const).map((p) => ({
                          value: p,
                          label: t(`priority.${p}`),
                        }))}
                      />
                    </FormField>
                  </div>
                  {row.source && (
                    <p className="text-xs text-muted-foreground">
                      {t('fromText.source', { text: row.source })}
                    </p>
                  )}
                </li>
              ))}
            </ol>
          )}
        </div>
        <div className="flex justify-end gap-2 border-t pt-4">
          <DialogClose asChild>
            <Button variant="outline">{tc('actions.cancel')}</Button>
          </DialogClose>
          <Button disabled={selected.length === 0} loading={saving} onClick={() => void createSelected()}>
            {t('fromText.createSelected', { count: selected.length })}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
