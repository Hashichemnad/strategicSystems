import { Link, useRouterState } from '@tanstack/react-router';
import { Lock, Plus } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useLookupValues } from '@/features/settings/lookups';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, Skeleton, StatusBanner, StatusChip } from '@/ui';
import { daysUntil, lockedSensitivities, sensitivityTone, SOON_DAYS, statusTone } from './api';

type Dyn = (key: string, options?: Record<string, unknown>) => string;

/** `t` for keys built from server codes (statuses, validity kinds); the message-key test proves they all exist. */
export function useDocT(): { k: Dyn; language: string } {
  const { t, i18n } = useTranslation('docreg');
  return { k: t as unknown as Dyn, language: i18n.language };
}

export function useFormatDate() {
  const { i18n } = useTranslation();
  return (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : '—');
}

export function useNumber() {
  const { i18n } = useTranslation();
  return (value: number) => formatNumber(value, i18n.language);
}

/** Label of a code of a Settings list (`docreg.level`, `docreg.type`, `docreg.sensitivity`) in the user's language. */
export function useLookupLabel(type: string) {
  const { i18n } = useTranslation();
  const values = useLookupValues(type).data;
  return (code: string | null | undefined) => {
    if (!code) return '—';
    const value = values?.find((v) => v.code === code);
    return (value && pickText(i18n.language, value.labelAr, value.labelEn)) || code;
  };
}

export function documentName(
  language: string,
  doc: { nameAr: string | null; nameEn: string | null },
  fallback: string,
) {
  return pickText(language, doc.nameAr, doc.nameEn) || fallback;
}

export function DocStatusChip({ status }: { status: string }) {
  const { k } = useDocT();
  return (
    <StatusChip tone={statusTone[status] ?? 'neutral'}>
      {k(`status.${status}`, { defaultValue: status })}
    </StatusChip>
  );
}

/** Sensitivity chip, colour-coded; confidential and secret carry a lock icon. */
export function SensitivityChip({ code }: { code: string | null }) {
  const label = useLookupLabel('docreg.sensitivity');
  if (!code) return null;
  return (
    <StatusChip tone={sensitivityTone[code] ?? 'neutral'} className="gap-1">
      {lockedSensitivities.has(code) && <Lock className="size-3" aria-hidden />}
      {label(code)}
    </StatusChip>
  );
}

/** "Expired", "Expires soon" and "Under revision" chips of a document. */
export function ValidityChips({
  status,
  expiryDate,
  underRevision,
}: {
  status: string;
  expiryDate: string | null;
  underRevision: boolean;
}) {
  const { t } = useTranslation('docreg');
  const soon =
    status === 'published' &&
    expiryDate != null &&
    daysUntil(expiryDate) <= SOON_DAYS &&
    daysUntil(expiryDate) >= 0;
  return (
    <>
      {status === 'expired' && <StatusChip tone="danger">{t('chips.expired')}</StatusChip>}
      {soon && <StatusChip tone="warning">{t('chips.expiresSoon')}</StatusChip>}
      {underRevision && <StatusChip tone="info">{t('chips.underRevision')}</StatusChip>}
    </>
  );
}

const navItems = [
  { to: '/documents', key: 'library', exact: true },
  { to: '/documents/dashboard', key: 'dashboard', permission: 'docreg.dashboard.read' },
] as const;

/** Secondary navigation of the Document Registry with "Submit a document". */
export function DocRegNav() {
  const { t } = useTranslation('docreg');
  const canDashboard = useCan('docreg.dashboard.read');
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  return (
    <nav
      aria-label={t('nav.label')}
      className="-mt-2 flex flex-wrap items-center gap-1 border-b pb-2"
      data-testid="docreg-nav"
    >
      {navItems
        .filter((item) => !('permission' in item) || canDashboard)
        .map((item) => {
          const active = 'exact' in item ? pathname === item.to : pathname.startsWith(item.to);
          return (
            <Link
              key={item.key}
              to={item.to}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm hover:bg-muted',
                active ? 'bg-muted font-semibold text-primary' : 'text-muted-foreground',
              )}
              aria-current={active ? 'page' : undefined}
            >
              {t(`nav.${item.key}`)}
            </Link>
          );
        })}
      <Button size="sm" className="ms-auto" asChild>
        <Link
          to="/requests/new/$typeCode"
          params={{ typeCode: 'docreg.document' }}
          data-testid="docreg-submit"
        >
          <Plus aria-hidden />
          {t('nav.submit')}
        </Link>
      </Button>
    </nav>
  );
}

export function DocRegPage({ children, testId }: { children: ReactNode; testId?: string }) {
  return (
    <div className="flex flex-col gap-6" data-testid={testId}>
      <DocRegNav />
      {children}
    </div>
  );
}

