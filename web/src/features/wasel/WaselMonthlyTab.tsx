import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  FormField,
  Input,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  fileUrl,
  formBody,
  useSubmissionHistory,
  useVendorSubmission,
  waselApi,
  waselKeys,
  type DiffEntry,
  type VendorProject,
  type VendorSubmissionItem,
} from './waselApi';
import { diffValue, formatPercent, percentToRatio, ratioToPercent, submissionTones } from './waselLogic';

/** Monthly update in "Vendor then PM" mode (W4.2): assigned milestones, evidence, Lead submits, PM review and diff, outcome. */
export function WaselMonthlyTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const submission = useVendorSubmission(project.id);
  const history = useSubmissionHistory(project.id);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [busy, setBusy] = useState(false);

  if (submission.isPending) return <Skeleton className="mt-4 h-40" />;
  if (submission.error) return <StatusBanner tone="danger" title={errorMessage(tc, submission.error)} />;
  const s = submission.data;

  async function submit() {
    setBusy(true);
    try {
      unwrap(
        await waselApi.POST('/api/wasel/v1/projects/{id}/submission/submit', {
          params: { path: { id: project.id } },
          body: { version: s.version ?? 0 },
          headers: { 'Idempotency-Key': crypto.randomUUID() },
        }),
      );
      await queryClient.invalidateQueries({ queryKey: ['wasel', 'project', project.id] });
      toast({ title: t('monthly.submittedToast') });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6 pt-4" data-testid="wasel-monthly">
      {s.updateMode !== 'vendor-then-pm' && <StatusBanner tone="info" title={t('monthly.pmOnly')} />}
      <div className="flex flex-wrap items-center gap-3 rounded-xl border bg-card p-4 text-sm">
        {s.period ? (
          <>
            <span>{t('monthly.window', { period: s.period })}</span>
            {s.dueAt && (
              <span className="text-muted-foreground">
                {t('monthly.due', { date: formatDateTime(s.dueAt, i18n.language) })}
              </span>
            )}
          </>
        ) : (
          <span>{t('monthly.noWindow')}</span>
        )}
        <span data-testid="wasel-submission-status">
          <StatusChip tone={submissionTones[s.status] ?? 'neutral'}>
            {t(`submissionStatus.${s.status}`, { defaultValue: s.status })}
          </StatusChip>
        </span>
        {s.canSubmit && (
          <Button
            className="ms-auto"
            onClick={() => void submit()}
            loading={busy}
            data-testid="wasel-submit-update"
          >
            {t('monthly.submit')}
          </Button>
        )}
      </div>
      {s.sharedOutcome && (
        <StatusBanner
          tone={s.sharedOutcome === 'approved' ? 'success' : 'warning'}
          title={t(`outcome.${s.sharedOutcome}`, { defaultValue: s.sharedOutcome })}
          description={s.sharedNote ?? undefined}
        />
      )}
      {s.items.length === 0 ? (
        <EmptyState title={t('monthly.noMilestones')} />
      ) : (
        <div className="flex flex-col gap-4">
          {s.items.map((item) => (
            <ItemCard key={item.milestoneId} projectId={project.id} item={item} canEdit={s.canEdit} />
          ))}
        </div>
      )}
      <section className="flex flex-col gap-2">
        <h2 className="font-semibold">{t('monthly.history')}</h2>
        {history.data && history.data.length > 0 ? (
          <ul className="flex flex-col gap-2 text-sm" data-testid="wasel-submission-history">
            {history.data.map((h) => (
              <li key={h.id} className="flex flex-wrap items-center gap-2 rounded-lg border bg-card p-3">
                <span className="font-medium" dir="ltr">
                  {h.period}
                </span>
                <StatusChip tone={submissionTones[h.status] ?? 'neutral'}>
                  {t(`submissionStatus.${h.status}`, { defaultValue: h.status })}
                </StatusChip>
                <span className="text-muted-foreground">{t('monthly.itemsCount', { count: h.items })}</span>
                {h.submittedAt && (
                  <span className="text-muted-foreground">{formatDate(h.submittedAt, i18n.language)}</span>
                )}
                {h.sharedNote && <span className="w-full">{h.sharedNote}</span>}
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">{t('monthly.noHistory')}</p>
        )}
      </section>
    </div>
  );
}

function ItemCard({
  projectId,
  item,
  canEdit,
}: {
  projectId: string;
  item: VendorSubmissionItem;
  canEdit: boolean;
}) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const v = item.values;
  const [achieve, setAchieve] = useState(ratioToPercent(v?.achieve ?? item.approvedAchieve));
  const [start, setStart] = useState(v?.actualStart ?? item.approvedActualStart ?? '');
  const [end, setEnd] = useState(v?.actualEnd ?? '');
  const [note, setNote] = useState(v?.progressNote ?? '');
  const [causality, setCausality] = useState(v?.causality ?? '');
  const [corrective, setCorrective] = useState(v?.correctiveActions ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const locked = item.reviewStatus === 'accepted' || item.reviewStatus === 'edited';
  const editable = canEdit && !locked;
  const refresh = () => queryClient.invalidateQueries({ queryKey: waselKeys.submission(projectId) });

  async function save() {
    setBusy(true);
    try {
      unwrap(
        await waselApi.PUT('/api/wasel/v1/projects/{id}/submission/items/{milestoneId}', {
          params: { path: { id: projectId, milestoneId: item.milestoneId } },
          body: {
            values: {
              achieve: percentToRatio(achieve),
              actualStart: start || null,
              actualEnd: end || null,
              progressNote: note || null,
              causality: causality || null,
              correctiveActions: corrective || null,
            },
            version: item.version,
          },
        }),
      );
      await refresh();
      toast({ title: t('monthly.saved') });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  }

  async function upload() {
    if (!file) return;
    setBusy(true);
    try {
      unwrap(
        await waselApi.POST('/api/wasel/v1/projects/{id}/submission/items/{milestoneId}/evidence', {
          params: { path: { id: projectId, milestoneId: item.milestoneId } },
          ...formBody<{ file: string }>({ file }),
        }),
      );
      setFile(null);
      await refresh();
      toast({ title: t('monthly.uploaded') });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <article
      className="flex flex-col gap-4 rounded-xl border bg-card p-4"
      data-testid="wasel-item"
      data-milestone={item.milestoneId}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs text-muted-foreground" dir="ltr">
          {item.code}
        </span>
        <h3 className="font-semibold">{pickText(i18n.language, item.titleAr, item.titleEn)}</h3>
        <StatusChip tone={submissionTones[item.reviewStatus] ?? 'neutral'}>
          {t(`reviewStatus.${item.reviewStatus}`, { defaultValue: item.reviewStatus })}
        </StatusChip>
        {item.delayed && <StatusChip tone="danger">{t('monthly.delayed')}</StatusChip>}
      </div>
      <dl className="grid grid-cols-2 gap-3 text-sm md:grid-cols-4">
        <div>
          <dt className="text-xs text-muted-foreground">{t('monthly.weight')}</dt>
          <dd>{formatPercent(item.weight, i18n.language)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">{t('monthly.approved')}</dt>
          <dd>{formatPercent(item.approvedAchieve, i18n.language)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">{t('monthly.plannedEnd')}</dt>
          <dd>{item.plannedEnd ? formatDate(item.plannedEnd, i18n.language) : '—'}</dd>
        </div>
      </dl>
      {item.pmComment && (
        <StatusBanner tone="warning" title={t('monthly.pmComment')} description={item.pmComment} />
      )}
      {item.diff.length > 0 && <ValueDiff diff={item.diff} />}
      <div className="grid gap-3 md:grid-cols-3">
        <FormField label={t('monthly.achieve')} hint={t('monthly.achieveHint')}>
          <Input
            type="number"
            min={0}
            step="0.01"
            value={achieve}
            disabled={!editable}
            onChange={(e) => setAchieve(e.target.value)}
            data-testid="wasel-item-achieve"
          />
        </FormField>
        <FormField label={t('monthly.actualStart')}>
          <Input
            type="date"
            value={start}
            disabled={!editable}
            onChange={(e) => setStart(e.target.value)}
            data-testid="wasel-item-start"
          />
        </FormField>
        <FormField label={t('monthly.actualEnd')}>
          <Input
            type="date"
            value={end}
            disabled={!editable}
            onChange={(e) => setEnd(e.target.value)}
            data-testid="wasel-item-end"
          />
        </FormField>
      </div>
      <FormField label={t('monthly.progressNote')}>
        <Textarea
          value={note}
          disabled={!editable}
          onChange={(e) => setNote(e.target.value)}
          data-testid="wasel-item-note"
        />
      </FormField>
      {(item.delayed || causality || corrective) && (
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('monthly.causality')}>
            <Textarea value={causality} disabled={!editable} onChange={(e) => setCausality(e.target.value)} />
          </FormField>
          <FormField label={t('monthly.corrective')}>
            <Textarea
              value={corrective}
              disabled={!editable}
              onChange={(e) => setCorrective(e.target.value)}
            />
          </FormField>
        </div>
      )}
      <div className="flex flex-col gap-2">
        <p className="text-sm font-medium">{t('monthly.evidence')}</p>
        {item.evidence.length > 0 ? (
          <ul className="flex flex-col gap-1 text-sm" data-testid="wasel-item-evidence">
            {item.evidence.map((f) => (
              <li key={f.id}>
                <a className="text-primary underline" href={fileUrl(projectId, f.id)} dir="ltr">
                  {f.name}
                </a>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-xs text-muted-foreground">{t('monthly.evidenceHint')}</p>
        )}
        {editable && (
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="file"
              aria-label={t('monthly.evidence')}
              accept=".pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg,.txt"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              data-testid="wasel-item-file"
            />
            <Button
              variant="outline"
              size="sm"
              disabled={!file || busy}
              onClick={() => void upload()}
              data-testid="wasel-item-upload"
            >
              {t('monthly.upload')}
            </Button>
          </div>
        )}
      </div>
      {editable && (
        <div>
          <Button onClick={() => void save()} loading={busy} data-testid="wasel-item-save">
            {t('monthly.save')}
          </Button>
        </div>
      )}
    </article>
  );
}

/** "Your value → PM value" for each field the PM changed before accepting (ADR 0114). */
export function ValueDiff({ diff }: { diff: readonly DiffEntry[] }) {
  const { t, i18n } = useTranslation('wasel');
  return (
    <div
      className="rounded-lg border border-warning/40 bg-warning-soft/40 p-3 text-sm"
      data-testid="wasel-value-diff"
    >
      <p className="mb-1 font-medium">{t('diff.pmEdited')}</p>
      <ul className="flex flex-col gap-1">
        {diff.map((entry) => (
          <li key={entry.field} data-testid="wasel-diff-entry">
            {t(`diff.field.${entry.field}`, { defaultValue: entry.field })}: {t('diff.yours')}{' '}
            <del>{diffValue(entry.field, entry.vendor, i18n.language)}</del> → {t('diff.pm')}{' '}
            <span className="font-medium">{diffValue(entry.field, entry.pm, i18n.language)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
