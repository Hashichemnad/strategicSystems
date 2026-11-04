import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, ClipboardCheck } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  EmptyState,
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
import {
  reviewDecisions,
  useCompleteReview,
  useReview,
  useSetDecision,
  type PortfolioReviewProject,
  type ReviewDecision,
} from './governanceApi';

const route = getRouteApi('/app/portfolio/reviews/$reviewId');

/** One quarterly portfolio review: a decision per project; completing it starts change requests (ADR 0116). */
export function ReviewPage() {
  const { reviewId } = route.useParams();
  const { t, i18n } = useTranslation('portfolioGov');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const review = useReview(reviewId);
  const complete = useCompleteReview(reviewId);
  const [confirming, setConfirming] = useState(false);

  if (review.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (review.isError) return <StatusBanner tone="danger" title={errorMessage(tc, review.error)} />;
  const data = review.data;
  const editable = data.canManage && data.status === 'draft';

  return (
    <div className="flex flex-col gap-6" data-testid="review-page" data-status={data.status}>
      <Link to="/portfolio/reviews" className="inline-flex items-center gap-1 text-sm text-muted-foreground">
        <ArrowLeft aria-hidden className="size-4 rtl:rotate-180" />
        {t('review.back')}
      </Link>
      <PageHeader
        title={
          <span className="flex flex-wrap items-center gap-2">
            <span dir="ltr">{data.period}</span>
            {data.title && <span>· {data.title}</span>}
            <StatusChip tone={data.status === 'completed' ? 'success' : 'info'}>
              {t(data.status === 'completed' ? 'reviews.statuses.completed' : 'reviews.statuses.draft')}
            </StatusChip>
          </span>
        }
        description={
          [data.reviewDate ? formatDate(data.reviewDate, i18n.language) : null, data.notes]
            .filter(Boolean)
            .join(' · ') || undefined
        }
        actions={
          editable ? (
            <Button data-testid="review-complete" onClick={() => setConfirming(true)}>
              {t('review.complete')}
            </Button>
          ) : undefined
        }
      />
      {data.status === 'completed' && <StatusBanner tone="info" title={t('review.readOnly')} />}
      {complete.isError && <FieldErrors error={complete.error} />}
      {data.projects.length === 0 ? (
        <EmptyState title={t('review.empty')} icon={ClipboardCheck} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('review.project')}</TableHeaderCell>
                <TableHeaderCell>{t('review.stage')}</TableHeaderCell>
                <TableHeaderCell>{t('review.status')}</TableHeaderCell>
                <TableHeaderCell>{t('review.progress')}</TableHeaderCell>
                <TableHeaderCell>{t('review.budget')}</TableHeaderCell>
                <TableHeaderCell className="min-w-72">{t('review.decision')}</TableHeaderCell>
                <TableHeaderCell>{t('review.changeRequest')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.projects.map((p) => (
                <ProjectDecisionRow key={p.projectId} reviewId={reviewId} project={p} editable={editable} />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('review.completeTitle')}
        description={t('review.completeDescription')}
        confirmLabel={t('review.complete')}
        loading={complete.isPending}
        onConfirm={() =>
          complete.mutate(undefined, {
            onSuccess: () => {
              setConfirming(false);
              toast({ title: t('review.completed') });
            },
            onError: () => setConfirming(false),
          })
        }
      />
    </div>
  );
}

function ProjectDecisionRow({
  reviewId,
  project,
  editable,
}: {
  reviewId: string;
  project: PortfolioReviewProject;
  editable: boolean;
}) {
  const { t, i18n } = useTranslation('portfolioGov');
  const { t: tp } = useTranslation('portfolio');
  const toast = useToast();
  const language = i18n.language;
  const save = useSetDecision(reviewId);
  const NONE = 'none';
  const [decision, setDecision] = useState<ReviewDecision | undefined>(
    (project.decision as ReviewDecision | null) ?? undefined,
  );
  const [comment, setComment] = useState(project.comment ?? '');
  const needsComment = decision !== undefined && decision !== 'continue';
  const missingComment = needsComment && !comment.trim();
  const changed = decision !== (project.decision ?? undefined) || comment !== (project.comment ?? '');
  const percent = (ratio: number) =>
    t('percent', { value: formatNumber(Math.round(ratio * 1000) / 10, language) });

  return (
    <TableRow data-testid="review-project-row" data-project={project.number}>
      <TableCell>
        <Link
          to="/portfolio/projects/$projectId"
          params={{ projectId: project.projectId }}
          search={{ tab: 'overview' }}
          className="font-medium underline-offset-4 hover:underline"
        >
          <span dir="ltr">{project.number}</span>
        </Link>
        <span className="block text-xs text-muted-foreground">
          {pickText(language, project.nameAr, project.nameEn)}
        </span>
      </TableCell>
      <TableCell>
        {tp(`projectStages.${project.stage}` as 'projectStages.planning', { defaultValue: project.stage })}
      </TableCell>
      <TableCell>
        <StatusChip tone={project.status === 'active' ? 'info' : 'neutral'}>
          {tp(`projectStatus.${project.status}` as 'projectStatus.active', { defaultValue: project.status })}
        </StatusChip>
      </TableCell>
      <TableCell className="tabular-nums">
        {percent(project.performance)}
        <span className="block text-xs text-muted-foreground">
          {project.progressStatus === 'delayed'
            ? tp('project.progress.delayed')
            : tp('project.progress.onTrack')}
        </span>
      </TableCell>
      <TableCell className="tabular-nums">
        {project.budget === null ? '—' : formatNumber(Math.round(project.budget * 100) / 100, language)}
      </TableCell>
      <TableCell>
        {editable ? (
          <div className="flex flex-col gap-2">
            <Select
              aria-label={t('review.decision')}
              value={decision ?? NONE}
              onValueChange={(v) => setDecision(v === NONE ? undefined : (v as ReviewDecision))}
              options={[
                { value: NONE, label: t('review.noDecision') },
                ...reviewDecisions.map((d) => ({ value: d, label: t(`review.decisions.${d}`) })),
              ]}
            />
            {decision !== undefined && (
              <Textarea
                aria-label={t('review.comment')}
                aria-invalid={missingComment || undefined}
                placeholder={t('review.comment')}
                value={comment}
                maxLength={4000}
                rows={2}
                onChange={(e) => setComment(e.target.value)}
              />
            )}
            {missingComment && <span className="text-xs text-danger">{t('review.commentRequired')}</span>}
            {save.isError && <FieldErrors error={save.error} />}
            {decision !== undefined && changed && (
              <Button
                size="sm"
                variant="outline"
                className="self-start"
                data-testid="review-decision-save"
                loading={save.isPending}
                disabled={missingComment}
                onClick={() =>
                  save.mutate(
                    { projectId: project.projectId, decision, comment: comment.trim() || null },
                    { onSuccess: () => toast({ title: t('review.saved') }) },
                  )
                }
              >
                {t('review.save')}
              </Button>
            )}
          </div>
        ) : project.decision ? (
          <div className="flex flex-col gap-1">
            <StatusChip tone={project.decision === 'continue' ? 'success' : 'warning'}>
              {t(`review.decisions.${project.decision as ReviewDecision}`, {
                defaultValue: project.decision,
              })}
            </StatusChip>
            {project.comment && <span className="text-xs text-muted-foreground">{project.comment}</span>}
          </div>
        ) : (
          <span className="text-muted-foreground">{t('review.noDecision')}</span>
        )}
      </TableCell>
      <TableCell>
        {project.changeRequestId ? (
          <Link
            to="/portfolio/projects/$projectId"
            params={{ projectId: project.projectId }}
            search={{ tab: 'changes' }}
            className="underline-offset-4 hover:underline"
            data-testid="review-change-request"
          >
            <span dir="ltr">{project.changeRequestNumber ?? '—'}</span>
            {project.changeRequestStatus && (
              <span className="block text-xs text-muted-foreground">
                {tp(
                  `project.change.statuses.${project.changeRequestStatus}` as 'project.change.statuses.draft',
                  {
                    defaultValue: project.changeRequestStatus,
                  },
                )}
              </span>
            )}
          </Link>
        ) : (
          '—'
        )}
      </TableCell>
    </TableRow>
  );
}
