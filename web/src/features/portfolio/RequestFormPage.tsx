import { getRouteApi, Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, FilePlus2, FileText, Sparkles, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import type { AiSuggestion } from '@/features/ai/AiDraft';
import { useAiFeature, useExtractForm, type ExtractionResult } from '@/features/ai/api';
import { Button, FileDrop, PageHeader, Skeleton, StatusBanner, useToast } from '@/ui';
import { requestAiFormKey, usePortfolioOptions, useRequest } from './api';
import { priorityPrefix, RequestWizard, screeningFields, type FormState } from './RequestWizard';

const newRoute = getRouteApi('/app/portfolio/requests/new');

/** Core fields the documents should fill; those not found are listed for the requester (SPEC §1.15 A4 "missing fields are flagged"). */
const expected = [
  'nameAr',
  'nameEn',
  'description',
  'justifications',
  'scopeOfWork',
  'strategicObjectiveCode',
  'suggestionSourceCode',
  'programTypeCode',
  'programNature',
  'expectedStartDate',
  'expectedEndDate',
  'requiresBudget',
  'estimatedBudget',
  'mainOptionCode',
  ...screeningFields,
  'practiceOperation',
] as const;

/** Merges the proposals of several documents: for each field the most confident value wins (ties: the first document). */
export function mergeExtractions(results: ExtractionResult[]): {
  form: FormState;
  drafts: Record<string, AiSuggestion>;
} {
  const best = new Map<string, ExtractionResult['fields'][number]>();
  for (const result of results) {
    for (const field of result.fields) {
      if (field.value === null || field.value === '') continue;
      const current = best.get(field.name);
      if (!current || field.confidence > current.confidence) best.set(field.name, field);
    }
  }
  const form: FormState = {};
  const drafts: Record<string, AiSuggestion> = {};
  for (const [name, field] of best) {
    let value = field.value ?? '';
    if (name.startsWith(priorityPrefix)) {
      const score = Math.round(Number(value));
      if (!Number.isFinite(score) || score < 1 || score > 5) continue;
      value = String(score);
    }
    form[name] = value;
    drafts[name] = {
      confidence: field.confidence,
      source: field.source ?? null,
      sourceVerified: field.sourceVerified,
      locationKind: field.locationKind,
      locationNumber: field.locationNumber,
      fromScan: field.fromScan,
      accepted: false,
    };
  }
  return { form, drafts };
}

/** New request (choose manual or "start from a document") and edit of a draft: both end in the request wizard. */
export function RequestFormPage() {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const params = useParams({ strict: false });
  const requestId = (params as { requestId?: string }).requestId;
  const options = usePortfolioOptions();
  const request = useRequest(requestId);
  if (options.isPending || (requestId && request.isPending)) return <Skeleton className="h-96 rounded-lg" />;
  if (options.isError) return <StatusBanner tone="danger" title={errorMessage(tc, options.error)} />;
  if (requestId && request.isError)
    return <StatusBanner tone="danger" title={errorMessage(tc, request.error)} />;
  if (requestId && request.data) {
    return (
      <div className="flex flex-col gap-6">
        <Link
          to="/portfolio/requests/$requestId"
          params={{ requestId }}
          search={{ tab: 'overview' }}
          className="flex items-center gap-1 text-sm text-primary hover:underline"
        >
          <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
          <bdi>{request.data.number}</bdi>
        </Link>
        <PageHeader title={t('editRequest')} description={<bdi>{request.data.number}</bdi>} />
        <RequestWizard request={request.data} options={options.data} />
      </div>
    );
  }
  return <NewRequest />;
}

function NewRequest() {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const { mode } = newRoute.useSearch();
  const navigate = newRoute.useNavigate();
  const options = usePortfolioOptions();
  const extractOn = useAiFeature('extract');
  const extract = useExtractForm(requestAiFormKey);
  const [file, setFile] = useState<File | null>(null);
  const [documents, setDocuments] = useState<File[]>([]);
  const [reading, setReading] = useState<string>();
  const [prefill, setPrefill] = useState<{
    form: FormState;
    drafts: Record<string, AiSuggestion>;
    missing: string[];
  }>();
  if (!options.data) return <Skeleton className="h-96 rounded-lg" />;

  async function read() {
    const results: ExtractionResult[] = [];
    try {
      for (const document of documents) {
        setReading(document.name);
        results.push(await extract.mutateAsync(document));
      }
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
      return;
    } finally {
      setReading(undefined);
    }
    const merged = mergeExtractions(results);
    const missing = expected.filter((name) => !(name in merged.form));
    toast({ title: t('start.filled', { count: Object.keys(merged.drafts).length, files: results.length }) });
    setPrefill({ ...merged, missing });
  }

  const header = (
    <>
      <Link
        to="/portfolio/requests"
        search={{ view: 'all', page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader title={t('newRequest')} />
    </>
  );

  if (mode === 'manual' || prefill) {
    return (
      <div className="flex flex-col gap-6">
        {header}
        <RequestWizard
          options={options.data}
          {...(prefill
            ? { initial: prefill.form, initialDrafts: prefill.drafts, missing: prefill.missing, documents }
            : {})}
        />
      </div>
    );
  }

  if (mode === 'document') {
    return (
      <div className="flex flex-col gap-6">
        {header}
        <section
          className="flex flex-col gap-4 rounded-lg border bg-card p-6"
          data-testid="start-from-document"
        >
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <Sparkles className="size-5 text-info" aria-hidden />
            {t('start.document')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('start.documentHint')}</p>
          {!extractOn && <StatusBanner tone="warning" title={t('start.noAi')} />}
          <FileDrop
            label={t('start.add')}
            accept=".pdf,.docx,.pptx,.png,.jpg,.jpeg,.tif,.tiff"
            file={file}
            onFile={setFile}
            disabled={!extractOn || Boolean(reading)}
          />
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              disabled={!file}
              onClick={() => {
                if (file) setDocuments((current) => [...current, file]);
                setFile(null);
              }}
            >
              <FilePlus2 aria-hidden />
              {t('start.add')}
            </Button>
          </div>
          {documents.length > 0 && (
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-medium">{t('start.documentsTitle')}</h3>
              <ul className="flex flex-col gap-1" data-testid="documents-to-read">
                {documents.map((d, i) => (
                  <li
                    key={`${d.name}-${i}`}
                    className="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
                  >
                    <span className="flex items-center gap-2">
                      <FileText className="size-4 text-muted-foreground" aria-hidden />
                      <bdi>{d.name}</bdi>
                    </span>
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-label={t('start.remove')}
                      onClick={() => setDocuments((current) => current.filter((_, j) => j !== i))}
                    >
                      <Trash2 aria-hidden />
                    </Button>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {reading && <StatusBanner tone="info" title={t('start.reading', { file: reading })} />}
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={() => void navigate({ search: { mode: 'manual' } })}>
              {t('start.manual')}
            </Button>
            <Button
              disabled={!extractOn || documents.length === 0}
              loading={Boolean(reading)}
              onClick={() => void read()}
            >
              <Sparkles aria-hidden />
              {t('start.read')}
            </Button>
          </div>
        </section>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {header}
      <h2 className="text-lg font-semibold">{t('start.title')}</h2>
      <div className="grid gap-4 md:grid-cols-2">
        <button
          type="button"
          className="flex flex-col gap-2 rounded-lg border bg-card p-6 text-start hover:border-primary focus-visible:ring-2 focus-visible:ring-ring"
          onClick={() => void navigate({ search: { mode: 'manual' } })}
        >
          <FileText className="size-6 text-primary" aria-hidden />
          <span className="font-semibold">{t('start.manual')}</span>
          <span className="text-sm text-muted-foreground">{t('start.manualHint')}</span>
        </button>
        {extractOn && (
          <button
            type="button"
            className="flex flex-col gap-2 rounded-lg border bg-card p-6 text-start hover:border-primary focus-visible:ring-2 focus-visible:ring-ring"
            onClick={() => void navigate({ search: { mode: 'document' } })}
          >
            <Sparkles className="size-6 text-info" aria-hidden />
            <span className="font-semibold">{t('start.document')}</span>
            <span className="text-sm text-muted-foreground">{t('start.documentHint')}</span>
          </button>
        )}
      </div>
    </div>
  );
}
