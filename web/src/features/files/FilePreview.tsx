import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { Button, Skeleton, StatusBanner } from '@/ui';
import { contentUrl, type StoredFile } from './api';

/**
 * In-page preview (CLAUDE.md §14): images as images, PDFs drawn page by page with pdf.js (Apache-2.0; scripting and eval off,
 * worker served from the app; glyphs drawn as paths, because the app's CSP forbids the inline @font-face pdf.js would inject). Other types are downloaded instead.
 */
export function FilePreview({ file, versionId }: { file: StoredFile; versionId?: string }) {
  const url = contentUrl(file.id, versionId);
  if (file.contentType.startsWith('image/')) {
    return (
      <img
        src={url}
        alt={file.name}
        className="mx-auto max-h-[70vh] max-w-full rounded border object-contain"
      />
    );
  }

  if (file.contentType === 'application/pdf')
    return <PdfPreview fileId={file.id} versionId={versionId} name={file.name} />;
  return null;
}

export function canPreview(contentType: string): boolean {
  return contentType.startsWith('image/') || contentType === 'application/pdf';
}

function PdfPreview({ fileId, versionId, name }: { fileId: string; versionId?: string; name: string }) {
  const { t } = useTranslation('files');
  const canvas = useRef<HTMLCanvasElement>(null);
  const [document, setDocument] = useState<import('pdfjs-dist').PDFDocumentProxy | null>(null);
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
        const content = await api.GET('/api/v1/files/{fileId}/content', {
          params: { path: { fileId }, query: versionId ? { versionId } : {} },
          parseAs: 'arrayBuffer',
        });
        if (!content.response.ok || !content.data) throw new Error(String(content.response.status));
        const loaded = await pdfjs.getDocument({
          data: new Uint8Array(content.data),
          isEvalSupported: false,
          enableXfa: false,
          // Glyphs as paths: the app's CSP forbids the inline @font-face pdf.js would inject.
          disableFontFace: true,
          // Never substitute a font of the same name (the SPA loads IBM Plex itself): draw the PDF's own embedded glyphs.
          useSystemFonts: false,
        }).promise;
        state.loaded = loaded;
        if (!state.cancelled) setDocument(loaded);
      } catch {
        if (!state.cancelled) setFailed(true);
      }
    })();
    return () => {
      state.cancelled = true;
      void state.loaded?.destroy();
    };
  }, [fileId, versionId]);

  useEffect(() => {
    if (!document || !canvas.current) return;
    let task: import('pdfjs-dist').RenderTask | null = null;
    void (async () => {
      const current = await document.getPage(page);
      const viewport = current.getViewport({ scale: 1.4 });
      const target = canvas.current;
      if (!target) return;
      target.width = viewport.width;
      target.height = viewport.height;
      task = current.render({ canvas: target, viewport });
      await task.promise.catch(() => undefined);
    })();
    return () => task?.cancel();
  }, [document, page]);

  if (failed) return <StatusBanner tone="danger" title={t('previewFailed')} />;
  if (!document) return <Skeleton className="h-[60vh] w-full rounded" />;
  return (
    <div className="flex flex-col items-center gap-3">
      <canvas ref={canvas} className="max-w-full rounded border shadow-sm" aria-label={name} role="img" />
      {document.numPages > 1 && (
        <div className="flex items-center gap-3 text-sm">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            {t('previousPage')}
          </Button>
          <span>{t('pageOf', { page, pages: document.numPages })}</span>
          <Button
            variant="outline"
            size="sm"
            disabled={page >= document.numPages}
            onClick={() => setPage((p) => p + 1)}
          >
            {t('nextPage')}
          </Button>
        </div>
      )}
    </div>
  );
}
