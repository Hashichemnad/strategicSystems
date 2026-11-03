import { Link } from '@tanstack/react-router';
import { Check, FileUp, Languages, ScanText, Sparkles } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FileDrop,
  StatusBanner,
  StatusChip,
  useToast,
  type Tone,
} from '@/ui';
import { useAiFeature, useExtractForm, useSimilarRecords, useTranslate, type ExtractionResult } from './api';

/** "AI draft" marker on anything an AI produced until a person accepts or edits it (CLAUDE.md §12). */
export function AiBadge({ label, className }: { label?: string; className?: string }) {
  const { t } = useTranslation('ai');
  return (
    <StatusChip tone="info" className={cn('gap-1', className)}>
      <Sparkles className="size-3" aria-hidden />
      {label ?? t('badge')}
    </StatusChip>
  );
}

/** A value proposed by AI for one form field: where it came from, how sure, and whether a person accepted it. */
export interface AiSuggestion {
  confidence: number | null;
  source: string | null;
  sourceVerified: boolean;
  /** Where the source was found: `page` / `slide` / `image` and its number. */
  locationKind: string | null;
  locationNumber: number | null;
  /** The source text was read from a scan by OCR (ADR 0033). */
  fromScan: boolean;
  accepted: boolean;
}

/** Highlight for an input that still holds an unreviewed AI value. */
export const aiPendingClass = 'border-info ring-2 ring-info/40 bg-info-soft/40';

function confidenceTone(confidence: number): Tone {
  if (confidence >= 0.75) return 'success';
  if (confidence >= 0.4) return 'warning';
  return 'danger';
}

/**
 * Shown under a field holding an AI value: badge, confidence, the source snippet from the document and an Accept button.
 * Editing the field also counts as review.
 */
export function AiFieldReview({ suggestion, onAccept }: { suggestion: AiSuggestion; onAccept: () => void }) {
  const { t } = useTranslation('ai');
  if (suggestion.accepted)
    return (
      <p className="flex items-center gap-1 text-xs text-success">
        <Check className="size-3" aria-hidden />
        {t('accepted')}
      </p>
    );
  return (
    <div className="flex flex-col gap-1 rounded-md bg-info-soft/60 p-2 text-xs" data-testid="ai-field-review">
      <div className="flex flex-wrap items-center gap-2">
        <AiBadge />
        {suggestion.fromScan && (
          <StatusChip tone="warning" className="gap-1">
            <ScanText className="size-3" aria-hidden />
            <span title={t('fromScanHint')}>{t('fromScan')}</span>
          </StatusChip>
        )}
        {suggestion.confidence !== null && (
          <StatusChip tone={confidenceTone(suggestion.confidence)}>
            {t('confidence', { value: Math.round(suggestion.confidence * 100) })}
          </StatusChip>
        )}
        <Button type="button" size="sm" variant="outline" className="ms-auto h-7" onClick={onAccept}>
          <Check aria-hidden />
          {t('accept')}
        </Button>
      </div>
      {suggestion.source && (
        <details>
          <summary className="cursor-pointer text-muted-foreground">
            {suggestion.locationKind
              ? t('sourceAt', {
                  location: (t as unknown as (key: string, params: object) => string)(
                    `location.${suggestion.locationKind}`,
                    {
                      number: suggestion.locationNumber ?? '',
                    },
                  ),
                })
              : t('source')}
          </summary>
          <blockquote className="mt-1 border-s-2 border-info ps-2 whitespace-pre-wrap" dir="auto">
            {suggestion.source}
          </blockquote>
        </details>
      )}
      {!suggestion.sourceVerified && suggestion.confidence !== null && (
        <p className="text-warning">{t('sourceNotVerified')}</p>
      )}
    </div>
  );
}

