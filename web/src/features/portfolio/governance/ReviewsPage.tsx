import { Link, useNavigate } from '@tanstack/react-router';
import { ClipboardCheck, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatDateTime, formatNumber } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
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
import { FieldErrors } from '../project/FieldErrors';
import { quarterOf, quarterOptions, useCreateReview, useReviews } from './governanceApi';

/** Portfolio → Portfolio reviews (SPEC §1.12, docs/design/portfolio.md §59): quarterly reviews with a decision per project. */
export function ReviewsPage() {
  const { t, i18n } = useTranslation('portfolioGov');
  const { t: tc } = useTranslation();
  const language = i18n.language;
  const reviews = useReviews();
  // The server checks portfolio.review.manage again on every change.
  const canManage = useCan('portfolio.review.manage');
  const [creating, setCreating] = useState(false);

  return (
    <div className="flex flex-col gap-6" data-testid="reviews-page">
      <PageHeader
        title={t('reviews.title')}
        description={t('reviews.description')}
        actions={
          canManage ? (
            <Button data-testid="review-new" onClick={() => setCreating(true)}>
              <Plus aria-hidden />
              {t('reviews.new')}
            </Button>
          ) : undefined
        }
      />
      {reviews.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : reviews.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, reviews.error)} />
      ) : reviews.data.length === 0 ? (
        <EmptyState title={t('reviews.empty')} icon={ClipboardCheck} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('reviews.period')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.titleField')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.reviewDate')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.status')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.decisions')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.completedAt')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {reviews.data.map((r) => (
                <TableRow key={r.id} data-testid="review-row">
                  <TableCell>
                    <Link
                      to="/portfolio/reviews/$reviewId"
                      params={{ reviewId: r.id }}
                      className="font-medium underline-offset-4 hover:underline"
                      aria-label={t('reviews.open', { period: r.period })}
                    >
                      <span dir="ltr">{r.period}</span>
                    </Link>
                  </TableCell>
                  <TableCell>{r.title ?? '—'}</TableCell>
                  <TableCell>{r.reviewDate ? formatDate(r.reviewDate, language) : '—'}</TableCell>
                  <TableCell>
                    <StatusChip tone={r.status === 'completed' ? 'success' : 'info'}>
                      {t(r.status === 'completed' ? 'reviews.statuses.completed' : 'reviews.statuses.draft')}
                    </StatusChip>
                  </TableCell>
                  <TableCell className="tabular-nums">{formatNumber(r.decisions, language)}</TableCell>
                  <TableCell>{r.completedAt ? formatDateTime(r.completedAt, language) : '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {creating && <NewReviewDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

function NewReviewDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('portfolioGov');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const create = useCreateReview();
  const today = new Date();
  const [period, setPeriod] = useState(quarterOf(today));
  const [title, setTitle] = useState('');
  const [reviewDate, setReviewDate] = useState<string | undefined>(undefined);
  const [notes, setNotes] = useState('');
  const submit = () =>
    create.mutate(
      { period, title: title.trim() || null, reviewDate: reviewDate ?? null, notes: notes.trim() || null },
      {
        onSuccess: (review) => {
          toast({ title: t('reviews.created') });
          onClose();
          void navigate({ to: '/portfolio/reviews/$reviewId', params: { reviewId: review.id } });
        },
      },
    );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('reviews.new')}
        className="max-w-xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending} data-testid="review-create" onClick={submit}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid="review-dialog">
          <FormField label={t('reviews.period')} required>
            <Select
              value={period}
              onValueChange={setPeriod}
              options={quarterOptions(today).map((q) => ({ value: q, label: q }))}
            />
          </FormField>
          <FormField label={t('reviews.reviewDate')}>
            <DatePicker value={reviewDate} onChange={setReviewDate} />
          </FormField>
          <FormField label={t('reviews.titleField')} className="md:col-span-2">
            <Input value={title} maxLength={300} onChange={(e) => setTitle(e.target.value)} />
          </FormField>
          <FormField label={t('reviews.notes')} className="md:col-span-2">
            <Textarea value={notes} maxLength={4000} onChange={(e) => setNotes(e.target.value)} />
          </FormField>
          {create.isError && (
            <div className="md:col-span-2">
              <FieldErrors error={create.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
