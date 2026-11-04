import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { ArrowLeft, CheckCircle2, Sparkles } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { AiBadge, AiFieldReview, aiPendingClass } from '@/features/ai/AiDraft';
import { useAiCapabilities } from '@/features/ai/api';
import { pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FileDrop,
  FormField,
  PageHeader,
  Skeleton,
  StageStepper,
  StatusBanner,
  Textarea,
  useToast,
} from '@/ui';
import {
  requestApi,
  useFrameworkRequest,
  useRequestMutation,
  useTypeForm,
  type FormSchema,
  type FormValues,
  type FrameworkRequest,
} from './api';
import { MessageList, RequestStatusChip, TypeIcon } from './components';
import { FormRenderer, FormValuesView } from './FormRenderer';
import {
  allFields,
  fieldErrorsOf,
  initialValues,
  isEmpty,
  isRequired,
  isVisible,
  normalise,
  otherErrors,
  parsePrefill,
  sectionOfError,
  type FieldErrors,
} from './formLogic';

const newRoute = getRouteApi('/app/requests/new/$typeCode');
const editRoute = getRouteApi('/app/requests/$requestId/edit');

/** New request from the catalogue or a deep link (`/requests/new/{type}?values=…`). */
export function RequestWizardPage() {
  const { typeCode } = newRoute.useParams();
  const { values } = newRoute.useSearch();
  const navigate = useNavigate();
  return (
    <RequestWizard
      typeCode={typeCode}
      prefill={parsePrefill(values)}
      onSubmitted={(request) =>
        void navigate({
          to: '/requests/$requestId',
          params: { requestId: request.id },
          search: { tab: 'overview' },
        })
      }
    />
  );
}