/** Upload a document and get proposed values for a form; nothing is saved here (docs/design/ai.md §4.1). */
export function FillFromDocumentDialog({
  formKey,
  open,
  onOpenChange,
  onResult,
}: {
  formKey: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onResult: (result: ExtractionResult) => void;
}) {
  const { t } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const [file, setFile] = useState<File | null>(null);
  const extract = useExtractForm(formKey);
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setFile(null);
          extract.reset();
        }
        onOpenChange(next);
      }}
    >
      <DialogContent
        title={t('fill.title')}
        description={t('fill.description')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!file}
              loading={extract.isPending}
              onClick={() =>
                file &&
                extract.mutate(file, {
                  onSuccess: (result) => {
                    onResult(result);
                    setFile(null);
                    onOpenChange(false);
                  },
                })
              }
            >
              <Sparkles aria-hidden />
              {t('fill.run')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <FileDrop
            label={t('fill.dropLabel')}
            hint={t('fill.dropHint')}
            accept=".pdf,.docx,.pptx,.txt,.png,.jpg,.jpeg,.tif,.tiff"
            file={file}
            onFile={setFile}
            disabled={extract.isPending}
          />
          {extract.isError && <StatusBanner tone="danger" title={errorMessage(tc, extract.error)} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

export function FillFromDocumentButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation('ai');
  if (!useAiFeature('extract')) return null;
  return (
    <Button type="button" variant="outline" onClick={onClick} data-testid="ai-fill-from-document">
      <FileUp aria-hidden />
      {t('fill.button')}
    </Button>
  );
}

/** Translates the other language's text into this field as an AI draft. Hidden when translation is not available. */
export function TranslateButton({
  from,
  sourceText,
  systemCode,
  onTranslated,
}: {
  from: 'ar' | 'en';
  sourceText: string;
  systemCode?: string;
  onTranslated: (text: string) => void;
}) {
  const { t } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const translate = useTranslate();
  if (!useAiFeature('translate')) return null;
  return (
    <Button
      type="button"
      size="sm"
      variant="ghost"
      className="h-7 px-2 text-xs"
      loading={translate.isPending}
      onClick={() => {
        if (!sourceText.trim()) {
          toast({ tone: 'error', title: t('translate.empty') });
          return;
        }
        translate.mutate(
          { text: sourceText, from, to: from === 'ar' ? 'en' : 'ar', ...(systemCode ? { systemCode } : {}) },
          {
            onSuccess: (result) => onTranslated(result.translation),
            onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
          },
        );
      }}
    >
      <Languages aria-hidden />
      {from === 'ar' ? t('translate.fromAr') : t('translate.fromEn')}
    </Button>
  );
}

/** Possible duplicates while typing a title (trigram similarity, no model call). */
export function SimilarRecords({
  resourceType,
  text,
  excludeId,
}: {
  resourceType: string;
  text: string;
  excludeId?: string | null;
}) {
  const { t, i18n } = useTranslation('ai');
  const enabled = useAiFeature('similar');
  const [debounced, setDebounced] = useState(text);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(text), 400);
    return () => clearTimeout(handle);
  }, [text]);
  const similar = useSimilarRecords(resourceType, debounced, excludeId ?? null, enabled);
  if (!enabled || !similar.data?.length) return null;
  return (
    <div
      className="rounded-md border border-warning/40 bg-warning-soft/40 p-3 text-sm"
      data-testid="ai-similar"
    >
      <p className="flex items-center gap-2 font-medium">
        <Sparkles className="size-4 text-warning" aria-hidden />
        {t('similar.title')}
      </p>
      <p className="text-xs text-muted-foreground">{t('similar.description')}</p>
      <ul className="mt-2 flex flex-col gap-1">
        {similar.data.map((hit) => (
          <li key={hit.id} className="flex items-center gap-2">
            <Link to={hit.link} target="_blank" className="text-primary hover:underline">
              {hit.number ? `${hit.number} · ` : ''}
              {pickText(i18n.language, hit.titleAr, hit.titleEn)}
            </Link>
            <span className="text-xs text-muted-foreground">
              {t('similar.score', { value: Math.round(hit.score * 100) })}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