/** A bilingual AI draft (Arabic and English side by side), labelled AI-generated until a person uses it (CLAUDE.md §12). */
export function AiDraftPanel({
  title,
  ar,
  en,
  testId,
}: {
  title: string;
  ar: string;
  en: string;
  testId?: string;
}) {
  const { t } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  return (
    <section className="flex flex-col gap-3 rounded-lg border border-info bg-card p-4" data-testid={testId}>
      <header className="flex flex-wrap items-center gap-2">
        <h3 className="font-semibold">{title}</h3>
        <AiBadge label={tc('status.aiGenerated')} />
      </header>
      <div className="grid gap-3 md:grid-cols-2">
        <article dir="rtl" lang="ar" className="whitespace-pre-line rounded-md border p-3 text-sm">
          <h4 className="mb-1 text-xs font-medium text-muted-foreground">{t('ai.arabic')}</h4>
          {ar}
        </article>
        <article dir="ltr" lang="en" className="whitespace-pre-line rounded-md border p-3 text-sm">
          <h4 className="mb-1 text-xs font-medium text-muted-foreground">{t('ai.english')}</h4>
          {en}
        </article>
      </div>
    </section>
  );
}

/** Accessible CSS bars (as the KPI dashboard). */
export function Bars({
  title,
  rows,
  testId,
}: {
  title: string;
  rows: { key: string; label: string; value: number }[];
  testId?: string;
}) {
  const { t } = useTranslation('docreg');
  const num = useNumber();
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={title}
      data-testid={testId}
    >
      <h2 className="font-semibold">{title}</h2>
      {rows.length === 0 && <p className="text-sm text-muted-foreground">{t('common.noData')}</p>}
      <ul className="flex flex-col gap-2">
        {rows.map((row) => (
          <li
            key={row.key}
            className="grid grid-cols-[minmax(6rem,12rem)_1fr_auto] items-center gap-2 text-sm"
          >
            <span className="truncate" title={row.label}>
              {row.label}
            </span>
            <span className="flex h-4 overflow-hidden rounded bg-muted" aria-hidden>
              <span className="h-full bg-primary" style={{ inlineSize: `${(row.value / max) * 100}%` }} />
            </span>
            <span className="tabular-nums text-muted-foreground">{num(row.value)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * In-page viewer of the stamped download (pdf.js, as files/FilePreview: scripting and eval off, glyphs as paths).
 * Loaded on demand, because every download is audited.
 */
export function StampedPdfViewer({ documentId, name }: { documentId: string; name: string }) {
  const { t } = useTranslation('files');
  const canvas = useRef<HTMLCanvasElement>(null);
  const [pdf, setPdf] = useState<import('pdfjs-dist').PDFDocumentProxy | null>(null);
  const [page, setPage] = useState(1);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const state: { cancelled: boolean; loaded?: import('pdfjs-dist').PDFDocumentProxy } = {
      cancelled: false,
    };
    void (async () => {
      try {
        const pdfjs = await import('pdfjs-dist');
        const worker = await import('pdfjs-dist/build/pdf.worker.min.mjs?url');
        pdfjs.GlobalWorkerOptions.workerSrc = worker.default;
        const content = await api.GET('/api/v1/docreg/documents/{id}/download', {
          params: { path: { id: documentId } },
          parseAs: 'arrayBuffer',
        });
        if (!content.response.ok || !content.data) throw new Error(String(content.response.status));
        const loaded = await pdfjs.getDocument({
          data: new Uint8Array(content.data),
          isEvalSupported: false,
          enableXfa: false,
          disableFontFace: true,
          useSystemFonts: false,
        }).promise;
        state.loaded = loaded;
        if (!state.cancelled) setPdf(loaded);
      } catch {
        if (!state.cancelled) setFailed(true);
      }
    })();
    return () => {
      state.cancelled = true;
      void state.loaded?.destroy();
    };
  }, [documentId]);

  useEffect(() => {
    if (!pdf || !canvas.current) return;
    let task: import('pdfjs-dist').RenderTask | null = null;
    void (async () => {
      const current = await pdf.getPage(page);
      const viewport = current.getViewport({ scale: 1.4 });
      const target = canvas.current;
      if (!target) return;
      target.width = viewport.width;
      target.height = viewport.height;
      task = current.render({ canvas: target, viewport });
      await task.promise.catch(() => undefined);
    })();
    return () => task?.cancel();
  }, [pdf, page]);

  if (failed) return <StatusBanner tone="danger" title={t('previewFailed')} />;
  if (!pdf) return <Skeleton className="h-[60vh] w-full rounded" />;
  return (
    <div className="flex flex-col items-center gap-3" data-testid="document-viewer">
      <canvas ref={canvas} className="max-w-full rounded border shadow-sm" aria-label={name} role="img" />
      {pdf.numPages > 1 && (
        <div className="flex items-center gap-3 text-sm">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            {t('previousPage')}
          </Button>
          <span>{t('pageOf', { page, pages: pdf.numPages })}</span>
          <Button
            variant="outline"
            size="sm"
            disabled={page >= pdf.numPages}
            onClick={() => setPage((p) => p + 1)}
          >
            {t('nextPage')}
          </Button>
        </div>
      )}
    </div>
  );
}
