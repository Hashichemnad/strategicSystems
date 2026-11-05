import { MessageSquare, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
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
import { fromPercent, type ProjectFull, toPercent } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { DiffList } from './DiffList';
import {
  compareRows,
  isEdited,
  type PmSubmission,
  type PmSubmissionItem,
  usePanelMutation,
  type VendorProgressValues,
  type VendorReviewAi,
  waselApi,
} from './waselApi';
import { percent } from './waselFormat';
import { EvidenceLinks, statusTone } from './WaselTab';

/** One vendor submission (§52): each milestone side by side — last approved | vendor | PM — with accept, edit-then-accept or return. */
export function SubmissionReview({
  project,
  submission,
  canManage,
  onComments,
}: {
  project: ProjectFull;
  submission: PmSubmission;
  canManage: boolean;
  onComments: () => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const lang = i18n.language;
  const reviewable = canManage && submission.status === 'submitted';
  const [ai, setAi] = useState<VendorReviewAi | null>(null);
  const [aiLoading, setAiLoading] = useState(false);
  const [returnComment, setReturnComment] = useState<string | null>(null);
  const [sharing, setSharing] = useState(false);
  const runAi = async () => {
    setAiLoading(true);
    try {
      setAi(await waselApi.aiReview(submission.id));
    } catch (e) {
      toast({ tone: 'error', title: errorMessage(tc, e) });
    } finally {
      setAiLoading(false);
    }
  };
  return (
    <article className="rounded-lg border bg-card p-4" data-testid="wasel-submission">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="font-medium">{t('submission.period', { period: submission.period })}</h3>
          <StatusChip tone={statusTone[submission.status] ?? 'neutral'}>
            {t(`submission.status.${submission.status}`, submission.status)}
          </StatusChip>
          <span className="text-sm text-muted-foreground">
            {pickText(lang, submission.vendorNameAr, submission.vendorNameEn)}
          </span>
          {submission.submittedAt && (
            <span className="text-xs text-muted-foreground">
              {t('proposal.submittedAt', { at: formatDateTime(submission.submittedAt, lang) })}
            </span>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          {canManage && (
            <Button
              size="sm"
              variant="outline"
              loading={aiLoading}
              onClick={() => void runAi()}
              data-testid="wasel-ai-review"
            >
              <Sparkles aria-hidden />
              {t('submission.aiReview')}
            </Button>
          )}
          {canManage && submission.status !== 'submitted' && submission.status !== 'draft' && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => setSharing(true)}
              data-testid="wasel-share-outcome"
            >
              {t('submission.share')}
            </Button>
          )}
          <Button size="sm" variant="ghost" aria-label={t('comments.open')} onClick={onComments}>
            <MessageSquare aria-hidden />
          </Button>
        </div>
      </header>
      {submission.sharedNote && (
        <p className="mt-2 text-sm">
          <span className="font-medium">{t('submission.sharedNote')}: </span>
          <span dir="auto">{submission.sharedNote}</span>
        </p>
      )}
      {ai && <AiPanel ai={ai} submission={submission} onUseComment={(c) => setReturnComment(c)} />}
      <div className="mt-3 flex flex-col gap-4">
        {submission.items.map((item) => (
          <ItemReview
            key={item.id}
            project={project}
            submission={submission}
            item={item}
            reviewable={reviewable}
            suggestedComment={returnComment}
          />
        ))}
      </div>
      {sharing && <ShareDialog project={project} submission={submission} onClose={() => setSharing(false)} />}
    </article>
  );
}

function AiPanel({
  ai,
  submission,
  onUseComment,
}: {
  ai: VendorReviewAi;
  submission: PmSubmission;
  onUseComment: (comment: string) => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const code = (id: string) => submission.items.find((i) => i.milestoneId === id)?.code ?? '';
  const comment =
    i18n.language === 'ar'
      ? (ai.returnCommentAr ?? ai.returnCommentEn)
      : (ai.returnCommentEn ?? ai.returnCommentAr);
  return (
    <section className="mt-3 rounded-lg border border-dashed p-3" data-testid="wasel-ai-panel">
      <p className="text-xs font-medium text-muted-foreground">{t('submission.aiDraft')}</p>
      {ai.flags.length === 0 ? (
        <p className="text-sm">{t('submission.noFlags')}</p>
      ) : (
        <ul className="mt-1 flex flex-col gap-1 text-sm">
          {ai.flags.map((f) => (
            <li key={`${f.milestoneId}-${f.flag}`} data-testid="wasel-ai-flag">
              <StatusChip tone="warning">{code(f.milestoneId)}</StatusChip> {t(`flags.${f.flag}`, f.flag)}
            </li>
          ))}
        </ul>
      )}
      {ai.summary && (
        <p dir="auto" className="mt-2 text-sm">
          {ai.summary}
        </p>
      )}
      {comment && (
        <div className="mt-2 flex flex-wrap items-start gap-2">
          <p dir="auto" className="flex-1 text-sm italic">
            {comment}
          </p>
          <Button
            size="sm"
            variant="outline"
            onClick={() => onUseComment(comment)}
            data-testid="wasel-ai-use-comment"
          >
            {t('submission.useComment')}
          </Button>
        </div>
      )}
      {!ai.aiUsed && <p className="mt-1 text-xs text-muted-foreground">{t('submission.aiUnavailable')}</p>}
    </section>
  );
}

function ItemReview({
  project,
  submission,
  item,
  reviewable,
  suggestedComment,
}: {
  project: ProjectFull;
  submission: PmSubmission;
  item: PmSubmissionItem;
  reviewable: boolean;
  suggestedComment: string | null;
}) {
  const { t, i18n } = useTranslation('vendors');
  const lang = i18n.language;
  const toast = useToast();
  const accept = usePanelMutation(project.id, waselApi.acceptItem);
  const [pm, setPm] = useState<VendorProgressValues>(() => ({ ...(item.final ?? item.vendor) }));
  const [returning, setReturning] = useState(false);
  const pending = item.reviewStatus === 'pending' && reviewable;
  const rows = compareRows(item, pm);
  const edited = isEdited(item, pm);
  const show = (field: string, value: string | number | null) =>
    value === null || value === ''
      ? '—'
      : field === 'achieve'
        ? percent(Number(value), lang)
        : field.startsWith('actual')
          ? formatDate(String(value), lang)
          : String(value);
  return (
    <section className="rounded-md border p-3" data-testid="wasel-review-item">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-medium">
          <span className="text-xs text-muted-foreground">{item.code}</span>{' '}
          <span dir="auto">{pickText(lang, item.titleAr, item.titleEn)}</span>{' '}
          <span className="text-xs text-muted-foreground">
            {t('panel.weight')} {percent(item.weight, lang)}
            {item.plannedEnd && ` · ${t('submission.plannedEnd')} ${formatDate(item.plannedEnd, lang)}`}
          </span>
        </p>
        <StatusChip tone={statusTone[item.reviewStatus] ?? 'neutral'}>
          {t(`submission.itemStatus.${item.reviewStatus}`, item.reviewStatus)}
        </StatusChip>
      </header>
      {item.flags.length > 0 && (
        <p className="mt-1 flex flex-wrap gap-1">
          {item.flags.map((f) => (
            <StatusChip key={f} tone="warning">
              {t(`flags.${f}`, f)}
            </StatusChip>
          ))}
        </p>
      )}
      <div className="mt-2 overflow-x-auto">
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('submission.field')}</TableHeaderCell>
              <TableHeaderCell>{t('submission.previous')}</TableHeaderCell>
              <TableHeaderCell>{t('submission.vendorValues')}</TableHeaderCell>
              <TableHeaderCell>{t('submission.pmValues')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((r) => (
              <TableRow
                key={r.field}
                data-testid={`wasel-compare-${r.field}`}
                data-changed={r.changed ? 'true' : 'false'}
              >
                <TableCell className="font-medium">{t(`diffFields.${r.field}`)}</TableCell>
                <TableCell>{show(r.field, r.previous)}</TableCell>
                <TableCell dir="auto">{show(r.field, r.vendor)}</TableCell>
                <TableCell dir="auto" className={r.changed ? 'bg-warning-soft' : undefined}>
                  {pending ? (
                    <PmField
                      field={r.field}
                      value={pm}
                      onChange={(patch) => setPm((c) => ({ ...c, ...patch }))}
                    />
                  ) : (
                    show(r.field, r.pm)
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {item.vendor.progressNote && (
        <p className="mt-2 text-sm">
          <span className="font-medium">{t('submission.progressNote')}: </span>
          <span dir="auto">{item.vendor.progressNote}</span>
        </p>
      )}
      <div className="mt-2 text-sm">
        <span className="font-medium">{t('submission.evidence')}: </span>
        <EvidenceLinks files={item.evidence} />
      </div>
      {item.diff.length > 0 && (
        <div className="mt-2">
          <p className="text-sm font-medium">{t('submission.pmChanges')}</p>
          <DiffList entries={item.diff} />
        </div>
      )}
      {item.pmComment && (
        <StatusBanner
          tone="warning"
          title={t('submission.returnedWith')}
          description={item.pmComment}
          className="mt-2"
        />
      )}
      {pending && (
        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            loading={accept.isPending}
            data-testid={edited ? 'wasel-item-edit-accept' : 'wasel-item-accept'}
            onClick={() =>
              accept.mutate(
                {
                  submissionId: submission.id,
                  itemId: item.id,
                  values: edited ? pm : null,
                  version: item.version,
                },
                {
                  onSuccess: () =>
                    toast({ title: edited ? t('submission.editedAccepted') : t('submission.accepted') }),
                },
              )
            }
          >
            {edited ? t('submission.editAccept') : t('actions.accept')}
          </Button>
          <Button variant="outline" onClick={() => setReturning(true)} data-testid="wasel-item-return">
            {t('actions.returnToVendor')}
          </Button>
          {edited && (
            <Button variant="ghost" onClick={() => setPm({ ...item.vendor })}>
              {t('submission.resetToVendor')}
            </Button>
          )}
        </div>
      )}
      {accept.error && <FieldErrors error={accept.error} />}
      {returning && (
        <ReturnItemDialog
          project={project}
          submission={submission}
          item={item}
          initial={suggestedComment ?? ''}
          onClose={() => setReturning(false)}
        />
      )}
    </section>
  );
}

function PmField({
  field,
  value,
  onChange,
}: {
  field: 'achieve' | 'actualStart' | 'actualEnd' | 'causality' | 'correctiveActions';
  value: VendorProgressValues;
  onChange: (patch: Partial<VendorProgressValues>) => void;
}) {
  const { t } = useTranslation('vendors');
  const label = t(`diffFields.${field}`);
  switch (field) {
    case 'achieve':
      return (
        <Input
          inputMode="decimal"
          className="w-28"
          aria-label={label}
          data-testid="wasel-pm-achieve"
          defaultValue={toPercent(value.achieve)?.toString() ?? ''}
          onBlur={(e) => onChange({ achieve: fromPercent(e.target.value) ?? 0 })}
        />
      );
    case 'actualStart':
    case 'actualEnd':
      return (
        <Input
          type="date"
          aria-label={label}
          value={value[field] ?? ''}
          onChange={(e) => onChange({ [field]: e.target.value || null })}
        />
      );
    default:
      return (
        <Textarea
          dir="auto"
          aria-label={label}
          value={value[field] ?? ''}
          onChange={(e) => onChange({ [field]: e.target.value || null })}
        />
      );
  }
}

function ReturnItemDialog({
  project,
  submission,
  item,
  initial,
  onClose,
}: {
  project: ProjectFull;
  submission: PmSubmission;
  item: PmSubmissionItem;
  initial: string;
  onClose: () => void;
}) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const back = usePanelMutation(project.id, waselApi.returnItem);
  const [comment, setComment] = useState(initial);
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('actions.returnToVendor')}
        description={item.code}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={comment.trim() === ''}
              loading={back.isPending}
              data-testid="wasel-item-return-confirm"
              onClick={() =>
                back.mutate(
                  {
                    submissionId: submission.id,
                    itemId: item.id,
                    comment: comment.trim(),
                    version: item.version,
                  },
                  { onSuccess: onClose },
                )
              }
            >
              {t('actions.returnToVendor')}
            </Button>
          </>
        }
      >
        <FormField label={t('panel.comment')} required hint={t('submission.returnHint')}>
          <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
        {back.error && <FieldErrors error={back.error} />}
      </DialogContent>
    </Dialog>
  );
}

function ShareDialog({
  project,
  submission,
  onClose,
}: {
  project: ProjectFull;
  submission: PmSubmission;
  onClose: () => void;
}) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const share = usePanelMutation(project.id, waselApi.share);
  const [note, setNote] = useState(submission.sharedNote ?? '');
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('submission.share')}
        description={t('submission.shareHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={note.trim() === ''}
              loading={share.isPending}
              data-testid="wasel-share-confirm"
              onClick={() =>
                share.mutate({ submissionId: submission.id, note: note.trim() }, { onSuccess: onClose })
              }
            >
              {t('submission.share')}
            </Button>
          </>
        }
      >
        <FormField label={t('submission.sharedNote')} required>
          <Textarea dir="auto" value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
        {share.error && <FieldErrors error={share.error} />}
      </DialogContent>
    </Dialog>
  );
}
