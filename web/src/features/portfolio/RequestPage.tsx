import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { ArrowLeft, Download, Pencil, Send, Upload } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { contentUrl } from '@/features/files/api';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FileDrop,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import { AssessmentPanel } from './AssessmentPanel';
import { useFeature } from './featuresApi';
import { RequestBenefitsPanel } from './standards/BenefitsTab';
import {
  bandTone,
  requestApi,
  requestResource,
  statusTone,
  usePortfolioOptions,
  useRequest,
  useRequestFiles,
  useRequestMutation,
  type PortfolioOptions,
  type ProgramRequest,
} from './api';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/portfolio/requests/$requestId');

export const requestTabs = [
  'overview',
  'assessment',
  'approvals',
  'files',
  'documents',
  'tasks',
  'history',
] as const;

type Dialogs =
  'submit' | 'cancel' | 'override' | 'budget' | 'cost-estimation' | 'project-scope' | 'quotation';

/** Where the request stands on the stepper; steps that the route skips are not shown (DG Office / Budget by budget). */
export function requestStages(
  request: ProgramRequest,
  threshold: number,
): { stages: string[]; current: string } {
  const v = request.values;
  const stages = ['draft', 'manager', 'pm-office'];
  if (v.requiresBudget === true && (v.estimatedBudget ?? 0) > threshold) stages.push('dg-office');
  if (v.requiresBudget === true) stages.push('budget');
  const final =
    request.status === 'rejected' ? 'rejected' : request.status === 'cancelled' ? 'cancelled' : 'approved';
  stages.push(final);
  let current: string;
  switch (request.status) {
    case 'in-approval':
      current =
        request.approvalStepCode === 'pm-office'
          ? 'pm-office'
          : request.approvalStepCode === 'dg-office'
            ? 'dg-office'
            : request.approvalStepCode === 'budget-section'
              ? 'budget'
              : 'manager';
      break;
    case 'draft':
      current = 'draft';
      break;
    default:
      current = final;
  }
  return { stages, current };
}

