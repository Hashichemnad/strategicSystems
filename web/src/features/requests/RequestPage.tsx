import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, ChevronDown, FileDown, Pencil, Send, Sparkles, XCircle } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { AiBadge, aiPendingClass } from '@/features/ai/AiDraft';
import { useAiCapabilities } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
  EmptyState,
  FormField,
  PageHeader,
  RecordLayout,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  downloadRequestPdf,
  requestApi,
  requestResource,
  useFrameworkRequest,
  useRequestMutation,
  type ClassificationSuggestion,
  type DuplicateCheck,
  type FormValues,
  type FrameworkRequest,
  type StageActionView,
} from './api';
import { MessageList, RequestStatusChip, useDynamicT } from './components';
import { FormRenderer, FormValuesView } from './FormRenderer';
import { fieldErrorsOf, isEmptySchema, normalise, otherErrors, type FieldErrors } from './formLogic';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/requests/$requestId');

export const requestTabs = ['overview', 'approvals', 'timeline', 'documents', 'tasks', 'history'] as const;

type DialogState =
  | { kind: 'submit' }
  | { kind: 'cancel' }
  | { kind: 'decide'; action: string }
  | { kind: 'act'; action: StageActionView }
  | { kind: 'choose' };

/** Where the request stands: the current (or stopped) stage, `completed` when done. */
export function currentStage(request: Pick<FrameworkRequest, 'stages'>): string {
  const active = request.stages.find((s) => s.state === 'current' || s.state === 'stopped');
  if (active) return active.code;
  const done = request.stages.filter((s) => s.state === 'done');
  return done.at(-1)?.code ?? request.stages[0]?.code ?? 'draft';
}

