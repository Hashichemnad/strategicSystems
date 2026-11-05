import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
} from '@/ui';
import { FieldErrors } from '../project/FieldErrors';
import { useAddWaselComment, useWaselComments } from './waselApi';

export interface CommentSubject {
  type: 'milestone' | 'submission' | 'proposal' | 'deliverable';
  id: string;
  label: string;
}

/** The thread between the PM and the vendor on one subject (append-only, SPEC §1.18 "Communication"). */
export function CommentsDialog({
  projectId,
  subject,
  canWrite,
  onClose,
}: {
  projectId: string;
  subject: CommentSubject;
  canWrite: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const comments = useWaselComments(projectId, subject.type, subject.id);
  const add = useAddWaselComment(projectId, subject.type, subject.id);
  const [body, setBody] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('comments.title')}
        description={subject.label}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            {canWrite && (
              <Button
                disabled={body.trim() === ''}
                loading={add.isPending}
                data-testid="wasel-comment-send"
                onClick={() => add.mutate(body.trim(), { onSuccess: () => setBody('') })}
              >
                {t('comments.send')}
              </Button>
            )}
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="wasel-comments">
          {comments.isPending ? (
            <Skeleton className="h-20" />
          ) : comments.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, comments.error)} />
          ) : comments.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('comments.empty')}</p>
          ) : (
            <ol className="flex max-h-80 flex-col gap-2 overflow-y-auto">
              {comments.data.map((c) => (
                <li
                  key={c.id}
                  className={`rounded-lg border p-2 ${c.authorKind === 'vendor' ? 'bg-muted' : 'bg-card'}`}
                  data-testid="wasel-comment"
                >
                  <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <StatusChip tone={c.authorKind === 'vendor' ? 'info' : 'neutral'}>
                      {t(`comments.${c.authorKind === 'vendor' ? 'vendor' : 'internal'}`)}
                    </StatusChip>
                    <span dir="auto">{c.authorName}</span>
                    <span>{formatDateTime(c.createdAt, i18n.language)}</span>
                  </p>
                  <p dir="auto" className="mt-1 whitespace-pre-wrap text-sm">
                    {c.body}
                  </p>
                </li>
              ))}
            </ol>
          )}
          {canWrite && (
            <FormField label={t('comments.new')}>
              <Textarea dir="auto" value={body} maxLength={4000} onChange={(e) => setBody(e.target.value)} />
            </FormField>
          )}
          {add.error && <FieldErrors error={add.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}