/** Program request record page (SPEC §1.8): header, stage stepper, current status with next actions, tabs. */
export function RequestPage() {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const { requestId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const go = useNavigate();
  const toast = useToast();
  const request = useRequest(requestId);
  const options = usePortfolioOptions();
  const handOff = useRequestMutation(requestApi.handOff);
  const [dialog, setDialog] = useState<Dialogs>();
  const benefitsOn = useFeature('benefits');
  // The Benefits tab is local state: the route's search schema lists the Phase 8 tabs only.
  const [benefitsTab, setBenefitsTab] = useState(false);
  const { t: ts } = useTranslation('pmStandards');
  if (request.isPending || options.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (request.isError) return <StatusBanner tone="danger" title={errorMessage(tc, request.error)} />;
  if (options.isError) return <StatusBanner tone="danger" title={errorMessage(tc, options.error)} />;
  const data = request.data;
  const lang = i18n.language;
  const name = pickText(lang, data.values.nameAr, data.values.nameEn);
  const { stages, current } = requestStages(data, options.data.thresholdAmount);
  const band = options.data.bands.find((b) => b.code === data.priorityBand);
  const doHandOff = () =>
    handOff.mutate(
      { id: data.id, version: data.version },
      {
        onSuccess: () => toast({ title: t('toast.handedOff') }),
        onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
      },
    );
  const fieldLabels: Record<string, string> = Object.fromEntries(
    Object.keys(data.values).map((key) => [
      key.charAt(0).toUpperCase() + key.slice(1),
      t(`fields.${key}` as 'fields.nameAr', { amount: '', defaultValue: key }),
    ]),
  );

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/portfolio/requests"
        search={{ view: 'all', page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader
        title={<span dir="auto">{name}</span>}
        description={
          <>
            <bdi>{data.number}</bdi>
            {data.unit && <> · {pickText(lang, data.unit.nameAr, data.unit.nameEn)}</>}
          </>
        }
        meta={
          <>
            <OriginBadge type="portfolio.request" id={data.id} />
            <StatusChip tone={statusTone[data.status] ?? 'neutral'}>
              {t(`status.${data.status as 'draft'}`)}
            </StatusChip>
            {data.approvalStatus === 'returned' && data.status === 'draft' && (
              <StatusChip tone="warning">{t('approvalStatus.returned')}</StatusChip>
            )}
            <StatusChip tone="info">
              {t(`classification.${data.effectiveClassification as 'project'}`)}
            </StatusChip>
            {band && (
              <StatusChip tone={bandTone[band.code] ?? 'neutral'}>
                {pickText(lang, band.labelAr, band.labelEn)}
                {data.evaluationScore !== null &&
                  ` · ${formatNumber(Math.round(data.evaluationScore * 10000) / 100, lang)} %`}
              </StatusChip>
            )}
          </>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {data.can.edit && (
              <Button variant="outline" asChild>
                <Link to="/portfolio/requests/$requestId/edit" params={{ requestId: data.id }}>
                  <Pencil aria-hidden />
                  {t('actions.edit')}
                </Link>
              </Button>
            )}
            {data.can.manage && (
              <Button variant="outline" onClick={() => setDialog('override')}>
                {t('actions.override')}
              </Button>
            )}
            {data.can.cancel && (
              <Button variant="outline" onClick={() => setDialog('cancel')}>
                {t('actions.cancel')}
              </Button>
            )}
            {data.project && (
              <Button asChild>
                <Link to="/portfolio/projects/$projectId" params={{ projectId: data.project.id }}>
                  {t('actions.openProject')}
                </Link>
              </Button>
            )}
          </div>
        }
      />
      <StageStepper
        stages={stages.map((key) => ({ key, label: t(`stages.${key as 'draft'}`) }))}
        current={current}
      />
      <CurrentStatusBanner
        resourceType={requestResource}
        resourceId={data.id}
        actionLabel={(code, key) => {
          const known: Record<string, string> = {
            submit: t('actions.submit'),
            resubmit: t('actions.resubmit'),
            'upload-cost-estimation': t('actions.uploadCostEstimation'),
            'upload-scope': t('actions.uploadScope'),
            'hand-off': t('actions.handOff'),
            'upload-quotations': t('actions.uploadQuotations'),
            'approved-budget': t('actions.setApprovedBudget'),
            'open-project': t('actions.openProject'),
          };
          return known[code] ?? tc(key as 'actions.edit', { defaultValue: code });
        }}
        onAction={(code) => {
          if (code === 'submit' || code === 'resubmit') setDialog('submit');
          if (code === 'upload-cost-estimation') setDialog('cost-estimation');
          if (code === 'upload-scope') setDialog('project-scope');
          if (code === 'upload-quotations') setDialog('quotation');
          if (code === 'hand-off') doHandOff();
          if (code === 'approved-budget') setDialog('budget');
          if (code === 'open-project' && data.project)
            void go({ to: '/portfolio/projects/$projectId', params: { projectId: data.project.id } });
        }}
      />
      <Tabs
        value={benefitsOn && benefitsTab ? 'benefits' : tab}
        onValueChange={(value) => {
          if (value === 'benefits') {
            setBenefitsTab(true);
            return;
          }
          setBenefitsTab(false);
          void navigate({ search: { tab: value as 'overview' } });
        }}
      >
        <TabsList className="flex-wrap">
          {requestTabs.map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
          {benefitsOn && (
            <TabsTrigger value="benefits" data-testid="request-tab-benefits">
              {ts('benefits.tab')}
            </TabsTrigger>
          )}
        </TabsList>
        <TabsContent value="overview">
          <Overview request={data} options={options.data} />
          <ExternalReferencesSection type="portfolio.request" id={data.id} />
        </TabsContent>
        <TabsContent value="assessment">
          <AssessmentPanel request={data} options={options.data} />
        </TabsContent>
        <TabsContent value="approvals">
          <ApprovalPanel resourceType={requestResource} resourceId={data.id} />
        </TabsContent>
        <TabsContent value="files">
          <CostFiles request={data} options={options.data} onUpload={setDialog} onHandOff={doHandOff} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType={requestResource}
            ownerId={data.id}
            canEdit={data.can.edit || data.can.manage}
          />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type={requestResource} id={data.id} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="request" id={data.id} fieldLabels={fieldLabels} />
        </TabsContent>
        {benefitsOn && (
          <TabsContent value="benefits">
            <RequestBenefitsPanel requestId={data.id} canEdit={data.can.edit && data.status === 'draft'} />
          </TabsContent>
        )}
      </Tabs>
      {dialog === 'submit' && <SubmitDialog request={data} onClose={() => setDialog(undefined)} />}
      {dialog === 'cancel' && <CancelDialog request={data} onClose={() => setDialog(undefined)} />}
      {dialog === 'override' && <OverrideDialog request={data} onClose={() => setDialog(undefined)} />}
      {dialog === 'budget' && <BudgetDialog request={data} onClose={() => setDialog(undefined)} />}
      {(dialog === 'cost-estimation' || dialog === 'project-scope' || dialog === 'quotation') && (
        <UploadDialog request={data} kind={dialog} onClose={() => setDialog(undefined)} />
      )}
    </div>
  );
}

function Overview({ request, options }: { request: ProgramRequest; options: PortfolioOptions }) {
  const { t, i18n } = useTranslation('portfolio');
  const lang = i18n.language;
  const v = request.values;
  const label = (items: PortfolioOptions['objectives'], code: string | null | undefined) => {
    const item = items.find((o) => o.code === code);
    return item ? pickText(lang, item.labelAr, item.labelEn) : (code ?? '—');
  };
  const money = (value: number | null | undefined) =>
    value === null || value === undefined ? '—' : `${formatNumber(value, lang)} ${options.currencyCode}`;
  const yesNo = (value: boolean | null | undefined) =>
    value === true ? t('values.yes') : value === false ? t('values.no') : '—';
  const date = (value: string | null | undefined) => (value ? formatDate(value, lang) : '—');
  const rows: [string, string][] = [
    [t('fields.strategicObjectiveCode'), label(options.objectives, v.strategicObjectiveCode)],
    [t('fields.programTypeCode'), label(options.programTypes, v.programTypeCode)],
    [
      t('fields.suggestionSourceCode'),
      v.suggestionSourceCode === 'other'
        ? (v.suggestionSourceOther ?? '—')
        : label(options.sources, v.suggestionSourceCode),
    ],
    [
      t('fields.programNature'),
      v.programNature
        ? `${t(`values.${v.programNature as 'new'}`)}${v.previousProgramName ? ` — ${v.previousProgramName}` : ''}`
        : '—',
    ],
    [t('fields.expectedStartDate'), date(v.expectedStartDate)],
    [t('fields.expectedEndDate'), date(v.expectedEndDate)],
    [
      t('fields.timeFrame'),
      request.timeFrameMonths ? t('values.months', { count: request.timeFrameMonths }) : '—',
    ],
    [
      t('fields.requester'),
      request.requester ? pickText(lang, request.requester.nameAr, request.requester.nameEn) : '—',
    ],
    [t('fields.requiresBudget'), yesNo(v.requiresBudget)],
    [t('fields.estimatedBudget'), money(v.requiresBudget ? v.estimatedBudget : null)],
    [t('fields.approvedBudget'), money(request.approvedBudget)],
    [t('fields.source'), t(`values.${request.source as 'manual'}`)],
  ];
  const texts: [string, string | null | undefined][] = [
    [t('fields.description'), v.description],
    [t('fields.justifications'), v.justifications],
    [t('fields.scopeOfWork'), v.scopeOfWork],
  ];
  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <div className="flex flex-col gap-4 rounded-lg border bg-card p-6">
        <dl className="grid gap-4 md:grid-cols-2">
          {rows.map(([key, value]) => (
            <div key={key}>
              <dt className="text-sm text-muted-foreground">{key}</dt>
              <dd className="mt-1" dir="auto">
                {value}
              </dd>
            </div>
          ))}
        </dl>
        {texts.map(([key, value]) => (
          <div key={key}>
            <h3 className="text-sm text-muted-foreground">{key}</h3>
            <p className="mt-1 whitespace-pre-line" dir="auto">
              {value ?? '—'}
            </p>
          </div>
        ))}
        {request.cancelReason && (
          <StatusBanner tone="info" title={t('fields.cancelReason')} description={request.cancelReason} />
        )}
      </div>
      <div className="flex flex-col gap-4">
        <section className="flex flex-col gap-2 rounded-lg border bg-card p-4" data-testid="request-budget">
          <h3 className="font-medium">{t('fields.yearCosts')}</h3>
          {(['year1Cost', 'year2Cost', 'year3Cost', 'year4Cost'] as const).map((key) => (
            <div key={key} className="flex justify-between text-sm">
              <span className="text-muted-foreground">{t(`fields.${key}`)}</span>
              <span className="tabular-nums">{money(v[key])}</span>
            </div>
          ))}
          <div className="flex justify-between border-t pt-2 text-sm font-medium">
            <span>{t('fields.estimatedBudget')}</span>
            <span className="tabular-nums">{money(v.estimatedBudget)}</span>
          </div>
        </section>
        <section
          className="flex flex-col gap-2 rounded-lg border bg-card p-4"
          data-testid="request-screening"
        >
          <h3 className="font-medium">{t('fields.classification')}</h3>
          <p>
            {t(`classification.${request.effectiveClassification as 'project'}`)}
            {request.classificationOverride && (
              <span className="block text-xs text-muted-foreground">
                {t('classification.overridden')} ·{' '}
                {t('classification.computed', {
                  value: t(`classification.${request.classification as 'project'}`),
                })}
                {request.overrideReason && ` · ${request.overrideReason}`}
              </span>
            )}
          </p>
          <h3 className="mt-2 font-medium">{t('fields.priorityScores')}</h3>
          {options.criteria.map((c) => (
            <div key={c.code} className="flex justify-between gap-2 text-sm">
              <span className="text-muted-foreground">{pickText(lang, c.labelAr, c.labelEn)}</span>
              <span className="tabular-nums">
                {v.priorityScores?.[c.code] !== undefined
                  ? formatNumber(v.priorityScores[c.code] ?? 0, lang)
                  : '—'}
              </span>
            </div>
          ))}
        </section>
      </div>
    </div>
  );
}

function CostFiles({
  request,
  options,
  onUpload,
  onHandOff,
}: {
  request: ProgramRequest;
  options: PortfolioOptions;
  onUpload: (kind: Dialogs) => void;
  onHandOff: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const files = useRequestFiles(request.id);
  const draft = request.status === 'draft';
  const costFiles = (files.data ?? []).filter((f) => f.kind !== 'document');
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-6" data-testid="cost-files">
      <h3 className="font-medium">{t('files.title')}</h3>
      {request.procurementStatus && (
        <StatusChip tone={request.procurementStatus === 'quotations-uploaded' ? 'success' : 'info'}>
          {t(`files.procurement.${request.procurementStatus as 'waiting-quotations'}`)}
        </StatusChip>
      )}
      {costFiles.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('files.none')}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {costFiles.map((f) => (
            <li
              key={f.file.id}
              className="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
            >
              <span>
                <StatusChip tone="neutral">{t(`files.kinds.${f.kind as 'quotation'}`)}</StatusChip>{' '}
                <bdi>{f.file.name}</bdi>
                <span className="ms-2 text-xs text-muted-foreground">
                  {formatDateTime(f.file.updatedAt, i18n.language)}
                </span>
              </span>
              <Button size="sm" variant="ghost" asChild>
                <a href={contentUrl(f.file.id, undefined, true)} aria-label={f.file.name}>
                  <Download aria-hidden />
                </a>
              </Button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex flex-wrap gap-2">
        {draft && request.can.edit && request.values.costPath === 'cost-estimation' && (
          <Button variant="outline" onClick={() => onUpload('cost-estimation')}>
            <Upload aria-hidden />
            {t('actions.uploadCostEstimation')}
          </Button>
        )}
        {draft &&
          request.can.edit &&
          request.values.costPath === 'project-scope' &&
          !request.procurementStatus && (
            <>
              {options.projectScopeTemplateFileId && (
                <Button variant="outline" asChild>
                  <a href={contentUrl(options.projectScopeTemplateFileId, undefined, true)}>
                    <Download aria-hidden />
                    {t('actions.downloadTemplate')}
                  </a>
                </Button>
              )}
              <Button variant="outline" onClick={() => onUpload('project-scope')}>
                <Upload aria-hidden />
                {t('actions.uploadScope')}
              </Button>
              {request.projectScopeUploaded && (
                <Button onClick={onHandOff}>
                  <Send aria-hidden />
                  {t('actions.handOff')}
                </Button>
              )}
            </>
          )}
        {request.can.quotations && (
          <Button variant="outline" onClick={() => onUpload('quotation')}>
            <Upload aria-hidden />
            {t('actions.uploadQuotations')}
          </Button>
        )}
      </div>
    </section>
  );
}

function SubmitDialog({ request, onClose }: { request: ProgramRequest; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const submit = useRequestMutation(requestApi.submit);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('dialogs.submitTitle')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={submit.isPending}
              onClick={() =>
                submit.mutate(
                  { id: request.id, version: request.version, comment: comment.trim() || null },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.submitted') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('actions.submit')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FormField label={t('dialogs.submitComment')}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {submit.isError && <SubmitErrors error={submit.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Submission failures with each rule's message (SPEC §1.4 A), e.g. the Year 1–4 mismatch. */
function SubmitErrors({ error }: { error: unknown }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const problem = (
    error as {
      problem?: { errors?: { field: string; messageKey: string; params?: Record<string, unknown> | null }[] };
    }
  ).problem;
  if (!problem?.errors?.length) return <StatusBanner tone="danger" title={errorMessage(tc, error)} />;
  return (
    <StatusBanner
      tone="danger"
      title={errorMessage(tc, error)}
      description={
        <ul className="list-disc ps-5" data-testid="submit-errors">
          {problem.errors.map((e, i) => (
            <li key={i}>
              {t(`fields.${e.field}` as 'fields.nameAr', { amount: '', defaultValue: e.field })}:{' '}
              {translateMessageKey(tc, e.messageKey, e.params)}
            </li>
          ))}
        </ul>
      }
    />
  );
}

function CancelDialog({ request, onClose }: { request: ProgramRequest; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const cancel = useRequestMutation(requestApi.cancel);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('dialogs.cancelTitle')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={reason.trim() === ''}
              loading={cancel.isPending}
              onClick={() =>
                cancel.mutate(
                  { id: request.id, version: request.version, reason: reason.trim() },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.cancelled') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('actions.cancel')}
            </Button>
          </>
        }
      >
        <FormField label={t('dialogs.cancelReason')} required>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {cancel.isError && <StatusBanner tone="danger" title={errorMessage(tc, cancel.error)} />}
      </DialogContent>
    </Dialog>
  );
}

function OverrideDialog({ request, onClose }: { request: ProgramRequest; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const classify = useRequestMutation(requestApi.classify);
  const [value, setValue] = useState(request.classificationOverride ?? request.classification);
  const [reason, setReason] = useState(request.overrideReason ?? '');
  const clear = value === request.classification && !request.classificationOverride;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('dialogs.overrideTitle')}
        description={t('dialogs.overrideHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!clear && reason.trim() === ''}
              loading={classify.isPending}
              onClick={() =>
                classify.mutate(
                  {
                    id: request.id,
                    version: request.version,
                    value: value === request.classification ? null : value,
                    reason: value === request.classification ? null : reason.trim(),
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.classified') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <p className="text-sm text-muted-foreground">
            {t('classification.computed', {
              value: t(`classification.${request.classification as 'project'}`),
            })}
          </p>
          <FormField label={t('fields.classification')}>
            <Select
              value={value}
              onValueChange={setValue}
              options={[
                { value: 'project', label: t('classification.project') },
                { value: 'initiative', label: t('classification.initiative') },
              ]}
            />
          </FormField>
          {value !== request.classification && (
            <FormField label={t('dialogs.overrideReason')} required>
              <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
          )}
          {classify.isError && <StatusBanner tone="danger" title={errorMessage(tc, classify.error)} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function BudgetDialog({ request, onClose }: { request: ProgramRequest; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useRequestMutation(requestApi.approvedBudget);
  const [amount, setAmount] = useState(
    String(request.approvedBudget ?? request.values.estimatedBudget ?? ''),
  );
  const valid = /^\d{1,16}(\.\d{1,2})?$/u.test(amount.trim());
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('dialogs.budgetTitle')}
        description={t('dialogs.budgetHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!valid}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: request.id, version: request.version, amount: Number(amount) },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.budget') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <FormField label={t('fields.approvedBudget')}>
          <Input dir="ltr" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </FormField>
        {save.isError && <StatusBanner tone="danger" title={errorMessage(tc, save.error)} />}
      </DialogContent>
    </Dialog>
  );
}

function UploadDialog({
  request,
  kind,
  onClose,
}: {
  request: ProgramRequest;
  kind: 'cost-estimation' | 'project-scope' | 'quotation';
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const upload = useRequestMutation(requestApi.upload);
  const confirm = useRequestMutation(requestApi.quotationsUploaded);
  const files = useRequestFiles(request.id);
  const [file, setFile] = useState<File | null>(null);
  const quotations = (files.data ?? []).filter((f) => f.kind === 'quotation').length;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('dialogs.uploadTitle')} — ${t(`files.kinds.${kind}`)}`}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            {kind === 'quotation' && quotations > 0 && (
              <Button
                variant="outline"
                loading={confirm.isPending}
                onClick={() =>
                  confirm.mutate(
                    { id: request.id, version: request.version },
                    {
                      onSuccess: () => {
                        toast({ title: t('toast.quotations') });
                        onClose();
                      },
                      onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                    },
                  )
                }
              >
                {t('actions.quotationsUploaded')}
              </Button>
            )}
            <Button
              disabled={!file}
              loading={upload.isPending}
              onClick={() =>
                file &&
                upload.mutate(
                  { id: request.id, kind, file },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.uploaded') });
                      setFile(null);
                      void files.refetch();
                      if (kind !== 'quotation') onClose();
                    },
                  },
                )
              }
            >
              <Upload aria-hidden />
              {t('actions.upload')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FileDrop
            label={t(`files.kinds.${kind}`)}
            file={file}
            onFile={setFile}
            accept=".pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg"
          />
          {upload.isError && <StatusBanner tone="danger" title={errorMessage(tc, upload.error)} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}
