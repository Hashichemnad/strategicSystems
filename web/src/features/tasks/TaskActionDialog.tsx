import { Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Select,
  StatusBanner,
  Textarea,
  useToast,
} from '@/ui';
import { useSummariseTask, useTaskAction, type TaskAction } from './api';

/** What the dialog needs of a task (list items and the task page both have it). */
export interface ActionTarget {
  id: string;
  version: number;
  template: string;
}

const reasonActions = new Set<TaskAction>(['reject', 'pause', 'cancel']);
const commentActions = new Set<TaskAction>(['submit', 'complete', 'approve', 'return', 'reassign']);

/** Actions that need no input: run at once from quick actions and Kanban moves. */
export const instantActions = new Set<TaskAction>(['accept', 'start', 'assign']);

/**
 * One lifecycle action (SPEC §11.3) with the inputs the server requires: a reason to reject / pause / cancel, a comment to return, the
 * feedback point to approve, the new assignee to reassign. The server checks everything again.
 */
export function TaskActionDialog({
  task,
  action,
  feedbackScaleMax = 5,
  onClose,
}: {
  task: ActionTarget;
  action: TaskAction;
  feedbackScaleMax?: number;
  onClose: () => void;
}) {
  const { t } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const act = useTaskAction();
  const summarise = useSummariseTask(task.id);
  const aiSummary = useAiFeature('task-summary');
  const [reason, setReason] = useState('');
  const [comment, setComment] = useState('');
  const [feedback, setFeedback] = useState<string>();
  const [assignee, setAssignee] = useState<string>();
  const [error, setError] = useState<string>();
  const needsReason = reasonActions.has(action);
  const needsComment = commentActions.has(action);
  const draftMode = action === 'submit' ? 'submission' : action === 'return' ? 'return' : undefined;
  const valid =
    (!needsReason || reason.trim().length > 0) &&
    (action !== 'return' || comment.trim().length > 0) &&
    (action !== 'approve' || feedback !== undefined) &&
    (action !== 'reassign' || assignee !== undefined);

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t(`actionDialog.${action}`)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              data-testid="task-action-confirm"
              variant={action === 'reject' || action === 'cancel' ? 'danger' : 'primary'}
              disabled={!valid}
              loading={act.isPending}
              onClick={() =>
                act.mutate(
                  {
                    task,
                    action,
                    body: {
                      reason: needsReason ? reason.trim() : null,
                      comment: comment.trim() || null,
                      feedbackPoint: feedback ? Number(feedback) : null,
                      assigneeEmployeeId: assignee ?? null,
                    },
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.acted') });
                      onClose();
                    },
                    onError: (failure) => setError(errorMessage(tc, failure)),
                  },
                )
              }
            >
              {t(`actions.${action}`)}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {error && <StatusBanner tone="danger" title={error} />}
          {action === 'reassign' && (
            <FormField label={t('actionDialog.newAssignee')} required>
              <EmployeePicker value={assignee} onChange={setAssignee} />
            </FormField>
          )}
          {action === 'approve' && (
            <FormField
              label={t('fields.feedbackPoint')}
              required
              hint={t('actionDialog.feedbackHint', { max: feedbackScaleMax })}
            >
              <Select
                value={feedback}
                onValueChange={setFeedback}
                aria-label={t('fields.feedbackPoint')}
                options={Array.from({ length: feedbackScaleMax }, (_, i) => ({
                  value: String(i + 1),
                  label: String(i + 1),
                }))}
              />
            </FormField>
          )}
          {needsReason && (
            <FormField label={t('actionDialog.reason')} required>
              <Textarea
                rows={3}
                maxLength={2000}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            </FormField>
          )}
          {needsComment && (
            <FormField label={t('actionDialog.comment')} required={action === 'return'}>
              <Textarea
                rows={4}
                maxLength={4000}
                value={comment}
                onChange={(e) => setComment(e.target.value)}
              />
            </FormField>
          )}
          {draftMode && aiSummary && (
            <Button
              variant="outline"
              size="sm"
              className="self-start"
              loading={summarise.isPending}
              onClick={() =>
                summarise.mutate(draftMode, {
                  onSuccess: (result) => setComment(result.text),
                  onError: (failure) => setError(errorMessage(tc, failure)),
                })
              }
            >
              <Sparkles aria-hidden />
              {t('actionDialog.draftWithAi')}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
