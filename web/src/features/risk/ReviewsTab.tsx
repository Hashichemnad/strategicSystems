import { ClipboardCheck, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  FormField,
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
import {
  reviewTone,
  riskApi,
  useControls,
  useReviews,
  useRiskMutation,
  useRiskOptions,
  type Review,
  type ReviewAssist,
  type ReviewValues,
  type Risk,
} from './api';
import { RatingChip, RiskMatrix, Section, useAxisLabels, useNames, useRiskLabels, useRiskT } from './components';
import { previewRating } from './scoring';

const NONE = '-';

export function ReviewsTab({ risk }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const reviews = useReviews(risk.id);
  const toast = useToast();
  const start = useRiskMutation(riskApi.startReview);
  const open = reviews.data?.find((r) => r.canEdit && (r.status === 'draft' || r.status === 'returned'));
  return (
    <div className="flex flex-col gap-4" data-testid="risk-reviews">
      {open ? (
        <ReviewForm key={open.id + open.version} risk={risk} review={open} />
      ) : (
        risk.can.startReview && (
          <Button
            className="self-start"
            loading={start.isPending}
            data-testid="risk-review-start"
            onClick={() => start.mutate(risk.id, { onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) })}
          >
            <ClipboardCheck aria-hidden />
            {k('actions.startReview')}
          </Button>
        )
      )}
      <Section title={t('reviews.history')}>
        {!reviews.data ? (
          <Skeleton className="h-24" />
        ) : reviews.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('reviews.empty')}</p>
        ) : (
          <Table data-testid="risk-reviews-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('reviews.createdAt')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.residual')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.commentary')}</TableHeaderCell>
                <TableHeaderCell>{t('reviews.decision')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {reviews.data.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>{formatDateTime(r.createdAt, language)}</TableCell>
                  <TableCell>
                    <StatusChip tone={reviewTone[r.status] ?? 'neutral'}>{k(`reviewStatus.${r.status}`)}</StatusChip>
                  </TableCell>
                  <TableCell>
                    <RatingChip band={r.previewResidualBand} score={r.previewResidualScore} />
                  </TableCell>
                  <TableCell className="max-w-80 whitespace-pre-line">{r.values.commentary ?? '—'}</TableCell>
                  <TableCell className="max-w-64 whitespace-pre-line">
                    {r.decisionComment ?? '—'}
                    {r.decidedAt && <span className="block text-xs text-muted-foreground">{formatDateTime(r.decidedAt, language)}</span>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
    </div>
  );
}

/** The periodic review of the open draft: residual L / I on the matrix, control ratings, action notes and the commentary. */
function ReviewForm({ risk, review }: { risk: Risk; review: Review }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const toast = useToast();
  const options = useRiskOptions();
  const controls = useControls(risk.id);
  const labels = useRiskLabels();
  const names = useNames();
  const axis = useAxisLabels();
  const canAssist = useCan('risk.ai.review');
  const save = useRiskMutation(riskApi.saveReview);
  const submit = useRiskMutation(riskApi.submitReview);
  const [v, setV] = useState<ReviewValues>({ ...review.values, controlRatings: review.values.controlRatings ?? {} });
  const [assist, setAssist] = useState<ReviewAssist>();
  const [assisting, setAssisting] = useState(false);
  const dirty = JSON.stringify(v) !== JSON.stringify({ ...review.values, controlRatings: review.values.controlRatings ?? {} });
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });
  const framework = options.data?.framework.content;
  const live = framework ? previewRating(framework.scoringMethod, framework.bands, v.residualLikelihood, v.residualImpact) : null;
  async function runAssist() {
    setAssisting(true);
    try {
      setAssist(await riskApi.reviewAssistant(risk.id));
    } catch (e) {
      onError(e);
    } finally {
      setAssisting(false);
    }
  }
  return (
    <Section
      title={t('reviews.formTitle')}
      testId="risk-review-form"
      actions={
        canAssist && (
          <Button variant="outline" size="sm" onClick={() => void runAssist()} loading={assisting} data-testid="risk-ai-review">
            <Sparkles aria-hidden />
            {t('ai.review.action')}
          </Button>
        )
      }
    >
      {review.status === 'returned' && review.decisionComment && (
        <StatusBanner tone="warning" title={t('reviews.returned')} description={review.decisionComment} />
      )}
      <div className="grid gap-6 xl:grid-cols-2">
        <div className="flex flex-col gap-2">
          <header className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="font-semibold">{t('rating.residual')}</h3>
            <span className="flex items-center gap-2 text-sm">
              {live ? (
                <RatingChip band={live.band?.code} score={live.score} testId="risk-review-live" />
              ) : (
                <span className="text-muted-foreground">{t('wizard.computedOnSave')}</span>
              )}
              <span className="text-muted-foreground">{t('reviews.saved')}</span>
              <RatingChip band={review.previewResidualBand} score={review.previewResidualScore} testId="risk-review-preview" />
            </span>
          </header>
          {options.data && (
            <RiskMatrix
              cells={options.data.matrix}
              bands={options.data.framework.content.bands}
              prefix="review"
              label={t('rating.residual')}
              axis={axis}
              value={{ likelihood: v.residualLikelihood, impact: v.residualImpact }}
              onChange={(l, i) => setV((p) => ({ ...p, residualLikelihood: l, residualImpact: i }))}
            />
          )}
        </div>
        <div className="flex flex-col gap-3">
          <h3 className="font-semibold">{t('reviews.controlRatings')}</h3>
          {(controls.data ?? []).filter((c) => c.status === 'active').length === 0 && (
            <p className="text-sm text-muted-foreground">{t('controls.empty')}</p>
          )}
          {(controls.data ?? [])
            .filter((c) => c.status === 'active')
            .map((c) => (
              <FormField key={c.id} label={names.text(c.values.nameAr, c.values.nameEn)}>
                <Select
                  value={v.controlRatings?.[c.id] ?? c.values.effectivenessCode ?? NONE}
                  onValueChange={(value) =>
                    setV((p) => {
                      const next = { ...(p.controlRatings ?? {}) };
                      if (value === NONE) delete next[c.id];
                      else next[c.id] = value;
                      return { ...p, controlRatings: next };
                    })
                  }
                  options={[
                    { value: NONE, label: t('controls.notRated') },
                    ...(framework?.controlEffectiveness ?? []).map((e) => ({ value: e.code, label: labels.effectiveness(e.code) })),
                  ]}
                />
              </FormField>
            ))}
          <FormField label={t('reviews.actionNotes')}>
            <Textarea value={v.actionNotes ?? ''} onChange={(e) => setV((p) => ({ ...p, actionNotes: e.target.value || null }))} />
          </FormField>
          <FormField label={t('reviews.commentary')} required>
            <Textarea
              rows={5}
              value={v.commentary ?? ''}
              onChange={(e) => setV((p) => ({ ...p, commentary: e.target.value || null }))}
              data-testid="risk-review-commentary"
            />
          </FormField>
        </div>
      </div>
      {assist && (
        <div className="flex flex-col gap-2 rounded border bg-muted/30 p-3 text-sm" data-testid="risk-ai-review-result">
          <AiBadge label={t('ai.generatedDraft')} className="self-start" />
          {assist.changes.length > 0 && (
            <>
              <h4 className="font-semibold">{t('ai.review.changes')}</h4>
              <ul className="list-disc ps-5">
                {assist.changes.map((c, index) => (
                  <li key={index}>{c}</li>
                ))}
              </ul>
            </>
          )}
          <h4 className="font-semibold">{t('ai.review.commentary')}</h4>
          <p className="whitespace-pre-line">{assist.commentary}</p>
          <Button size="sm" className="self-start" onClick={() => setV((p) => ({ ...p, commentary: assist.commentary }))} data-testid="risk-ai-review-use">
            {t('ai.use')}
          </Button>
        </div>
      )}
      <div className="flex flex-wrap justify-end gap-2 border-t pt-3">
        <Button
          variant="outline"
          loading={save.isPending}
          data-testid="risk-review-save"
          onClick={() => save.mutate({ id: review.id, values: v, version: review.version }, { onError })}
        >
          {tc('actions.save')}
        </Button>
        <Button
          loading={submit.isPending || save.isPending}
          disabled={!v.commentary?.trim()}
          data-testid="risk-review-submit"
          onClick={async () => {
            try {
              if (dirty) await save.mutateAsync({ id: review.id, values: v, version: review.version });
              await submit.mutateAsync({ id: review.id, comment: null });
            } catch (e) {
              onError(e);
            }
          }}
        >
          {k('actions.submit')}
        </Button>
      </div>
    </Section>
  );
}