/** Request record page (docs/design/requests.md §2.7): header, stage stepper, current status, actions, tabs. */
export function RequestPage() {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const { requestId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const toast = useToast();
  const request = useFrameworkRequest(requestId);
  const [dialog, setDialog] = useState<DialogState>();
  if (request.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (request.isError) return <StatusBanner tone="danger" title={errorMessage(tc, request.error)} />;
  const data = request.data;
  const lang = i18n.language;
  const title = pickText(lang, data.titleAr, data.titleEn) || data.number;
  const typeLabel = pickText(lang, data.typeNameAr, data.typeNameEn) || data.typeCode;
  const stopped = data.stages.find((s) => s.state === 'stopped');
  const returned = data.status === 'returned';

  const pdf = (template: string, language: string) =>
    void downloadRequestPdf(data.id, template, language, `${data.number}-${template}-${language}.pdf`).catch(
      (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
    );

  const when = (value: string | null) => (value ? formatDateTime(value, lang) : '—');
  return (
    <RecordLayout
      back={
        <Link to="/requests/mine" className="flex items-center gap-1 text-sm text-primary hover:underline">
          <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
          {t('nav.mine')}
        </Link>
      }
      header={
        <PageHeader
          title={<span dir="auto">{title}</span>}
          description={
            <>
              <bdi>{data.number}</bdi> · {typeLabel}
            </>
          }
          meta={
            <>
              <OriginBadge type="requests.request" id={data.id} />
              <RequestStatusChip status={data.status} />
              {data.overdue && <StatusChip tone="danger">{t('list.overdue')}</StatusChip>}
            </>
          }
          actions={
            <>
              {data.can.edit && (
                <Button variant="outline" asChild data-testid="request-action-edit">
                  <Link to="/requests/$requestId/edit" params={{ requestId: data.id }}>
                    <Pencil aria-hidden />
                    {tc('actions.edit')}
                  </Link>
                </Button>
              )}
              {data.can.submit && (
                <Button onClick={() => setDialog({ kind: 'submit' })} data-testid="request-action-submit">
                  <Send aria-hidden />
                  {returned ? t('request.resubmit') : t('request.submit')}
                </Button>
              )}
              {data.can.decide &&
                data.can.decideActions.map((code) => (
                  <Button
                    key={code}
                    variant={code === 'approve' ? 'primary' : code === 'reject' ? 'danger' : 'outline'}
                    onClick={() => setDialog({ kind: 'decide', action: code })}
                    data-testid={`request-decide-${code}`}
                  >
                    <DecisionLabel code={code} />
                  </Button>
                ))}
              {data.can.actions.map((action) => (
                <Button
                  key={action.code}
                  variant={
                    action.tone === 'danger' ? 'danger' : action.tone === 'secondary' ? 'outline' : 'primary'
                  }
                  onClick={() => setDialog({ kind: 'act', action })}
                  data-testid={`request-action-${action.code}`}
                >
                  {pickText(lang, action.labelAr, action.labelEn) || action.code}
                </Button>
              ))}
              {data.templates.length > 0 && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline" data-testid="request-pdf">
                      <FileDown aria-hidden />
                      {t('request.pdf')}
                      <ChevronDown aria-hidden />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    {data.templates.map((template) => (
                      <div key={template.code}>
                        <DropdownMenuLabel>
                          {pickText(lang, template.nameAr, template.nameEn) || template.code}
                        </DropdownMenuLabel>
                        <DropdownMenuItem onSelect={() => pdf(template.code, 'ar')}>
                          {t('request.pdfAr')}
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => pdf(template.code, 'en')}>
                          {t('request.pdfEn')}
                        </DropdownMenuItem>
                      </div>
                    ))}
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
              {data.can.cancel && (
                <Button
                  variant="outline"
                  onClick={() => setDialog({ kind: 'cancel' })}
                  data-testid="request-action-cancel"
                >
                  <XCircle aria-hidden />
                  {t('request.cancel')}
                </Button>
              )}
            </>
          }
        />
      }
      highlights={[
        { key: 'number', label: t('list.number'), value: <bdi>{data.number}</bdi> },
        { key: 'type', label: t('request.type'), value: typeLabel },
        {
          key: 'requester',
          label: t('request.requester'),
          value: pickText(lang, data.requester.nameAr, data.requester.nameEn) || '—',
        },
        {
          key: 'unit',
          label: t('request.unit'),
          value: data.unit ? pickText(lang, data.unit.nameAr, data.unit.nameEn) : '—',
        },
        { key: 'submitted', label: t('request.submittedAt'), value: when(data.submittedAt) },
        { key: 'due', label: t('request.dueAt'), value: when(data.stageDueAt) },
      ]}
      path={
        <StageStepper
          stages={data.stages.map((s) => ({
            key: s.code,
            label:
              s.code === 'completed' && (data.status === 'rejected' || data.status === 'cancelled')
                ? t(`status.${data.status}`)
                : pickText(lang, s.nameAr, s.nameEn) || s.code,
          }))}
          current={
            stopped
              ? stopped.code
              : data.status === 'rejected' || data.status === 'cancelled'
                ? 'completed'
                : currentStage(data)
          }
        />
      }
      status={
        <CurrentStatusBanner
          resourceType={requestResource}
          resourceId={data.id}
          actionLabel={(code) =>
            code === 'submit'
              ? returned
                ? t('request.resubmit')
                : t('request.submit')
              : code === 'decide'
                ? t('request.decide')
                : t('request.act')
          }
          onAction={(code) => {
            if (code === 'submit') setDialog({ kind: 'submit' });
            if (code === 'decide')
              setDialog({ kind: 'decide', action: data.can.decideActions[0] ?? 'approve' });
            if (code === 'act') {
              const only = data.can.actions.length === 1 ? data.can.actions[0] : undefined;
              setDialog(only ? { kind: 'act', action: only } : { kind: 'choose' });
            }
          }}
        />
      }
      summary={<Subject request={data} />}
      activeSection={tab}
      onSectionChange={(key) => void navigate({ search: { tab: key as 'overview' } })}
      sections={[
        {
          key: 'overview',
          label: t('request.tabs.overview'),
          content: (
            <>
              <Overview request={data} />
              <ExternalReferencesSection type="requests.request" id={data.id} />
            </>
          ),
        },
        {
          key: 'approvals',
          label: t('request.tabs.approvals'),
          content: <ApprovalPanel resourceType={requestResource} resourceId={data.id} />,
        },
        {
          key: 'timeline',
          label: t('request.tabs.timeline'),
          content: <Timeline request={data} />,
        },
        {
          key: 'documents',
          label: t('request.tabs.documents'),
          content: <DocumentsPanel ownerType={requestResource} ownerId={data.id} canEdit={data.can.upload} />,
        },
        {
          key: 'tasks',
          label: t('request.tabs.tasks'),
          content: <LinkedTasksPanel type={requestResource} id={data.id} />,
        },
        {
          key: 'history',
          label: t('request.tabs.history'),
          content: <HistoryTab record="serviceRequest" id={data.id} />,
        },
      ]}
    >
      {dialog?.kind === 'submit' && <SubmitDialog request={data} onClose={() => setDialog(undefined)} />}
      {dialog?.kind === 'cancel' && <CancelDialog request={data} onClose={() => setDialog(undefined)} />}
      {dialog?.kind === 'decide' && (
        <DecideDialog request={data} initial={dialog.action} onClose={() => setDialog(undefined)} />
      )}
      {dialog?.kind === 'act' && (
        <ActionDialog request={data} action={dialog.action} onClose={() => setDialog(undefined)} />
      )}
      {dialog?.kind === 'choose' && (
        <Dialog open onOpenChange={(open) => !open && setDialog(undefined)}>
          <DialogContent title={t('request.act')}>
            <div className="flex flex-col gap-2">
              {data.can.actions.map((action) => (
                <Button
                  key={action.code}
                  variant={action.tone === 'danger' ? 'danger' : 'outline'}
                  onClick={() => setDialog({ kind: 'act', action })}
                >
                  {pickText(lang, action.labelAr, action.labelEn) || action.code}
                </Button>
              ))}
            </div>
          </DialogContent>
        </Dialog>
      )}
    </RecordLayout>
  );
}

function DecisionLabel({ code }: { code: string }) {
  const { t } = useTranslation('workflow');
  return <>{t(`actions.${code as 'approve'}`, { defaultValue: code })}</>;
}

/** The register record the request created (IP record) or its outcome (document version). */
function Subject({ request }: { request: FrameworkRequest }) {
  const { t } = useTranslation('requests');
  const dt = useDynamicT();
  if (!request.subjectType && !request.outcome) return null;
  return (
    <section
      className="flex flex-wrap items-center gap-3 rounded-lg border bg-card p-4 text-sm"
      data-testid="request-subject"
    >
      {request.outcome && (
        <span>
          <span className="text-muted-foreground">{t('request.outcome')}: </span>
          <StatusChip tone="success">{dt(`request.outcomes.${request.outcome}`, request.outcome)}</StatusChip>
        </span>
      )}
      {request.subjectType === 'ip.record' && request.subjectId && (
        <Link
          to="/ip/records/$recordId"
          params={{ recordId: request.subjectId }}
          search={{ tab: 'overview' }}
          className="font-medium text-primary hover:underline"
        >
          {t('request.openIpRecord')}
        </Link>
      )}
    </section>
  );
}

function Overview({ request }: { request: FrameworkRequest }) {
  const { t, i18n } = useTranslation('requests');
  const dt = useDynamicT();
  const lang = i18n.language;
  const date = (value: string | null) => (value ? formatDateTime(value, lang) : '—');
  const stageName = (code: string) => {
    const stage = request.stages.find((s) => s.code === code);
    return stage ? pickText(lang, stage.nameAr, stage.nameEn) || code : code;
  };
  const rows: [string, string][] = [
    [t('request.type'), pickText(lang, request.typeNameAr, request.typeNameEn) || request.typeCode],
    [t('request.requester'), pickText(lang, request.requester.nameAr, request.requester.nameEn) || '—'],
    [t('request.unit'), request.unit ? pickText(lang, request.unit.nameAr, request.unit.nameEn) : '—'],
    [t('request.createdAt'), date(request.createdAt)],
    [t('request.submittedAt'), date(request.submittedAt)],
    [t('request.dueAt'), date(request.stageDueAt)],
    [t('request.completedAt'), date(request.completedAt)],
    [t('request.source'), dt(`request.sources.${request.source}`, request.source)],
  ];
  const process = Object.entries(request.processData).filter(([, values]) => Object.keys(values).length > 0);
  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <div className="flex flex-col gap-4">
        <section className="rounded-lg border bg-card p-4 sm:p-6" aria-label={t('request.formValues')}>
          <FormValuesView schema={request.form} values={request.data} requestId={request.id} />
        </section>
        {process.map(([stage, values]) => {
          const schema = request.stageForms[stage];
          return (
            <section
              key={stage}
              className="flex flex-col gap-3 rounded-lg border bg-card p-4 sm:p-6"
              data-testid={`process-${stage}`}
            >
              <h3 className="font-semibold">{t('request.processData', { stage: stageName(stage) })}</h3>
              {schema ? (
                <FormValuesView schema={schema} values={values} requestId={request.id} />
              ) : (
                <pre className="text-xs whitespace-pre-wrap" dir="ltr">
                  {JSON.stringify(values, null, 2)}
                </pre>
              )}
            </section>
          );
        })}
        {request.cancelReason && (
          <StatusBanner tone="info" title={t('request.cancelReason')} description={request.cancelReason} />
        )}
      </div>
      <section className="h-fit rounded-lg border bg-card p-4">
        <dl className="flex flex-col gap-3 text-sm">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-3">
              <dt className="text-muted-foreground">{label}</dt>
              <dd className="text-end" dir="auto">
                {value}
              </dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  );
}

function Timeline({ request }: { request: FrameworkRequest }) {
  const { t, i18n } = useTranslation('requests');
  const dt = useDynamicT();
  const lang = i18n.language;
  const stageName = (code: string | null) => {
    if (!code) return null;
    const stage = request.stages.find((s) => s.code === code);
    return stage ? pickText(lang, stage.nameAr, stage.nameEn) || code : code;
  };
  if (request.events.length === 0) return <EmptyState title={t('timeline.empty')} />;
  return (
    <ol className="flex flex-col gap-3" data-testid="request-timeline">
      {[...request.events].reverse().map((event) => {
        const schema = event.fromStage ? request.stageForms[event.fromStage] : undefined;
        const from = stageName(event.fromStage);
        const to = stageName(event.toStage);
        return (
          <li key={event.id} className="flex flex-col gap-2 rounded-lg border bg-card p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium">{dt(`timeline.actions.${event.action}`, event.action)}</span>
              {event.toStatus && event.toStatus !== event.fromStatus && (
                <RequestStatusChip status={event.toStatus} />
              )}
              <span className="text-muted-foreground">· {event.actorName ?? t('history.system')}</span>
              <time className="ms-auto text-muted-foreground" dateTime={event.at}>
                {formatDateTime(event.at, lang)}
              </time>
            </div>
            {from && to && from !== to && (
              <p className="text-muted-foreground">{t('timeline.moved', { from, to })}</p>
            )}
            {event.comment && (
              <blockquote className="border-s-2 ps-3 whitespace-pre-line" dir="auto">
                {event.comment}
              </blockquote>
            )}
            {event.data && Object.keys(event.data).length > 0 && schema && (
              <FormValuesView schema={schema} values={event.data} requestId={request.id} />
            )}
          </li>
        );
      })}
    </ol>
  );
}

function SubmitDialog({ request, onClose }: { request: FrameworkRequest; onClose: () => void }) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const submit = useRequestMutation(requestApi.submit);
  const [comment, setComment] = useState('');
  const fieldErrors = submit.isError ? fieldErrorsOf(submit.error, 'fields') : {};
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={request.status === 'returned' ? t('request.resubmit') : t('request.submit')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={submit.isPending}
              data-testid="dialog-confirm"
              onClick={() =>
                submit.mutate(
                  { id: request.id, version: request.version, comment: comment.trim() || null },
                  {
                    onSuccess: (saved) => {
                      toast({ title: t('wizard.submitted', { number: saved.number }) });
                      onClose();
                    },
                  },
                )
              }
            >
              {request.status === 'returned' ? t('request.resubmit') : t('request.submit')}
            </Button>
          </>
        }
      >
        <FormField label={t('wizard.comment')}>
          <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
        {submit.isError && (
          <StatusBanner
            tone="danger"
            title={errorMessage(tc, submit.error)}
            description={
              Object.keys(fieldErrors).length > 0 ? (
                <>
                  <MessageList messages={Object.values(fieldErrors)} />
                  <Link
                    to="/requests/$requestId/edit"
                    params={{ requestId: request.id }}
                    className="text-primary hover:underline"
                  >
                    {t('request.fixInForm')}
                  </Link>
                </>
              ) : undefined
            }
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function CancelDialog({ request, onClose }: { request: FrameworkRequest; onClose: () => void }) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const cancel = useRequestMutation(requestApi.cancel);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('request.cancelTitle')}
        description={t('request.cancelHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={reason.trim() === ''}
              loading={cancel.isPending}
              data-testid="dialog-confirm"
              onClick={() =>
                cancel.mutate(
                  { id: request.id, version: request.version, reason: reason.trim() },
                  {
                    onSuccess: () => {
                      toast({ title: t('request.cancelled') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('request.cancel')}
            </Button>
          </>
        }
      >
        <FormField label={t('request.cancelReason')} required>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {cancel.isError && <StatusBanner tone="danger" title={errorMessage(tc, cancel.error)} />}
      </DialogContent>
    </Dialog>
  );
}

function useAiFeature(key: string) {
  const capabilities = useAiCapabilities();
  return capabilities.data?.enabled === true && capabilities.data.features[key] === true;
}

/**
 * Approve / reject / return of the current approval step. Approving a stage with an approve form renders that form; for document requests
 * the AI may propose the classification and look for overlapping documents (drafts the approver reviews).
 */
function DecideDialog({
  request,
  initial,
  onClose,
}: {
  request: FrameworkRequest;
  initial: string;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('requests');
  const { t: tw } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const decide = useRequestMutation(requestApi.decide);
  const [action, setAction] = useState(initial);
  const [comment, setComment] = useState('');
  const [values, setValues] = useState<FormValues>({});
  const [aiFields, setAiFields] = useState<Record<string, boolean>>({});
  const [suggestion, setSuggestion] = useState<ClassificationSuggestion>();
  const [duplicates, setDuplicates] = useState<DuplicateCheck>();
  const [aiBusy, setAiBusy] = useState<'classify' | 'duplicates'>();
  const isDocument = request.typeCode === 'docreg.document';
  const canClassify = useAiFeature('docreg-classify');
  const canDuplicates = useAiFeature('docreg-duplicates');
  const form =
    action === 'approve' && request.can.approveForm && !isEmptySchema(request.can.approveForm)
      ? request.can.approveForm
      : null;
  const commentRequired = action === 'reject' || action === 'return';
  const errors: FieldErrors = decide.isError ? fieldErrorsOf(decide.error, 'form') : {};
  const lang = i18n.language;

  const classify = async () => {
    setAiBusy('classify');
    try {
      const result = await requestApi.classify({ id: request.id });
      setSuggestion(result);
      const proposed: FormValues = {};
      if (result.level) proposed.level = result.level;
      if (result.docType) proposed.docType = result.docType;
      if (result.sensitivity) proposed.sensitivity = result.sensitivity;
      setValues((v) => ({ ...v, ...proposed }));
      setAiFields(Object.fromEntries(Object.keys(proposed).map((code) => [code, false])));
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setAiBusy(undefined);
    }
  };
  const checkDuplicates = async () => {
    setAiBusy('duplicates');
    try {
      setDuplicates(await requestApi.duplicates({ id: request.id }));
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setAiBusy(undefined);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className="max-w-2xl"
        title={t('request.decideTitle')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={action === 'reject' ? 'danger' : 'primary'}
              disabled={commentRequired && comment.trim() === ''}
              loading={decide.isPending}
              data-testid="dialog-confirm"
              onClick={() =>
                decide.mutate(
                  {
                    id: request.id,
                    action,
                    comment: comment.trim() || null,
                    form: form ? normalise(form, values) : null,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('request.decided') });
                      onClose();
                    },
                  },
                )
              }
            >
              <DecisionLabel code={action} />
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {request.can.decideActions.length > 1 && (
            <FormField label={t('request.decision')}>
              <Select
                value={action}
                onValueChange={setAction}
                options={request.can.decideActions.map((code) => ({
                  value: code,
                  label: tw(`actions.${code as 'approve'}`, { defaultValue: code }),
                }))}
              />
            </FormField>
          )}
          {form && isDocument && (canClassify || canDuplicates) && (
            <div className="flex flex-wrap gap-2">
              {canClassify && (
                <Button
                  size="sm"
                  variant="outline"
                  loading={aiBusy === 'classify'}
                  onClick={() => void classify()}
                  data-testid="ai-classify"
                >
                  <Sparkles aria-hidden />
                  {t('decide.aiClassify')}
                </Button>
              )}
              {canDuplicates && (
                <Button
                  size="sm"
                  variant="outline"
                  loading={aiBusy === 'duplicates'}
                  onClick={() => void checkDuplicates()}
                  data-testid="ai-duplicates"
                >
                  <Sparkles aria-hidden />
                  {t('decide.aiDuplicates')}
                </Button>
              )}
            </div>
          )}
          {suggestion && suggestion.reasons.length > 0 && (
            <div className="flex flex-col gap-1 rounded-md bg-info-soft/60 p-3 text-sm">
              <AiBadge label={t('decide.aiSuggested')} />
              <ul className="list-disc ps-5">
                {suggestion.reasons.map((reason, i) => (
                  <li key={i} dir="auto">
                    {reason}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {duplicates && (
            <div
              className="flex flex-col gap-2 rounded-md border p-3 text-sm"
              data-testid="ai-duplicate-findings"
            >
              <AiBadge />
              {duplicates.findings.length === 0 ? (
                <p>{t('decide.noDuplicates', { count: duplicates.compared })}</p>
              ) : (
                <ul className="flex flex-col gap-2">
                  {duplicates.findings.map((finding) => (
                    <li key={finding.documentId}>
                      <Link
                        to="/documents/$documentId"
                        params={{ documentId: finding.documentId }}
                        search={{ tab: 'overview' }}
                        className="font-medium text-primary hover:underline"
                      >
                        {finding.documentNo && <bdi className="me-1">{finding.documentNo}</bdi>}
                        {pickText(lang, finding.nameAr, finding.nameEn)}
                      </Link>{' '}
                      <StatusChip tone="warning">{finding.kind}</StatusChip>
                      <p className="text-muted-foreground" dir="auto">
                        {finding.explanation}
                      </p>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
          {form && (
            <FormRenderer
              schema={form}
              values={values}
              onChange={(next) => {
                setAiFields((f) =>
                  Object.fromEntries(
                    Object.entries(f).map(([code, ok]) => [code, ok || next[code] !== values[code]]),
                  ),
                );
                setValues(next);
              }}
              errors={errors}
              requestId={request.id}
              decorate={(field) => {
                const code = field.code ?? '';
                if (!(code in aiFields) || aiFields[code]) return undefined;
                return {
                  className: aiPendingClass,
                  after: (
                    <span className="flex items-center gap-2">
                      <AiBadge label={t('decide.aiSuggested')} />
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        className="h-7"
                        onClick={() => setAiFields((f) => ({ ...f, [code]: true }))}
                      >
                        {t('decide.accept')}
                      </Button>
                    </span>
                  ),
                };
              }}
            />
          )}
          <FormField label={t('request.comment')} required={commentRequired}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {decide.isError && (
            <StatusBanner
              tone="danger"
              title={errorMessage(tc, decide.error)}
              description={<MessageList messages={otherErrors(decide.error, ['form'])} />}
            />
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** A processing action with its mini-form and comment. */
function ActionDialog({
  request,
  action,
  onClose,
}: {
  request: FrameworkRequest;
  action: StageActionView;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const act = useRequestMutation(requestApi.act);
  const [comment, setComment] = useState('');
  const [values, setValues] = useState<FormValues>({});
  const label = pickText(i18n.language, action.labelAr, action.labelEn) || action.code;
  const form = action.form && !isEmptySchema(action.form) ? action.form : null;
  const errors: FieldErrors = act.isError ? fieldErrorsOf(act.error, 'form') : {};
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        className={form ? 'max-w-2xl' : undefined}
        title={label}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={action.tone === 'danger' ? 'danger' : 'primary'}
              disabled={action.requiresComment && comment.trim() === ''}
              loading={act.isPending}
              data-testid="dialog-confirm"
              onClick={() =>
                act.mutate(
                  {
                    id: request.id,
                    action: action.code,
                    version: request.version,
                    comment: comment.trim() || null,
                    form: form ? normalise(form, values) : null,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('request.actionDone', { action: label }) });
                      onClose();
                    },
                  },
                )
              }
            >
              {label}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {form && (
            <FormRenderer
              schema={form}
              values={values}
              onChange={setValues}
              errors={errors}
              requestId={request.id}
            />
          )}
          <FormField label={t('request.comment')} required={action.requiresComment}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {act.isError && (
            <StatusBanner
              tone="danger"
              title={errorMessage(tc, act.error)}
              description={<MessageList messages={otherErrors(act.error, ['form'])} />}
            />
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