/** Correct a draft or a returned request and (re)submit it. */
export function RequestEditPage() {
  const { requestId } = editRoute.useParams();
  const { t: tc } = useTranslation();
  const { t } = useTranslation('requests');
  const navigate = useNavigate();
  const request = useFrameworkRequest(requestId);
  if (request.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (request.isError) return <StatusBanner tone="danger" title={errorMessage(tc, request.error)} />;
  if (!request.data.can.edit)
    return (
      <StatusBanner
        tone="warning"
        title={t('wizard.notEditable')}
        actions={
          <Button variant="outline" asChild>
            <Link to="/requests/$requestId" params={{ requestId }} search={{ tab: 'overview' }}>
              {t('wizard.openRequest')}
            </Link>
          </Button>
        }
      />
    );
  return (
    <RequestWizard
      typeCode={request.data.typeCode}
      existing={request.data}
      onSubmitted={(saved) =>
        void navigate({
          to: '/requests/$requestId',
          params: { requestId: saved.id },
          search: { tab: 'overview' },
        })
      }
    />
  );
}

interface RequestWizardProps {
  typeCode: string;
  prefill?: FormValues;
  existing?: FrameworkRequest;
  /** Chrome-less intranet page: compact header, source `embed`. */
  embedded?: boolean;
  onSubmitted: (request: FrameworkRequest) => void;
}

/**
 * The request wizard (docs/design/requests.md §2.7): one step per form section, a review step, save as draft at any step, server field
 * errors put back on their inputs, AI document-to-form values shown as drafts to accept.
 */
export function RequestWizard({ typeCode, prefill, existing, embedded, onSubmitted }: RequestWizardProps) {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const typeForm = useTypeForm(typeCode);
  const lang = i18n.language;
  const schema: FormSchema | undefined = existing?.form ?? typeForm.data?.form;
  const [request, setRequest] = useState<FrameworkRequest | undefined>(existing);
  const [values, setValues] = useState<FormValues | undefined>(undefined);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<unknown>();
  const [step, setStep] = useState(0);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [aiDrafts, setAiDrafts] = useState<Record<string, boolean>>({});
  const [aiOpen, setAiOpen] = useState(false);
  const submitMutation = useRequestMutation(requestApi.submit);

  const current = useMemo(
    () => values ?? (schema ? initialValues(schema, existing?.data ?? prefill ?? {}) : {}),
    [values, schema, existing, prefill],
  );

  if (!existing && typeForm.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (!existing && typeForm.isError)
    return <StatusBanner tone="danger" title={errorMessage(tc, typeForm.error)} />;
  if (!schema) return null;

  const name = existing
    ? pickText(lang, existing.typeNameAr, existing.typeNameEn)
    : pickText(lang, typeForm.data?.nameAr, typeForm.data?.nameEn);
  const sections = schema.sections ?? [];
  const steps = [
    ...sections.map((section, index) => ({
      key: section.code ?? `s${index}`,
      label: pickText(lang, section.titleAr, section.titleEn) || t('wizard.section', { number: index + 1 }),
    })),
    { key: '__review', label: t('wizard.review') },
  ];
  const reviewing = step === steps.length - 1;
  const pendingAi = Object.values(aiDrafts).some((accepted) => !accepted);
  const returned = request?.status === 'returned';

  const change = (next: FormValues) => {
    // Editing an AI value counts as reviewing it.
    const touched = Object.keys(aiDrafts).filter((code) => next[code] !== current[code]);
    if (touched.length > 0)
      setAiDrafts((drafts) => ({ ...drafts, ...Object.fromEntries(touched.map((code) => [code, true])) }));
    setValues(next);
  };

  const showFailure = (failure: unknown) => {
    const fieldErrors = fieldErrorsOf(failure, 'fields');
    setErrors(fieldErrors);
    const index = sectionOfError(schema, fieldErrors);
    if (index >= 0) setStep(index);
    if (Object.keys(fieldErrors).length === 0 || otherErrors(failure, ['fields']).length > 0)
      setBanner(failure);
    else setBanner(undefined);
    if (!(failure instanceof ApiError) || !failure.problem.errors?.length)
      toast({ tone: 'error', title: errorMessage(tc, failure) });
  };

  const persist = async (): Promise<FrameworkRequest> => {
    const data = normalise(schema, current);
    const saved = request
      ? await requestApi.save({ id: request.id, data, version: request.version })
      : await requestApi.create({ typeCode, data, source: embedded ? 'embed' : 'ui' });
    setRequest(saved);
    setValues(initialValues(saved.form, saved.data));
    setErrors({});
    setBanner(undefined);
    return saved;
  };

  const ensureRequest = async () => {
    if (request) return request.id;
    try {
      return (await persist()).id;
    } catch (failure) {
      showFailure(failure);
      throw failure;
    }
  };

  const saveDraft = async () => {
    setBusy(true);
    try {
      await persist();
      toast({ title: tc('wizard.draftSaved') });
    } catch (failure) {
      showFailure(failure);
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    setBusy(true);
    try {
      const saved = await persist();
      const submitted = await submitMutation.mutateAsync({
        id: saved.id,
        version: saved.version,
        comment: comment.trim() || null,
      });
      toast({ title: t('wizard.submitted', { number: submitted.number }) });
      onSubmitted(submitted);
    } catch (failure) {
      showFailure(failure);
    } finally {
      setBusy(false);
    }
  };

  /** Required visible fields of the step are filled before moving on (the server checks everything again on submit). */
  const checkStep = () => {
    const section = sections[step];
    if (!section) return true;
    const missing: FieldErrors = {};
    for (const field of section.fields ?? [])
      if (isVisible(field, current) && isRequired(field, current) && field.type !== 'table') {
        const value = current[field.code ?? ''];
        if (isEmpty(value) || (field.type === 'checkbox' && value !== true))
          missing[field.code ?? ''] = {
            messageKey: field.type === 'checkbox' ? 'requests.errors.mustConfirm' : 'validation.notEmpty',
          };
      }
    setErrors((existingErrors) => {
      const kept = Object.fromEntries(
        Object.entries(existingErrors).filter(
          ([path]) => !(section.fields ?? []).some((f) => path.split(/[.[]/u)[0] === f.code),
        ),
      );
      return { ...kept, ...missing };
    });
    return Object.keys(missing).length === 0;
  };

  const header = embedded ? (
    <div className="flex items-center gap-3">
      <span className="flex size-10 items-center justify-center rounded-md bg-accent text-primary">
        <TypeIcon name={typeForm.data?.icon} className="size-5" />
      </span>
      <h1 className="text-xl font-semibold" dir="auto">
        {name}
      </h1>
    </div>
  ) : (
    <>
      <Link
        to={request ? '/requests/$requestId' : '/requests'}
        params={request ? { requestId: request.id } : {}}
        search={request ? { tab: 'overview' } : {}}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {request ? t('wizard.openRequest') : t('catalogue.title')}
      </Link>
      <PageHeader
        title={<span dir="auto">{name}</span>}
        description={
          request ? (
            <bdi>{request.number}</bdi>
          ) : (
            pickText(lang, typeForm.data?.descriptionAr, typeForm.data?.descriptionEn)
          )
        }
        meta={request && <RequestStatusChip status={request.status} />}
      />
    </>
  );

  return (
    <div className="flex flex-col gap-6">
      {header}
      {returned && <StatusBanner tone="warning" title={t('wizard.returnedHint')} />}
      <AiFill
        typeCode={typeCode}
        open={aiOpen}
        onOpenChange={setAiOpen}
        onValues={(proposed) => {
          const known = new Set(allFields(schema).map((f) => f.code));
          const accepted = Object.fromEntries(Object.entries(proposed).filter(([code]) => known.has(code)));
          setValues({ ...current, ...accepted });
          setAiDrafts(Object.fromEntries(Object.keys(accepted).map((code) => [code, false])));
        }}
      />
      {pendingAi && (
        <StatusBanner
          tone="info"
          title={
            <span className="inline-flex items-center gap-2">
              <AiBadge />
              {t('wizard.aiReview')}
            </span>
          }
          actions={
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                setAiDrafts((d) => Object.fromEntries(Object.keys(d).map((code) => [code, true])))
              }
              data-testid="ai-accept-all"
            >
              {t('wizard.aiAcceptAll')}
            </Button>
          }
        />
      )}
      <div className="flex flex-col gap-6 rounded-lg border bg-card p-4 sm:p-6">
        <StageStepper stages={steps} current={steps[step]?.key ?? '__review'} />
        <section aria-labelledby="wizard-step-title" className="flex flex-col gap-4">
          <div className="flex flex-col gap-0.5">
            <p className="text-xs text-muted-foreground">
              {tc('wizard.stepOf', { current: step + 1, total: steps.length })}
            </p>
            <h2 id="wizard-step-title" className="text-lg font-semibold">
              {steps[step]?.label}
            </h2>
          </div>
          {reviewing ? (
            <div className="flex flex-col gap-4">
              <p className="text-sm text-muted-foreground">{t('wizard.reviewHint')}</p>
              <FormValuesView schema={schema} values={current} requestId={request?.id} />
              <FormField label={t('wizard.comment')}>
                <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
              </FormField>
            </div>
          ) : (
            <FormRenderer
              schema={schema}
              values={current}
              onChange={change}
              errors={errors}
              sectionCodes={[sections[step]?.code ?? '']}
              showSectionTitles={false}
              requestId={request?.id}
              ensureRequest={ensureRequest}
              decorate={(field) => {
                const code = field.code ?? '';
                if (!(code in aiDrafts)) return undefined;
                const accepted = aiDrafts[code] === true;
                return {
                  className: accepted ? undefined : aiPendingClass,
                  after: (
                    <AiFieldReview
                      suggestion={{
                        confidence: null,
                        source: null,
                        sourceVerified: true,
                        locationKind: null,
                        locationNumber: null,
                        fromScan: false,
                        accepted,
                      }}
                      onAccept={() => setAiDrafts((d) => ({ ...d, [code]: true }))}
                    />
                  ),
                };
              }}
            />
          )}
          {banner !== undefined && (
            <StatusBanner
              tone="danger"
              title={errorMessage(tc, banner)}
              description={<MessageList messages={otherErrors(banner, ['fields'])} />}
            />
          )}
        </section>
        <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-4">
          <div className="flex flex-wrap gap-2">
            <Button
              variant="ghost"
              onClick={() => void saveDraft()}
              disabled={busy || pendingAi}
              data-testid="wizard-save"
            >
              {tc('actions.saveDraft')}
            </Button>
            {!pendingAi && Object.keys(aiDrafts).length === 0 && <AiButton onClick={() => setAiOpen(true)} />}
          </div>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setStep((i) => i - 1)} disabled={step === 0 || busy}>
              {tc('actions.back')}
            </Button>
            {reviewing ? (
              <Button
                onClick={() => void submit()}
                loading={busy}
                disabled={pendingAi}
                data-testid="wizard-submit"
              >
                {returned ? t('wizard.resubmit') : t('wizard.submit')}
              </Button>
            ) : (
              <Button
                onClick={() => {
                  if (checkStep()) setStep((i) => i + 1);
                }}
                disabled={busy}
                data-testid="wizard-next"
              >
                {tc('actions.next')}
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

const fillFeature = 'requests-form-fill';

function useFillAvailable() {
  const capabilities = useAiCapabilities();
  const features = capabilities.data?.features as Record<string, boolean | undefined> | undefined;
  return capabilities.data?.enabled === true && features?.[fillFeature] === true;
}

function AiButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation('requests');
  const available = useFillAvailable();
  if (!available) return null;
  return (
    <Button variant="outline" onClick={onClick} data-testid="wizard-ai-fill">
      <Sparkles aria-hidden />
      {t('wizard.aiFill')}
    </Button>
  );
}

/** Upload a document; the proposed values are shown as an AI draft (never saved by the server, CLAUDE.md rule 6). */
function AiFill({
  typeCode,
  open,
  onOpenChange,
  onValues,
}: {
  typeCode: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onValues: (values: FormValues) => void;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  if (!open) return null;
  const run = async () => {
    if (!file) return;
    setBusy(true);
    setError(undefined);
    try {
      const result = await requestApi.aiFill({ typeCode, file });
      const proposed = result.values as unknown as FormValues;
      if (Object.keys(proposed).length === 0) {
        toast({ tone: 'info', title: t('wizard.aiNone') });
      } else {
        onValues(proposed);
        toast({ title: t('wizard.aiDone', { count: Object.keys(proposed).length }) });
      }
      setFile(null);
      onOpenChange(false);
    } catch (failure) {
      if (failure instanceof ApiError && (failure.status === 403 || failure.status === 404)) {
        toast({ tone: 'error', title: errorMessage(tc, failure) });
        onOpenChange(false);
      } else setError(failure);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent
        title={t('wizard.aiTitle')}
        description={t('wizard.aiHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button disabled={!file} loading={busy} onClick={() => void run()} data-testid="dialog-confirm">
              <Sparkles aria-hidden />
              {t('wizard.aiRun')}
            </Button>
          </>
        }
      >
        <FileDrop
          label={t('wizard.aiFile')}
          accept=".pdf,.docx,.doc,.txt,.png,.jpg,.jpeg"
          file={file}
          onFile={setFile}
        />
        {error !== undefined && <StatusBanner tone="danger" title={errorMessage(tc, error)} />}
      </DialogContent>
    </Dialog>
  );
}

/** After an embedded submission: the number and a way to start again. */
export function SubmittedPanel({ request, onAnother }: { request: FrameworkRequest; onAnother: () => void }) {
  const { t } = useTranslation('requests');
  return (
    <div
      className="flex flex-col items-center gap-4 rounded-lg border bg-card p-8 text-center"
      data-testid="embed-done"
    >
      <CheckCircle2 className="size-10 text-success" aria-hidden />
      <h2 className="text-lg font-semibold">{t('wizard.embedDone', { number: request.number })}</h2>
      <p className="text-sm text-muted-foreground">{t('wizard.embedDoneHint')}</p>
      <div className="flex flex-wrap justify-center gap-2">
        <Button variant="outline" asChild>
          <a href={`/requests/${request.id}`} target="_blank" rel="noreferrer">
            {t('wizard.openRequest')}
          </a>
        </Button>
        <Button onClick={onAnother}>{t('wizard.embedAnother')}</Button>
      </div>
    </div>
  );
}
