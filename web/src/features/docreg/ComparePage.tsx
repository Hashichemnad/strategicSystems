import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { Button, FormField, PageHeader, Select, Skeleton, StatusBanner, StatusChip, useToast } from '@/ui';
import {
  defaultPair,
  docregApi,
  useCompare,
  useDocRegAi,
  useDocument,
  type Compare,
  type SummaryDraft,
} from './api';
import { AiDraftPanel, DocRegPage, documentName, useDocT } from './components';

const route = getRouteApi('/app/documents/$documentId/compare');

/** Compare two versions of a document (docs/design/requests.md §3.6): word-level diff inline, counts and the AI "what changed". */
export function DocumentComparePage() {
  const { t } = useTranslation('docreg');
  const { t: tc, i18n } = useTranslation();
  const { k } = useDocT();
  const { documentId } = route.useParams();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const document = useDocument(documentId);
  const versions = (document.data?.versions ?? []).filter((v) => v.hasText);
  const fallback = defaultPair(versions);
  const from = search.from ?? fallback.from;
  const to = search.to ?? fallback.to;
  const compare = useCompare(documentId, from, to);

  if (document.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (document.isError)
    return (
      <DocRegPage>
        <StatusBanner tone="danger" title={errorMessage(tc, document.error)} />
      </DocRegPage>
    );
  const doc = document.data;
  const options = versions.map((v) => ({
    value: v.id,
    label: `${v.label ?? '—'} · ${k(`versionStatus.${v.status}`, { defaultValue: v.status })}`,
  }));

  return (
    <DocRegPage testId="document-compare">
      <Link
        to="/documents/$documentId"
        params={{ documentId }}
        search={{ tab: 'versions' }}
        className="flex w-fit items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {documentName(i18n.language, doc, t('document.untitled'))}
      </Link>
      <PageHeader
        title={t('compare.title')}
        description={<bdi>{doc.documentNo ?? t('document.noNumber')}</bdi>}
      />
      {versions.length < 2 ? (
        <StatusBanner tone="info" title={t('compare.notEnough')} />
      ) : (
        <>
          <div className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2">
            <FormField label={t('compare.from')}>
              <Select
                value={from}
                onValueChange={(value) => void navigate({ search: (p) => ({ ...p, from: value, to }) })}
                options={options}
              />
            </FormField>
            <FormField label={t('compare.to')}>
              <Select
                value={to}
                onValueChange={(value) => void navigate({ search: (p) => ({ ...p, from, to: value }) })}
                options={options}
              />
            </FormField>
          </div>
          {from === to ? (
            <StatusBanner tone="warning" title={t('compare.pick')} />
          ) : compare.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, compare.error)} />
          ) : !compare.data ? (
            <Skeleton className="h-64 rounded-lg" />
          ) : (
            <>
              <WhatChanged documentId={documentId} from={from ?? ''} to={to ?? ''} />
              <Diff data={compare.data} />
            </>
          )}
        </>
      )}
    </DocRegPage>
  );
}

function Diff({ data }: { data: Compare }) {
  const { t } = useTranslation('docreg');
  if (data.parts.length === 0) return <StatusBanner tone="info" title={t('compare.noText')} />;
  const unchanged = data.added === 0 && data.removed === 0;
  return (
    <section className="flex flex-col gap-3" aria-label={t('compare.title')}>
      <div className="flex flex-wrap items-center gap-2 text-sm" aria-live="polite">
        <StatusChip tone="success" className="tabular-nums">
          {t('compare.added', { count: data.added })}
        </StatusChip>
        <StatusChip tone="danger" className="tabular-nums">
          {t('compare.removed', { count: data.removed })}
        </StatusChip>
        <span className="text-muted-foreground">
          <bdi>{data.fromLabel ?? '—'}</bdi> → <bdi>{data.toLabel ?? '—'}</bdi>
        </span>
      </div>
      {unchanged && <StatusBanner tone="success" title={t('compare.same')} />}
      <div
        dir="auto"
        className="max-h-[70vh] overflow-y-auto rounded-lg border bg-card p-5 text-start text-sm leading-7 whitespace-pre-wrap"
        data-testid="diff"
      >
        {data.parts.map((part, index) => {
          const space = index > 0 ? ' ' : '';
          if (part.kind === 'added')
            return (
              <span key={index}>
                {space}
                <ins
                  className="rounded bg-success-soft px-0.5 text-success no-underline"
                  data-testid="diff-added"
                  title={t('compare.legendAdded')}
                >
                  <span className="sr-only">{t('compare.legendAdded')}: </span>
                  {part.text}
                </ins>
              </span>
            );
          if (part.kind === 'removed')
            return (
              <span key={index}>
                {space}
                <del
                  className="rounded bg-danger-soft px-0.5 text-danger line-through"
                  data-testid="diff-removed"
                  title={t('compare.legendRemoved')}
                >
                  <span className="sr-only">{t('compare.legendRemoved')}: </span>
                  {part.text}
                </del>
              </span>
            );
          return (
            <span key={index}>
              {space}
              {part.text}
            </span>
          );
        })}
      </div>
    </section>
  );
}

function WhatChanged({ documentId, from, to }: { documentId: string; from: string; to: string }) {
  const { t } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  const enabled = useDocRegAi('docreg-what-changed');
  const toast = useToast();
  const [draft, setDraft] = useState<{ key: string; value: SummaryDraft }>();
  const [busy, setBusy] = useState(false);
  if (!enabled) return null;
  const key = `${from}:${to}`;
  return (
    <div className="flex flex-col gap-3">
      <Button
        variant="outline"
        className="w-fit"
        loading={busy}
        data-testid="compare-ai"
        onClick={() => {
          setBusy(true);
          docregApi
            .whatChanged({ id: documentId, from, to })
            .then((value) => setDraft({ key, value }))
            .catch((e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) }))
            .finally(() => setBusy(false));
        }}
      >
        <Sparkles aria-hidden />
        {t('ai.whatChanged')}
      </Button>
      {draft?.key === key && (
        <AiDraftPanel
          title={t('ai.whatChangedTitle')}
          ar={draft.value.summaryAr}
          en={draft.value.summaryEn}
          testId="compare-ai-draft"
        />
      )}
    </div>
  );
}
